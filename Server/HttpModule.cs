/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2015 by Adam Milazzo.

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

// TODO: what access type should ShouldDenyAccess use for LOCK and UNLOCK requests? write? a custom type? the type submitted in the body?
// TODO: in places where we use 405 Method Not Found, RFC 7231 section 6.5.5 requires supplying an Allow header containing a
// list of legal methods, but it would be nontrivial to construct such a header... maybe we can do it centrally, though, in
// WebDAVModule, or by adding an IWebDAVResource/Service.GetSupportedMethods method
// TODO: look into Microsoft's WebDAV extensions
// TODO: see if we can annotate the code with reliability attributes. (http://msdn.microsoft.com/en-us/magazine/cc163716.aspx)

namespace AdamMil.WebDAV.Server
{

#region UriResolution
/// <summary>Contains the results of an attempt by <see cref="WebDAVModule.ResolveUri"/> to resolve a <see cref="Uri"/> to an
/// <see cref="IWebDAVService"/> and <see cref="IWebDAVResource"/>.
/// </summary>
public sealed class UriResolution
{
  /// <summary>Gets whether access should be denied to the resource. This will always be false if the <c>performAccessChecks</c> argument
  /// to <see cref="WebDAVModule.ResolveUri"/> was false.
  /// </summary>
  public bool AccessDenied { get; internal set; }

  /// <summary>Gets a collection of <see cref="IAuthorizationFilter">authorization filters</see> associated with the URI.</summary>
  public ReadOnlyListWrapper<IAuthorizationFilter> AuthorizationFilters
  {
    get
    {
      if(_filterWrapper == null)
      {
        _filterWrapper = filterArray == null ? NoFilters : new ReadOnlyListWrapper<IAuthorizationFilter>(filterArray);
      }
      return _filterWrapper;
    }
  }

  /// <summary>Gets the <see cref="ILockManager"/> responsible for the URI, if any.</summary>
  public ILockManager LockManager { get; internal set; }

  /// <summary>Gets the <see cref="IPropertyStore"/> responsible for the URI, if any.</summary>
  public IPropertyStore PropertyStore { get; internal set; }

  /// <summary>Gets the path to the resource named by the URI, relative to the <see cref="ServiceRoot"/>. This variable will be valid
  /// whenever <see cref="Service"/> is not null.
  /// </summary>
  public string RelativePath { get; internal set; }

  /// <summary>Gets the <see cref="IWebDAVResource"/> corresponding to the URI, or null if the URI could not be resolved to a resource.</summary>
  public IWebDAVResource Resource { get; internal set; }

  /// <summary>Gets the <see cref="IWebDAVService"/> corresponding to the URI, or null if the URI did not correspond to any defined service
  /// location. This variable may be set to a valid service even if the URI could not be resolved to a resource.
  /// </summary>
  public IWebDAVService Service { get; internal set; }

  /// <summary>Gets the absolute path to the <see cref="Service"/> root. If the <see cref="Uri.Scheme"/> or <see cref="Uri.Authority"/> of
  /// the URI was different from the request URI, this will be an absolute URI (e.g. <c>http://othersite/otherRoot/</c>) rather than an
  /// absolute path (e.g. <c>/otherRoot/</c>). This variable will be valid whenever <see cref="Service"/> is not null.
  /// </summary>
  public string ServiceRoot { get; internal set; }

  internal IAuthorizationFilter[] filterArray;
  ReadOnlyListWrapper<IAuthorizationFilter> _filterWrapper;

