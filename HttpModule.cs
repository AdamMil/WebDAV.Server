using System;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Web;
using HiA.WebDAV.Configuration;

namespace HiA.WebDAV
{

/// <summary>Implements an <see cref="HttpModule"/> that provides WebDAV services.</summary>
public sealed class HttpModule : IHttpModule
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

  #region Configuration
  /// <summary>Represents the configuration of the WebDAV module.</summary>
  sealed class Configuration
  {
    public Configuration(WebDAVSection config)
    {
      Enabled = config.Enabled;
      if(Enabled)
      {
        Locations = new LocationConfig[config.Locations.Count];
        for(int i=0; i<Locations.Length; i++) Locations[i] = new LocationConfig(config.Locations[i]);
      }
    }

    /// <summary>The <see cref="LocationConfig"/> objects representing the WebDAV services defined in the configuration, or null if
    /// the WebDAV module is disabled (i.e. <see cref="Enabled"/> is false).
    /// </summary>
    public readonly LocationConfig[] Locations;
    /// <summary>Whether the WebDAV module is enabled. If false, the WebDAV module will not process any requests.</summary>
    public readonly bool Enabled;
  }
  #endregion

  #region AuthFilterConfig
  sealed class AuthFilterConfig
  {
    public AuthFilterConfig(AuthorizationFilterElement config)
    {
      filterCreator = GetCreationDelegate<IAuthorizationFilter>(config.Type, config.Parameters);
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
        providerCreator = GetCreationDelegate<IWebDAVService>(config.Type, config.Parameters);
        AuthFilters     = new AuthFilterConfig[config.AuthorizationFilters.Count];
        for(int i=0; i<AuthFilters.Length; i++) AuthFilters[i] = new AuthFilterConfig(config.AuthorizationFilters[i]);
      }
    }

    public string RootPath
    {
      get { return path ?? "/"; }
    }

    public void ClearSharedService()
    {
      sharedService = null;
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
      return (port == 0 || port == request.Url.Port) &&
             (hostname == null || request.Url.HostNameType == UriHostNameType.Dns &&
                                  string.Equals(hostname, request.Url.Host, StringComparison.OrdinalIgnoreCase)) &&
             (scheme == null || string.Equals(scheme, request.Url.Scheme, StringComparison.Ordinal)) &&
             (ip == null || IpMatches(request)) &&
             (path == null || PathMatches(request.Url));
    }

    public readonly bool Enabled;
    public readonly AuthFilterConfig[] AuthFilters;

    bool IpMatches(HttpRequest request)
    {
      throw new NotImplementedException(); // TODO: implement IP-based matching
    }

    void ParseMatch(string matchString)
    {
      Match m = new Regex(LocationElement.MatchPattern).Match(matchString);
      if(!m.Success) throw new ConfigurationErrorsException(matchString + " is not a valid HiA.WebDAV location match string.");

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

      // TODO: what about escaped characters? should they be allowed in the match string? (currently, they are not)
      if(m.Groups["path"].Success)
      {
        path = m.Groups["path"].Value;
        if(path[path.Length-1] != '/') path += "/"; // make sure it has a trailing slash
      }
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
          try
          {
            ProcessRequest(application, location);
          }
          catch(HttpException ex) // HttpExceptions might be okay (this includes WebDAVException)
          {
            int httpCode = ex.GetHttpCode();
            if(httpCode >= 500 && httpCode < 600) throw; // 500 errors represent server failure, so propagate the error to ASP.NET
            // lesser errors are the client's fault and don't represent server failure, so send the response to the client as normal
            SetErrorResponse(application, httpCode, ex.Message);
          }
          catch(UnauthorizedAccessException ex)
          {
            SetErrorResponse(application, (int)HttpStatusCode.Forbidden, ex.Message); // TODO: does using ex.Message reveal too much?
          }
          catch // if an non-WebDAV error occurs, prevent the service from being reused for future requests and propagate it to ASP.NET
          {
            location.ClearSharedService();
            throw;
          }
          application.CompleteRequest();
        }

