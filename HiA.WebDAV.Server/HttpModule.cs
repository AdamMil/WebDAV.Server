/*
 * Version: EUPL 1.1
 * 
 * The contents of this file are subject to the European Union Public Licence Version 1.1 (the "Licence"); 
 * you may not use this file except in compliance with the Licence. 
 * You may obtain a copy of the Licence at:
 * http://joinup.ec.europa.eu/software/page/eupl/licence-eupl
 */
using System;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Web;
using HiA.WebDAV.Server.Configuration;

// TODO: we should use 405 Method Not Found in more places (but it's difficult because RFC 2616 section 10.4.6 requires supplying an Allow
// header containing a list of legal methods, but it would be nontrivial to construct such a header... maybe we can do it centrally,
// though, in WebDAVModule)

// TODO: look into Microsoft's WebDAV extensions
// TODO: support the Expects header (RFC 2616 section 14.20) if IIS doesn't do it for us
// TODO: section 8.3 says that href elements in multi-status responses must not have prefixes that don't match the request URI, but we
// might do that sometimes... check for it and see what we can do about it...
// TODO: see if we can annotate the code with reliability attributes. (http://msdn.microsoft.com/en-us/magazine/cc163716.aspx)

namespace HiA.WebDAV.Server
{

/// <summary>Implements an <see cref="IHttpModule"/> that provides WebDAV services.</summary>
public sealed class WebDAVModule : IHttpModule
{
  /// <summary>Initializes the WebDAV module, hooking into the ASP.NET pipeline if the module has not been disabled in the application
  /// configuration file.
  /// </summary>
  public void Init(HttpApplication context)
  {
    if(config.Enabled) // if the WebDAV module is enabled...
    {
      if(context == null) throw new ArgumentNullException();     // validate the argument
      context.PostAuthenticateRequest += OnRequestAuthenticated; // and attach to the ASP.NET pipeline after authentication is complete
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVModule/ResolveUri/node()" />
  public static bool ResolveUri(WebDAVContext context, Uri uri, bool performAccessChecks,
                                out IWebDAVService service, out IWebDAVResource resource)
  {
    string serviceRoot, relativePath;
    return ResolveUri(context, uri, performAccessChecks, out service, out resource, out serviceRoot, out relativePath);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVModule/ResolveUri/node()" />
  /// <param name="serviceRoot">A variable that will receive the absolute path to the <paramref name="service"/> root. This variable will
  /// be valid whenever <paramref name="service"/> is not null.
  /// </param>
  /// <param name="relativePath">A variable that will receive the path to the resource named by the <paramref name="uri"/>, relative to
  /// the <paramref name="serviceRoot"/>. This variable will be valid whenever <paramref name="service"/> is not null.
  /// </param>
  public static bool ResolveUri(WebDAVContext context, Uri uri, bool performAccessChecks, out IWebDAVService service,
                                out IWebDAVResource resource, out string serviceRoot, out string relativePath)
  {
    if(context == null || uri == null) throw new ArgumentNullException();

    // if the absolute URI doesn't have an authority section, use the authority from the request
    if(!uri.IsAbsoluteUri)
    {
      string path = uri.ToString();
      if(path.Length == 0 || path[0] != '/')
      {
        throw new ArgumentException("The uri must be either an absolute URI or a URI constructed from an absolute path.");
      }
      uri = new Uri(context.Request.Url.GetLeftPart(UriPartial.Authority) + path);
    }

    service      = null;
    resource     = null;
    serviceRoot  = null;
    relativePath = null;

    // find the service that matches the URL, if any
    foreach(LocationConfig location in config.Locations)
    {
      if(location.MatchesRequest(uri))
      {
        if(location.Enabled)
        {
          serviceRoot  = location.RootPath;
          relativePath = location.GetRelativeUrl(uri);
          service      = location.GetService();
          resource     = service.ResolveResource(context, relativePath);

          if(performAccessChecks)
          {
            foreach(AuthFilterConfig filterConfig in location.AuthFilters)
            {
              if(ShouldDenyAccess(context, filterConfig.GetFilter()))
              {
                resource = null;
                break;
              }
            }
            if(resource != null && ShouldDenyAccess(context, resource)) resource = null;
          }
        }

        return resource != null;
      }
    }

    return false;
  }

  #region Configuration
  /// <summary>Represents the configuration of the WebDAV module.</summary>
  sealed class Configuration
  {
    public Configuration(WebDAVServerSection config)
    {
      Enabled             = config.Enabled;
      ShowSensitiveErrors = config.ShowSensitiveErrors;
      if(Enabled)
      {
        Locations = new LocationConfig[config.Locations.Count];
        for(int i=0; i<Locations.Length; i++) Locations[i] = new LocationConfig(config.Locations[i]);
      }

      if(config.LockManager != null && config.LockManager.Type != null)
      {
        ConstructorInfo cons = config.LockManager.Type.GetConstructor(new Type[] { typeof(ParameterCollection) });
        if(cons != null)
        {
          LockManager = (ILockManager)cons.Invoke(new object[] { config.LockManager.Parameters });
        }
        else
        {
          cons = config.LockManager.Type.GetConstructor(Type.EmptyTypes);
          if(cons != null)
          {
            LockManager = (ILockManager)cons.Invoke(null);
          }
          else
          {
            throw new ConfigurationErrorsException(config.LockManager.Type.FullName + " does not have either a public default " +
                                                   "constructor or a public constructor that accepts a " +
                                                   typeof(ParameterCollection).FullName + " object.");
          }
        }
      }

      ContextSettings = new WebDAVContext.Configuration(ShowSensitiveErrors);

      AppDomain.CurrentDomain.DomainUnload += OnDomainUnloading;
    }

    /// <summary>Gets the <see cref="WebDAVContext.Configuration"/> object that should be used when servicing requests.</summary>
    public WebDAVContext.Configuration ContextSettings;
    /// <summary>Gets the <see cref="ILockManager"/> defined in the configuration.</summary>
    public readonly ILockManager LockManager;
    /// <summary>The <see cref="LocationConfig"/> objects representing the WebDAV services defined in the configuration, or null if
    /// the WebDAV module is disabled (i.e. <see cref="Enabled"/> is false).
    /// </summary>
    public readonly LocationConfig[] Locations;
    /// <summary>Whether the WebDAV module is enabled. If false, the WebDAV module will not process any requests.</summary>
    public readonly bool Enabled;
    /// <summary>Whether to show potentially sensitive error messages when an exception occurs.</summary>
    public readonly bool ShowSensitiveErrors;

    void OnDomainUnloading(object sender, EventArgs e)
    {
      // dispose things when the domain is about to be unloaded because if we wait for the finalizer, things may be too messed up to be
      // disposed properly. (e.g. FileStreams may already be closed, so we can't save data like locks)
      if(Locations != null)
      {
        foreach(LocationConfig location in Locations) location.Dispose();
      }
      if(LockManager != null) LockManager.Dispose();
    }
  }
  #endregion

  #region AuthFilterConfig
  sealed class AuthFilterConfig
  {
    public AuthFilterConfig(AuthorizationFilterElement config)
    {
      filterCreator = GetCreationDelegate<IAuthorizationFilter>(config.Type, config.Parameters);
    }

    public void Dispose()
    {
      Utility.Dispose(sharedFilter);
      sharedFilter = null;
    }

    public IAuthorizationFilter GetFilter()
    {
      IAuthorizationFilter filter = sharedFilter;
      if(filter == null)
      {
        filter = filterCreator();
        if(filter.IsReusable) sharedFilter = filter;
      }
      return filter;
    }

    readonly Func<IAuthorizationFilter> filterCreator;
    IAuthorizationFilter sharedFilter;
  }
  #endregion

  #region LocationConfig
  sealed class LocationConfig
  {
    public LocationConfig(LocationElement config)
    {
      Enabled       = config.Enabled;
      caseSensitive = config.CaseSensitive;
      ParseMatch(config.Match);

      if(Enabled)
      {
        providerCreator  = GetCreationDelegate<IWebDAVService>(config.Type, config.Parameters);
        AuthFilters      = new AuthFilterConfig[config.AuthorizationFilters.Count];
        ServeRootOptions = config.ServeRootOptions;
        for(int i=0; i<AuthFilters.Length; i++) AuthFilters[i] = new AuthFilterConfig(config.AuthorizationFilters[i]);
      }
    }

    public string RootPath { get; private set; }
    public bool ServeRootOptions { get; private set; }

    public void ClearSharedService()
    {
      sharedService = null;
    }

    public void Dispose()
    {
      Utility.Dispose(sharedService);
      sharedService = null;
      if(AuthFilters != null)
      {
        foreach(AuthFilterConfig authFilter in AuthFilters) authFilter.Dispose();
      }
    }

    /// <summary>Returns a <see cref="Uri"/> relative to the root of the WebDAV service.</summary>
    public string GetRelativeUrl(Uri requestUri)
    {
      string requestPath = requestUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
      return path == null ? requestPath : path.Length >= requestPath.Length ? "" : requestPath.Substring(path.Length);
    }

    public IWebDAVService GetService()
    {
      IWebDAVService service = sharedService;
      if(service == null)
      {
        service = providerCreator();
        if(service.IsReusable) sharedService = service;
      }
      return service;
    }

    public bool MatchesRequest(HttpRequest request)
    {
      return MatchesRequest(request.Url) && (ip == null || IpMatches(request));
    }

    public bool MatchesRequest(Uri uri)
    {
      return (port == 0 || port == uri.Port) &&
             (hostname == null || uri.HostNameType == UriHostNameType.Dns &&
                                  string.Equals(hostname, uri.Host, StringComparison.OrdinalIgnoreCase)) &&
             (scheme == null || string.Equals(scheme, uri.Scheme, StringComparison.Ordinal)) &&
             (path == null || PathMatches(uri));
    }

    public readonly bool Enabled;
    public readonly AuthFilterConfig[] AuthFilters;

    bool IpMatches(HttpRequest request)
    {
      throw new NotImplementedException(); // TODO: implement IP-based matching, if possible
    }

    void ParseMatch(string matchString)
    {
      Match m = new Regex(LocationElement.MatchPattern).Match(matchString);
      if(!m.Success) throw new ConfigurationErrorsException(matchString + " is not a valid HiA.WebDAV.Server location match string.");

      if(m.Groups["scheme"].Success) scheme = m.Groups["scheme"].Value.ToLowerInvariant();

      if(m.Groups["hostname"].Success)
      {
        hostname = m.Groups["hostname"].Value;
      }
      else if(m.Groups["ipv4"].Success || m.Groups["ipv6"].Success)
      {
        string ipStr = m.Groups["ipv4"].Success ? m.Groups["ipv4"].Value : m.Groups["ipv6"].Value;
        try { ip = IPAddress.Parse(ipStr); }
        catch(FormatException ex) { throw new ConfigurationErrorsException(ipStr + " is not a valid IP address.", ex); }
      }

      if(m.Groups["port"].Success) port = int.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture);

      if(m.Groups["path"].Success) path = DAVUtility.WithTrailingSlash(m.Groups["path"].Value);
      RootPath = "/" + path;
    }

    bool PathMatches(Uri requestUri)
    {
      StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      string requestPath = requestUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
      // the match path includes the trailing slash, so if the request path is greater or equal in length, then the match path must be a
      // prefix of it. otherwise, if the request path is shorter, they can only match if the request path is equal to the match path when
      // the trailing slash of the match path is ignored (e.g. the match path is /dav/ but the user type /dav -- we'll accept this).
      return requestPath.Length >= path.Length ? requestPath.StartsWith(path, comparison)
                                               : requestPath.Length+1 == path.Length && path.StartsWith(requestPath, comparison);
    }

    readonly Func<IWebDAVService> providerCreator;
    string scheme, hostname, path;
    IPAddress ip;
    IWebDAVService sharedService;
    int port;
    readonly bool caseSensitive;
  }
  #endregion

  /// <summary>Occurs after the request has been authenticated. This is where the WebDAV module works its magic.</summary>
  void OnRequestAuthenticated(object sender, EventArgs e)
  {
    HttpApplication application = (HttpApplication)sender;
    foreach(LocationConfig location in config.Locations)
    {
      if(location.MatchesRequest(application.Request))
      {
        if(location.Enabled)
        {
          bool endRequest = true;
          try
          {
            endRequest = ProcessRequest(application, location);
          }
          catch(HttpException ex) // HttpExceptions might be okay (this includes WebDAVException)
          {
            WebDAVException wde = ex as WebDAVException; // if it's a WebDAVException, use the ConditionCode if it's available
            if(wde != null && wde.ConditionCode != null)
            {
              WriteErrorResponse(application, wde.ConditionCode);
            }
            else
            {
              // 5xx errors represent server failure, so propagate the error to ASP.NET. lesser errors are typically the client's fault
              // and don't represent server failure, so send the response to the client as normal
              int httpCode = ex.GetHttpCode();
              if(httpCode >= 500 && httpCode < 600) throw;
              else WriteErrorResponse(application, httpCode, ex.Message);
            }
          }
          catch(UnauthorizedAccessException ex)
          {
            WriteErrorResponse(application, (int)HttpStatusCode.Forbidden, FilterErrorMessage(ex.Message));
          }
          catch(System.Threading.ThreadAbortException) { throw; } // don't throw away the service due to exceptions from Response.End, etc
          catch // if an non-WebDAV error occurs, prevent the service from being reused for future requests and propagate it to ASP.NET
          {
            location.ClearSharedService();
            throw;
          }

          if(endRequest) application.CompleteRequest();
        }

        break; // in any case, once we've found the right location, we're done
      }
      // if the service is configured to serve OPTIONS requests to the root, and this is such a request, then we'll handle it also
      else if(location.Enabled && location.ServeRootOptions && application.Request.HttpMethod.OrdinalEquals(HttpMethods.Options) &&
              (string.Equals(application.Request.Url.AbsolutePath, "/", StringComparison.Ordinal) ||
               string.Equals(application.Request.RawUrl, "*", StringComparison.Ordinal)))
      {
        using(WebDAVContext context = CreateContext(location, "", application, config))
        {
          OptionsRequest request = context.Service.CreateOptions(context);
          request.SetOutOfScope(); // enable special processing within OptionsRequest for out-of-scope requests
          ProcessMethod(context, request, context.Service.Options);
        }
        break; // don't complete the request because we only want to add our custom headers to ASP.NET's usual response
      }
    }
  }

  /// <summary>Disposes resources related to the <see cref="IHttpModule"/>. Note that this method is distinct from
  /// <see cref="IDisposable.Dispose"/>.
  /// </summary>
  void IHttpModule.Dispose()
  {
    // we have nothing to dispose (and we don't need to remove the event handler delegate because we hold no reference to HttpApplication)
  }

  static WebDAVContext CreateContext(LocationConfig location, string requestPath, HttpApplication application, Configuration config)
  {
    return new WebDAVContext(location.GetService(), location.RootPath, requestPath, application,
                             config.LockManager, config.ContextSettings);
  }

  /// <summary>Returns the given string or null, depending on the value of <see cref="Configuration.ShowSensitiveErrors"/>.</summary>
  static string FilterErrorMessage(string message)
  {
    return config.ShowSensitiveErrors ? message : null;
  }

  /// <summary>Creates a <see cref="Func{T}"/> delegate that will instantiate and return a new object of type <typeparamref name="T"/>,
  /// which must be a reference type. If the object has a public constructor that accepts a <see cref="ParameterCollection"/> object, that
  /// constructor will be used. Otherwise, the public default constructor will be used if it's available. If neither constructor is
  /// available, a <see cref="ConfigurationErrorsException"/> will be thrown.
  /// </summary>
  /// <remarks>This method exists to avoid the performance penalty that we would incur if we used reflection or the C# "new T()"
  /// expression (which uses reflection internally).
  /// </remarks>
  static Func<T> GetCreationDelegate<T>(Type type, ParameterCollection parameters)
  {
    // we don't allow value types because it complicates the code generation, but they could be supported if somebody really wanted it
    if(type.IsValueType)
    {
      throw new ConfigurationErrorsException(type.FullName + " is a value type, which is not allowed. Use a reference type.");
    }

    // if the type has a public constructor that accepts a ConfigurationElement, use that...
    ConstructorInfo cons = type.GetConstructor(new Type[] { typeof(ParameterCollection) });
    if(cons != null)
    {
      // compile a method whose body is essentially "return new T(this);" where 'this' is a reference to the ParameterCollection
      DynamicMethod method = new DynamicMethod("Create" + type.Name, typeof(T), new Type[] { typeof(ParameterCollection) });
      ILGenerator il = method.GetILGenerator();
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Newobj, cons);
      il.Emit(OpCodes.Ret);
      return (Func<T>)method.CreateDelegate(typeof(Func<T>), parameters);
    }

    // otherwise, see if it has a default constructor
    cons = type.GetConstructor(Type.EmptyTypes);
    if(cons != null)
    {
      // compile a method whose body is just "return new T();"
      DynamicMethod method = new DynamicMethod("Create" + type.Name, typeof(T), Type.EmptyTypes);
      ILGenerator il = method.GetILGenerator();
      il.Emit(OpCodes.Newobj, cons);
      il.Emit(OpCodes.Ret);
      return (Func<T>)method.CreateDelegate(typeof(Func<T>));
    }

    // neither type of constructor was available for use, so throw an exception
    throw new ConfigurationErrorsException(type.FullName + " does not have either a public default constructor or a public constructor " +
                                           "that accepts a " + typeof(ParameterCollection).FullName + " object.");
  }

  /// <summary>Processes a request based on an HTTP method, given a function to process the <see cref="WebDAVRequest"/> appropriate to
  /// that HTTP method.
  /// </summary>
  static void ProcessMethod<T>(WebDAVContext context, T request, Action<T> process) where T : WebDAVRequest
  {
    try
    {
      request.ParseRequest();
    }
    catch(System.Xml.XmlException ex) // if an XML exception occurred when parsing the request, assume it was caused by an invalid XML
    {                                 // request sent by the client
      context.WriteStatusResponse(new ConditionCode(HttpStatusCode.BadRequest, "Invalid XML body. " + ex.Message));
      return;
    }
    process(request);
    request.WriteResponse();
    Utility.Dispose(request);
  }

  /// <summary>Processes a WebDAV request using the service represented by the given <see cref="LocationConfig"/>. Returns whether the
  /// request was handled and should be completed.
  /// </summary>
  static bool ProcessRequest(HttpApplication app, LocationConfig config)
  {
    // create the request context and service instance
    using(WebDAVContext context = CreateContext(config, config.GetRelativeUrl(app.Request.Url), app, WebDAVModule.config))
    {
      // resolve the request Uri into a WebDAV resource
      context.RequestResource = context.Service.ResolveResource(context, context.RequestPath);

      // regardless of whether resolution succeeded, perform authorization using authorization filters first
      foreach(AuthFilterConfig filterConfig in config.AuthFilters)
      {
        if(ShouldDenyAccess(context, filterConfig.GetFilter())) return true;
      }

      string method = app.Request.HttpMethod;
      if(context.RequestResource == null) // if the resource was not found...
      {
        // some verbs (like MKCOL and PUT) can be executed on unmapped URLs. if it's not a verb we care about and the service also declines
        // to handle it, issue a 404 Not Found response
        if(method.OrdinalEquals(HttpMethods.Options))
        {
          ProcessMethod(context, context.Service.CreateOptions(context), context.Service.Options);
        }
        else if(method.OrdinalEquals(HttpMethods.Put))
        {
          ProcessMethod(context, context.Service.CreatePut(context), context.Service.Put);
        }
        else if(method.OrdinalEquals(HttpMethods.Lock))
        {
          ProcessMethod(context, context.Service.CreateLock(context), context.Service.CreateAndLock);
        }
        else if(method.OrdinalEquals(HttpMethods.Post))
        {
          ProcessMethod(context, context.Service.CreatePost(context), context.Service.Post);
        }
        else if(method.OrdinalEquals(HttpMethods.MkCol))
        {
          ProcessMethod(context, context.Service.CreateMkCol(context), context.Service.MakeCollection);
        }
        else if(method.OrdinalEquals(HttpMethods.Unlock))
        {
          ProcessMethod(context, context.Service.CreateUnlock(context), context.Service.Unlock);
        }
        else if(!context.Service.HandleGenericRequest(context))
        {
          if(method.OrdinalEquals(HttpMethods.Trace)) return false; // let ASP.NET handle TRACE requests if the WebDAV service didn't
          WriteNotFoundResponse(app);
        }
      }
      else // otherwise, the URL was mapped to a resource
      {
        // check the resource's built-in authorization
        if(ShouldDenyAccess(context, context.RequestResource)) return true;

        // now process the WebDAV operation against the resource based on the HTTP method
        if(method.OrdinalEquals(HttpMethods.PropFind))
        {
          ProcessMethod(context, context.Service.CreatePropFind(context), context.RequestResource.PropFind);
        }
        else if(method.OrdinalEquals(HttpMethods.Get) || method.OrdinalEquals(HttpMethods.Head))
        {
          ProcessMethod(context, context.Service.CreateGetOrHead(context), context.RequestResource.GetOrHead);
        }
        else if(method.OrdinalEquals(HttpMethods.Options))
        {
          ProcessMethod(context, context.Service.CreateOptions(context), context.RequestResource.Options);
        }
        else if(method.OrdinalEquals(HttpMethods.Lock))
        {
          ProcessMethod(context, context.Service.CreateLock(context), context.RequestResource.Lock);
        }
        else if(method.OrdinalEquals(HttpMethods.Unlock))
        {
          ProcessMethod(context, context.Service.CreateUnlock(context), context.RequestResource.Unlock);
        }
        else if(method.OrdinalEquals(HttpMethods.Put))
        {
          ProcessMethod(context, context.Service.CreatePut(context), context.RequestResource.Put);
        }
        else if(method.OrdinalEquals(HttpMethods.PropPatch))
        {
          ProcessMethod(context, context.Service.CreatePropPatch(context), context.RequestResource.PropPatch);
        }
        else if(method.OrdinalEquals(HttpMethods.Copy) || method.OrdinalEquals(HttpMethods.Move))
        {
          ProcessMethod(context, context.Service.CreateCopyOrMove(context), context.RequestResource.CopyOrMove);
        }
        else if(method.OrdinalEquals(HttpMethods.Delete))
        {
          ProcessMethod(context, context.Service.CreateDelete(context), context.RequestResource.Delete);
        }
        else if(method.OrdinalEquals(HttpMethods.Post))
        {
          ProcessMethod(context, context.Service.CreatePost(context), context.RequestResource.Post);
        }
        else if(method.OrdinalEquals(HttpMethods.MkCol))
        {
          // MKCOL is not allowed on mapped URLs as per RFC 4918 section 9.3. we'll respond with 405 Method Not Allowed as per section
          // 9.3.1. this requires adding an Allow header that describes the allowed methods. the easiest way to do that is to process it as
          // though it was an OPTIONS request, so that's what we'll do
          ProcessMethod(context, context.Service.CreateOptions(context), request =>
          {
            context.RequestResource.Options(request);
            request.Status = new ConditionCode(HttpStatusCode.MethodNotAllowed, "A resource already exists there.");
          });
        }
        else if(!context.RequestResource.HandleGenericRequest(context) && !context.Service.HandleGenericRequest(context))
        {
          if(method.OrdinalEquals(HttpMethods.Trace)) return false; // let ASP.NET handle TRACE requests if the WebDAV service didn't
          WriteErrorResponse(app, (int)HttpStatusCode.Forbidden, "The WebDAV service declined to respond to this request.");
        }
      }
    }

    return true;
  }

  /// <summary>Attempts to authorize the user in the context of the current request. If true is returned, the user is immediately denied
  /// access. If false is returned, the user will be granted access only if all other authorization methods allow it.
  /// </summary>
  static bool ShouldDenyAccess(WebDAVContext context, ISupportAuthorization authProvider)
  {
    bool denyExistence;
    if(!authProvider.ShouldDenyAccess(context, out denyExistence)) return false;

    if(denyExistence) WriteNotFoundResponse(context.Application); // if we should deny even the existence of the resource, return a 404
    else WriteErrorResponse(context.Application, (int)HttpStatusCode.Forbidden, "Access denied.");
    return false;
  }

  /// <summary>Sets a 404 Not Found status response for the request URI.</summary>
  static void WriteNotFoundResponse(HttpApplication app)
  {
    WriteErrorResponse(app, (int)HttpStatusCode.NotFound, "The requested resource could not be found.");
  }

  /// <summary>Sets the response status code to the given status code and writes an error message to the page.</summary>
  static void WriteErrorResponse(HttpApplication app, int httpStatusCode, string errorText)
  {
    DAVUtility.WriteStatusResponse(app.Request, app.Response, httpStatusCode, errorText);
  }

  /// <summary>Sets the response status code to the given status code and writes an error response based on the given
  /// <see cref="ConditionCode"/>.
  /// </summary>
  static void WriteErrorResponse(HttpApplication app, ConditionCode code)
  {
    DAVUtility.WriteStatusResponse(app.Request, app.Response, code);
  }

  /// <summary>The configuration of the WebDAV module.</summary>
  static readonly Configuration config = new Configuration(WebDAVServerSection.Get());
}

} // namespace HiA.WebDAV.Server