  static readonly ReadOnlyListWrapper<IAuthorizationFilter> NoFilters =
    new ReadOnlyListWrapper<IAuthorizationFilter>(new IAuthorizationFilter[0]);
}
#endregion

#region WebDAVModule
/// <summary>Implements an <see cref="IHttpModule"/> that provides WebDAV services.</summary>
/// <remarks>You can derive from this class to customize the integration with ASP.NET by overriding the <see cref="Initialize"/> method
/// and potentially the <see cref="Dispose"/> method. If you derive from this class, you may want to override the following virtual
/// members.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="Dispose"/></term>
///   <description>You need to clean up data when the HTTP module is disposed.</description>
/// </item>
/// <item>
///   <term><see cref="Initialize"/></term>
///   <description>You need to perform additional tasks when the HTTP module is initialized.</description>
/// </item>
/// </list>
/// </remarks>
public class WebDAVModule : IHttpModule
{
  /// <summary>Returns the given string or null, depending on the value of <see cref="Configuration.ShowSensitiveErrors"/>.</summary>
  public static string FilterErrorMessage(string message)
  {
    return config.ShowSensitiveErrors ? message : null;
  }

  /// <summary>In the context of the given <paramref name="context"/>, attempts to resolve a URL into an <see cref="IWebDAVService"/> and
  /// <see cref="IWebDAVResource"/>. Returns a <see cref="UriResolution"/> object that describes the results of the resolution attempt.
  /// </summary>
  /// <param name="context">The <see cref="WebDAVContext"/> in which the request is being executed.</param>
  /// <param name="uri">
  ///   A <see cref="Uri"/> to resolve. This can either be an absolute URI (i.e. a URI with a scheme and authority) or a
  ///   relative URI with an absolute path (i.e. a URI constructed from a path beginning with a slash). If the URI is relative, the
  ///   authority of the request URI will be used.
  /// </param>
  /// <param name="performAccessChecks">If true, authorization checks will be performed against the resource using the null (read)
  /// permission. If access is denied, the resource may not be resolved. The result of the check will be placed in the
  /// <see cref="UriResolution.AccessDenied"/> property.Note that authorization checks may consider details of the request, such as the
  /// HTTP method, when deciding whether to grant or deny access, so if just validating an <c>If</c> header, for example, you should skip
  /// the access checks. If you need to check a different permission, you should skip access checks and call
  /// <see cref="ShouldDenyAccess(WebDAVContext,Uri,XmlQualifiedName)"/> if the resource resolves.
  /// </param>
  public static UriResolution ResolveUri(WebDAVContext context, Uri uri, bool performAccessChecks)
  {
    if(context == null || uri == null) throw new ArgumentNullException();

    LocationConfig location = ResolveLocation(uri, context);
    UriResolution info = new UriResolution();
    if(location != null)
    {
      info.ServiceRoot   = location.RootPath;
      info.RelativePath  = location.GetRelativeUrl(uri, context);
      info.LockManager   = location.LockManager;
      info.PropertyStore = location.PropertyStore;
      info.Service       = location.GetService();
      info.Resource      = info.Service.ResolveResource(context, info.RelativePath);
      info.filterArray   = location.AuthFilters;

      // convert the service root to an absolute URI if its scheme and authority aren't the same as those of the request URI
      if(uri.IsAbsoluteUri &&
         (!uri.Authority.OrdinalEquals(context.Request.Url.Authority) || !uri.Scheme.OrdinalEquals(context.Request.Url.Scheme)))
      {
        info.ServiceRoot = DAVUtility.RemoveTrailingSlash(uri.GetLeftPart(UriPartial.Authority)) + info.ServiceRoot;
      }

      ConditionCode response;
      if(performAccessChecks && info.Resource != null &&
         info.Service.ShouldDenyAccess(context, info.Resource, location.AuthFilters, null, out response))
      {
        info.AccessDenied = true;
        if(response != null && response.StatusCode == (int)HttpStatusCode.NotFound) info.Resource = null;
      }
    }

    return info;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVModule/ShouldDenyAccess/node()" />
  public static bool ShouldDenyAccess(WebDAVContext context, Uri uri, XmlQualifiedName access)
  {
    ConditionCode response;
    return ShouldDenyAccess(context, uri, access, out response);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVModule/ShouldDenyAccess/node()" />
  /// <param name="response">A variable that will receive a <see cref="ConditionCode"/> indicating the type of response to send to the
  /// client, or null if the default response (typically <see cref="ConditionCodes.Forbidden"/>) should be used.
  /// </param>
  public static bool ShouldDenyAccess(WebDAVContext context, Uri uri, XmlQualifiedName access, out ConditionCode response)
  {
    response = null;
    LocationConfig location = ResolveLocation(uri, context);
    bool denyAccess = false;
    if(location != null)
    {
      IWebDAVService service = location.GetService();
      IWebDAVResource resource = service.ResolveResource(context, location.GetRelativeUrl(uri, context));
      service.ShouldDenyAccess(context, resource, location.AuthFilters, access, out response);
    }
    return denyAccess;
  }

  /// <summary>Disposes resources related to the <see cref="IHttpModule"/>. Note that this method is distinct from
  /// <see cref="IDisposable.Dispose"/>.
  /// </summary>
  /// <remarks><note type="inherit">Derived classes that override this method must call the base class implementation.</note></remarks>
  protected virtual void Dispose()
  {
    // we have nothing to dispose (and we don't need to remove the event handler delegate because we hold no reference to HttpApplication)
  }

  /// <summary>Initializes the WebDAV module, hooking into the ASP.NET pipeline.</summary>
  /// <remarks><note type="inherit">Derived classes that override this method must call the base implementation, and if you store a
  /// reference to <paramref name="context"/>, you must release the reference in <see cref="Dispose"/>.
  /// </note></remarks>
  protected virtual void Initialize(HttpApplication context)
  {
    if(context == null) throw new ArgumentNullException();     // validate the argument
    context.PostAuthenticateRequest += OnRequestAuthenticated; // and attach to the ASP.NET pipeline after authentication is complete
  }

  #region Configuration
  /// <summary>Represents the configuration of the WebDAV module.</summary>
  sealed class Configuration
  {
    public Configuration(WebDAVServerSection config)
    {
      if(config != null)
      {
        Enabled             = config.Enabled;
        ShowSensitiveErrors = config.ShowSensitiveErrors;
        if(Enabled)
        {
          HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          Locations = new LocationConfig[config.Locations.Count];
          for(int i=0; i<Locations.Length; i++)
          {
            Locations[i] = new LocationConfig(config.Locations[i], config.LockManager, config.PropertyStore);
            if(!ids.Add(Locations[i].ID))
            {
              throw new ConfigurationErrorsException("Duplicate ID " + Locations[i].ID + " found in location " +
                                                     config.Locations[i].Match);
            }
          }
        }
      }

      ContextSettings = new WebDAVContext.Configuration(ShowSensitiveErrors);

      AppDomain.CurrentDomain.DomainUnload += OnDomainUnloading;
    }

    /// <summary>Gets the <see cref="WebDAVContext.Configuration"/> object that should be used when servicing requests.</summary>
    public readonly WebDAVContext.Configuration ContextSettings;
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
    }
  }
  #endregion

  #region LocationConfig
  sealed class LocationConfig
  {
    public LocationConfig(LocationElement config, LockManagerElement lockManager, PropertyStoreElement propertyStore)
    {
      Enabled       = config.Enabled;
      caseSensitive = config.CaseSensitive;
      ParseMatch(config.Match);

      if(Enabled)
      {
        ID = config.ID;
        if(string.IsNullOrEmpty(ID)) // if there's no ID, generate one in the form scheme_hostname_port_path/to/dav
        {
          ID = scheme + "_" + hostname + "_" + (port != 0 ? port.ToStringInvariant() : null) + "_" + RootPath.Trim('/');
        }
        ID = ID.ToLowerInvariant();

        if(config.LockManager != null && config.LockManager.InnerType != null) lockManager = config.LockManager;
        if(config.PropertyStore != null && config.PropertyStore.InnerType != null) propertyStore = config.PropertyStore;

        serviceCreator   = GetCreationDelegate<IWebDAVService>(config.Type, config.Parameters);
        ResetOnError     = config.ResetOnError;
        ServeRootOptions = config.ServeRootOptions;
        LockManager      = Construct(lockManager, typeof(DisableLockManager), ID);
        PropertyStore    = Construct(propertyStore, typeof(DisablePropertyStore), ID);

        if(config.AuthorizationFilters.Count != 0)
        {
          AuthFilters = new IAuthorizationFilter[config.AuthorizationFilters.Count];
          for(int i=0; i<AuthFilters.Length; i++) AuthFilters[i] = Construct(config.AuthorizationFilters[i], null, null);
        }
      }
    }

    public string ID { get; private set; }
    public ILockManager LockManager { get; private set; }
    public IPropertyStore PropertyStore { get; private set; }
    public bool ResetOnError { get; private set; }
    public string RootPath { get; private set; }
    public bool ServeRootOptions { get; private set; }

    public void ClearSharedService()
    {
      service = null;
    }

    public void Dispose()
    {
      Utility.Dispose(service);
      service = null;
      if(AuthFilters != null)
      {
        foreach(IAuthorizationFilter authFilter in AuthFilters) Utility.Dispose(authFilter);
      }
      Utility.Dispose(LockManager);
      Utility.Dispose(PropertyStore);
    }

    /// <summary>Given a request <see cref="Uri"/>, returns the path to the requested resource, relative to the root of the WebDAV
    /// service. The request <see cref="Uri"/> must either be absolute or have an absolute path (e.g. /path/to/resource).
    /// </summary>
    /// <param name="requestUri">A request <see cref="Uri"/>.</param>
    /// <param name="context">The context in which the URI should be resolved. If null, <paramref name="requestUri"/> must be an absolute
    /// <see cref="Uri"/>.
    /// </param>
    public string GetRelativeUrl(Uri requestUri, WebDAVContext context)
    {
      string requestPath = DAVUtility.UriPathPartialDecode(GetAbsoluteUri(requestUri, context).AbsolutePath);
      return RootPath.Length >= requestPath.Length ? "" : requestPath.Substring(RootPath.Length);
    }

    public IWebDAVService GetService()
    {
      IWebDAVService service = this.service; // grab a local copy to prevent it from being cleared on another thread via ClearSharedService
      if(service == null) service = this.service = serviceCreator();
      return service;
    }

    public bool MatchesRequest(HttpRequest request)
    {
      return MatchesRequest(request.Url);
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
    public readonly IAuthorizationFilter[] AuthFilters;

    void ParseMatch(string matchString)
    {
      Match m = new Regex(LocationElement.MatchPattern).Match(matchString);
      if(!m.Success) throw new ConfigurationErrorsException(matchString + " is not a valid AdamMil.WebDAV.Server location match string.");
      if(m.Groups["scheme"].Success) scheme = m.Groups["scheme"].Value.ToLowerInvariant();
      if(m.Groups["hostname"].Success) hostname = m.Groups["hostname"].Value.ToLowerInvariant();
      if(m.Groups["port"].Success) port = int.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture);
      if(m.Groups["path"].Success) path = "/" + DAVUtility.WithTrailingSlash(DAVUtility.UriPathNormalize(m.Groups["path"].Value));
      RootPath = path ?? "/";
    }

    bool PathMatches(Uri requestUri)
    {
      StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      string requestPath = DAVUtility.UriPathNormalize(requestUri.AbsolutePath);
      // the match path includes the trailing slash, so if the request path is greater or equal in length, then the match path must be a
      // prefix of it. otherwise, if the request path is shorter, they can only match if the request path is equal to the match path when
      // the trailing slash of the match path is ignored (e.g. the match path is /dav/ but the user types /dav -- we'll accept this).
      return requestPath.Length >= path.Length ? requestPath.StartsWith(path, comparison)
                                               : requestPath.Length+1 == path.Length && path.StartsWith(requestPath, comparison);
    }

    readonly Func<IWebDAVService> serviceCreator;
    string scheme, hostname, path;
    IWebDAVService service;
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
          catch(HttpException ex) // HttpExceptions might be expected (this includes WebDAVException)
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
            if(location.ResetOnError) location.ClearSharedService();
            throw;
          }

          if(endRequest) application.CompleteRequest();
        }