        break; // in any case, once we've found the right location, we're done
      }
    }
  }

  /// <summary>Processes a request based on an HTTP method, given functions to create and process the <see cref="WebDAVRequest"/>
  /// appropriate to that HTTP method.
  /// </summary>
  void ProcessMethod<T>(WebDAVContext context, Func<WebDAVContext, T> createMethod, Action<T> process) where T : WebDAVRequest
  {
    T request = createMethod(context);
    try { request.ParseRequest(); }
    catch(System.Xml.XmlException ex) { throw new WebDAVException((int)HttpStatusCode.BadRequest, "Invalid XML body. " + ex.Message, ex); }
    process(request);
    request.WriteResponse();
  }

  /// <summary>Processes a WebDAV request using the service represented by the given <see cref="LocationConfig"/>.</summary>
  void ProcessRequest(HttpApplication app, LocationConfig config)
  {
    // create the request context and service instance
    WebDAVContext context = new WebDAVContext(config.RootPath, config.GetRelativeUrl(app.Request.Url), app);
    IWebDAVService service = config.GetService();

    // resolve the request Uri into a WebDAV resource
    context.RequestResource = service.ResolveResource(context);

    // if the resource was not found, return a 404 Not Found result
    if(context.RequestResource == null)
    {
      SetNotFoundResponse(app);
      return;
    }

    // perform authorization against the resource using authorization filters first
    foreach(AuthFilterConfig filterConfig in config.AuthFilters)
    {
      if(ShouldDenyAccess(app, filterConfig.GetFilter(), context)) return;
    }

    // then check the resource's built-in authorization
    if(ShouldDenyAccess(app, context.RequestResource, context)) return;

    // now process the WebDAV operation against the resource based on the HTTP method
    if(app.Request.HttpMethod.OrdinalEquals("PROPFIND"))
    {
      ProcessMethod<PropFindRequest>(context, service.CreatePropFind, context.RequestResource.PropFind);
    }
    else if(app.Request.HttpMethod.OrdinalEquals("PROPPATCH"))
    {
      ProcessMethod<PropPatchRequest>(context, service.CreatePropPatch, context.RequestResource.PropPatch);
    }
    else
    {
      throw new NotImplementedException(); // TODO: implement support for arbitrary methods
    }
  }

  /// <summary>Disposes resources related to the <see cref="HttpModule"/>. Note that this method is distinct from
  /// <see cref="IDisposable.Dispose"/>.
  /// </summary>
  void IHttpModule.Dispose()
  {
    // we have nothing to dispose (and we don't need to remove the event handler delegate because we hold no reference to HttpApplication)
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
                                           "that accepts a System.Configuration.ConfigurationElement object.");
  }

  /// <summary>Sets the status code to the given status code and writes an error message to the page.</summary>
  static void SetErrorResponse(HttpApplication app, int httpStatusCode, string errorText)
  {
    HttpResponse response = app.Response;
    response.ContentType = "text/plain";
    response.StatusCode  = httpStatusCode;
    response.Write(string.Format(CultureInfo.InvariantCulture, "{0}\n{1} {2}\n", app.Request.Url, httpStatusCode, errorText));
  }

  /// <summary>Sets a 404 Not Found status response for the request URI.</summary>
  static void SetNotFoundResponse(HttpApplication app)
  {
    SetErrorResponse(app, (int)HttpStatusCode.NotFound, "Resource could not be found.");
  }

  /// <summary>Attempts to authorize the user in the context of the current request. If true is returned, the user is immediately denied
  /// access. If false is returned, the user will be granted access only if all other authorization methods allow it.
  /// </summary>
  static bool ShouldDenyAccess(HttpApplication app, ISupportAuthorization authProvider, WebDAVContext context)
  {
    bool denyExistence;
    if(!authProvider.ShouldDenyAccess(context, out denyExistence)) return false;

    if(denyExistence) SetNotFoundResponse(app); // if we should deny even the existence of the resource, return a 404
    else SetErrorResponse(app, (int)HttpStatusCode.Forbidden, "Access denied to " + app.Request.Url.AbsolutePath);
    return false;
  }

  /// <summary>The configuration of the WebDAV module.</summary>
  static readonly Configuration config = new Configuration(WebDAVSection.Get());
}

} // namespace HiA.WebDAV