        break; // in any case, once we've found the right location, we're done
      }
      // if the service is configured to serve OPTIONS requests to the root, and this is such a request, then we'll handle it also
      else if(location.Enabled && location.ServeRootOptions && application.Request.HttpMethod.OrdinalEquals(DAVMethods.Options) &&
              (string.Equals(application.Request.Url.AbsolutePath, "/", StringComparison.Ordinal) ||
               string.Equals(application.Request.RawUrl, "*", StringComparison.Ordinal)))
      {
        WebDAVContext context = CreateContext(location, "", application, config);
        OptionsRequest request = context.Service.CreateOptions(context);
        request.SetOutOfScope(); // enable special processing within OptionsRequest for out-of-scope requests
        ProcessMethod(context, request, context.Service.Options);
        break; // don't complete the request because we only want to add our custom headers to ASP.NET's usual response
      }
    }
  }

  void IHttpModule.Dispose()
  {
    Dispose();
  }

  void IHttpModule.Init(HttpApplication context)
  {
    if(config.Enabled) Initialize(context);
  }

  /// <summary>Constructs an object specified in the server configuration.</summary>
  static T Construct<T>(TypeElementBase<T> element, Type disableType, string locationId) where T : class
  {
    T obj = null;
    Type type = element != null ? element.InnerType : null;
    if(type != null && type != disableType)
    {
      if(!typeof(T).IsAssignableFrom(type))
      {
        throw new ConfigurationErrorsException(type.FullName + " does not derive from " + typeof(T).FullName + ".");
      }
      Type[] paramTypes =
        locationId == null ? new Type[] { typeof(ParameterCollection) } : new Type[] { typeof(string), typeof(ParameterCollection) };
      ConstructorInfo cons = type.GetConstructor(paramTypes);
      if(cons != null)
      {
        obj = (T)cons.Invoke(locationId == null ? new object[] { element.Parameters } : new object[] { locationId, element.Parameters });
      }
      else
      {
        cons = type.GetConstructor(locationId == null ? Type.EmptyTypes : new Type[] { typeof(string) });
        if(cons != null)
        {
          obj = (T)cons.Invoke(locationId == null ? null : new object[] { locationId });
        }
        else
        {
          throw new ConfigurationErrorsException(type.FullName + " does not have a public constructor that accepts a string or a public " +
                                                 "constructor that accepts a string and an " + typeof(ParameterCollection).FullName +
                                                 " object.");
        }
      }
    }
    return obj;
  }

  static WebDAVContext CreateContext(LocationConfig location, string requestPath, HttpApplication application, Configuration config)
  {
    return new WebDAVContext(location.GetService(), location.AuthFilters, location.RootPath, requestPath, application,
                             location.LockManager, location.PropertyStore, config.ContextSettings);
  }

  /// <summary>Attempts to authorize the user in the context of the current request. If true is returned, the user has already been denied
  /// access. If false is returned, the user will be granted access only if all other authorization methods allow it.
  /// </summary>
  static bool DeniedAccess(WebDAVContext context, LocationConfig location)
  {
    ConditionCode response;
    if(context.Service.ShouldDenyAccess(context, location.AuthFilters, out response))
    {
      // issuing a 404 Not Found response when the request would normally create a new resource actually reveals its existence rather than
      // hiding it because a 404 response is never normally issued for those requests. so for requests that can create new resources,
      // use the default response
      if(response != null && response.StatusCode == (int)HttpStatusCode.NotFound &&
         (context.Request.HttpMethod.OrdinalEquals(DAVMethods.Put) || context.Request.HttpMethod.OrdinalEquals(DAVMethods.Lock) ||
          context.Request.HttpMethod.OrdinalEquals(DAVMethods.MkCol)))
      {
        response = null;
      }

      if(response == null) WriteErrorResponse(context.Application, (int)HttpStatusCode.Forbidden, "Access denied.");
      else if(response.StatusCode == (int)HttpStatusCode.NotFound) WriteNotFoundResponse(context.Application);
      else WriteErrorResponse(context.Application, response.StatusCode, response.Message);
      return true;
    }

    return false;
  }

  /// <summary>Converts a <see cref="Uri"/> into an absolute <see cref="Uri"/>, for which <see cref="Uri.IsAbsoluteUri"/> is true.</summary>
  static Uri GetAbsoluteUri(Uri uri, WebDAVContext context)
  {
    if(uri == null) throw new ArgumentNullException();

    // if the absolute URI doesn't have an authority section, use the authority from the request
    if(!uri.IsAbsoluteUri)
    {
      string path = uri.ToString();
      if(path.Length == 0 || path[0] != '/')
      {
        throw new ArgumentException("The uri must be either an absolute URI or a URI constructed from an absolute path.");
      }
      if(context == null) throw new ArgumentNullException("A non-absolute URI can only be resolved in the context of a request.");
      uri = new Uri(context.Request.Url.GetLeftPart(UriPartial.Authority) + path);
    }

    return uri;
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
                                           "that accepts an " + typeof(ParameterCollection).FullName + " object.");
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
    if(DAVUtility.IsSuccess(request.Status)) process(request);
    request.WriteResponse();
    Utility.Dispose(request);
  }

  /// <summary>Processes a WebDAV request using the service represented by the given <see cref="LocationConfig"/>. Returns whether the
  /// request was handled and should be completed.
  /// </summary>
  static bool ProcessRequest(HttpApplication app, LocationConfig config)
  {
    // create the request context and service instance
    WebDAVContext context = CreateContext(config, config.GetRelativeUrl(app.Request.Url, null), app, WebDAVModule.config);
    // resolve the request Uri into a WebDAV resource
    context.ResolveResource();

    // perform authorization
    if(DeniedAccess(context, config)) return true;

    string method = app.Request.HttpMethod;
    if(context.RequestResource == null) // if the resource was not found...
    {
      // some verbs (like MKCOL and PUT) can be executed on unmapped URLs. if it's not a verb we care about and the service also declines
      // to handle it, issue a 404 Not Found response
      if(method.OrdinalEquals(DAVMethods.Put))
      {
        ProcessMethod(context, context.Service.CreatePut(context), context.Service.Put);
      }
      else if(method.OrdinalEquals(DAVMethods.Lock))
      {
        ProcessMethod(context, context.Service.CreateLock(context), context.Service.CreateAndLock);
      }
      else if(method.OrdinalEquals(DAVMethods.MkCol))
      {
        ProcessMethod(context, context.Service.CreateMkCol(context), context.Service.MakeCollection);
      }
      else if(method.OrdinalEquals(DAVMethods.Unlock))
      {
        ProcessMethod(context, context.Service.CreateUnlock(context), context.Service.Unlock);
      }
      else if(method.OrdinalEquals(DAVMethods.Options))
      {
        ProcessMethod(context, context.Service.CreateOptions(context), context.Service.Options);
      }
      else if(method.OrdinalEquals(DAVMethods.Post))
      {
        ProcessMethod(context, context.Service.CreatePost(context), context.Service.Post);
      }
      else if(!context.Service.HandleGenericRequest(context))
      {
        if(method.OrdinalEquals(DAVMethods.Trace)) return false; // let ASP.NET handle TRACE requests if the WebDAV service didn't
        WriteNotFoundResponse(app);
      }
    }
    else // otherwise, the URL was mapped to a resource
    {
      // now process the WebDAV operation against the resource based on the HTTP method
      if(method.OrdinalEquals(DAVMethods.PropFind))
      {
        ProcessMethod(context, context.Service.CreatePropFind(context), context.RequestResource.PropFind);
      }
      else if(method.OrdinalEquals(DAVMethods.Get) || method.OrdinalEquals(DAVMethods.Head))
      {
        ProcessMethod(context, context.Service.CreateGetOrHead(context), context.RequestResource.GetOrHead);
      }
      else if(method.OrdinalEquals(DAVMethods.Put))
      {
        ProcessMethod(context, context.Service.CreatePut(context), context.RequestResource.Put);
      }
      else if(method.OrdinalEquals(DAVMethods.Lock))
      {
        ProcessMethod(context, context.Service.CreateLock(context), context.RequestResource.Lock);
      }
      else if(method.OrdinalEquals(DAVMethods.Unlock))
      {
        ProcessMethod(context, context.Service.CreateUnlock(context), context.RequestResource.Unlock);
      }
      else if(method.OrdinalEquals(DAVMethods.PropPatch))
      {
        ProcessMethod(context, context.Service.CreatePropPatch(context), context.RequestResource.PropPatch);
      }
      else if(method.OrdinalEquals(DAVMethods.Options))
      {
        OptionsRequest request = context.Service.CreateOptions(context);
        ProcessMethod(context, request, // route all server-wide queries to IWebDAVService.Options
                      request.IsServerQuery ? (Action<OptionsRequest>)context.Service.Options : context.RequestResource.Options);
      }
      else if(method.OrdinalEquals(DAVMethods.Delete))
      {
        ProcessMethod(context, context.Service.CreateDelete(context), context.RequestResource.Delete);
      }
      else if(method.OrdinalEquals(DAVMethods.Copy) || method.OrdinalEquals(DAVMethods.Move))
      {
        ProcessMethod(context, context.Service.CreateCopyOrMove(context), context.RequestResource.CopyOrMove);
      }
      else if(method.OrdinalEquals(DAVMethods.Post))
      {
        ProcessMethod(context, context.Service.CreatePost(context), context.RequestResource.Post);
      }
      else if(method.OrdinalEquals(DAVMethods.MkCol))
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
        if(method.OrdinalEquals(DAVMethods.Trace)) return false; // let ASP.NET handle TRACE requests if the WebDAV service didn't
        WriteErrorResponse(app, (int)HttpStatusCode.Forbidden, "The WebDAV service declined to respond to this request.");
      }
    }

    return true;
  }

  static LocationConfig ResolveLocation(Uri uri, WebDAVContext context)
  {
    uri = GetAbsoluteUri(uri, context);
    // find the service that matches the URL, if any
    foreach(LocationConfig location in config.Locations)
    {
      if(location.MatchesRequest(uri))
      {
        if(location.Enabled) return location;
        break;
      }
    }
    return null;
  }

  /// <summary>Sets a 404 Not Found status response for the request URI.</summary>
  static void WriteNotFoundResponse(HttpApplication app)
  {
    WriteErrorResponse(app, (int)HttpStatusCode.NotFound, "The requested resource could not be found.");
  }

  /// <summary>Sets the response status code to the given status code and writes an error message to the page.</summary>
  static void WriteErrorResponse(HttpApplication app, int httpStatusCode, string errorText)
  {
    // clear out any content that's already been written
    if(app.Response.BufferOutput)
    {
      app.Response.ClearHeaders();
      app.Response.Clear();
    }
    DAVUtility.WriteStatusResponse(app.Request, app.Response, httpStatusCode, errorText);
  }

  /// <summary>Sets the response status code to the given status code and writes an error response based on the given
  /// <see cref="ConditionCode"/>.
  /// </summary>
  static void WriteErrorResponse(HttpApplication app, ConditionCode code)
  {
    // clear out any content that's already been written
    if(app.Response.BufferOutput)
    {
      app.Response.ClearHeaders();
      app.Response.Clear();
    }
    DAVUtility.WriteStatusResponse(app.Request, app.Response, code);
  }

  /// <summary>The configuration of the WebDAV module.</summary>
  static readonly Configuration config = new Configuration(WebDAVServerSection.Get());
}
#endregion

} // namespace AdamMil.WebDAV.Server
