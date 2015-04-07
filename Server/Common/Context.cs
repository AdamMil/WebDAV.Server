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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.IO;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

/// <summary>Contains information about the current WebDAV request.</summary>
public sealed class WebDAVContext
{
  internal WebDAVContext(IWebDAVService service, IAuthorizationFilter[] authFilters, string serviceRootPath, string requestPath,
                         HttpApplication app, ILockManager lockManager, IPropertyStore propertyStore, Configuration config)
  {
    Service          = service;
    this.authFilters = authFilters;
    ServiceRoot      = serviceRootPath;
    RequestPath      = requestPath;
    Application      = app;
    Request          = app.Request;
    Response         = app.Response;
    User             = app.User;
    LockManager      = lockManager;
    PropertyStore    = propertyStore;
    Settings         = config;
  }

  #region Configuration
  /// <summary>Contains configuration settings that a WebDAV service should use in when processing a request.</summary>
  public sealed class Configuration
  {
    internal Configuration(bool showSensitiveErrors)
    {
      ShowSensitiveErrors = showSensitiveErrors;
    }

    /// <summary>Gets whether error messages containing potentially sensitive information should be written to the client. If false</summary>
    public bool ShowSensitiveErrors { get; private set; }
  }
  #endregion

  /// <summary>Get the ID of the user making the current request, or null if the user is unknown or anonymous.</summary>
  /// <remarks><para><include file="documentation.xml" path="/DAV/IWebDAVService/GetCurrentUserId/remarks/node()" /></para>
  /// <para>This property will return an ID determined by interrogating the <see cref="IAuthorizationFilter">authorization filters</see> as
  /// well as the <see cref="Service"/>. The value will be cached for the duration of the request.
  /// </para>
  /// </remarks>
  public string CurrentUserId
  {
    get
    {
      if(_currentUserId == null)
      {
        string userId = null;
        if(authFilters != null)
        {
          foreach(IAuthorizationFilter filter in authFilters)
          {
            if(filter.GetCurrentUserId(this, out userId))
            {
              userId = userId ?? "";
              break;
            }
          }
        }
        if(userId == null) userId = Service.GetCurrentUserId(this);
        _currentUserId = userId ?? "";
      }

      return StringUtility.MakeNullIfEmpty(_currentUserId);
    }
  }

  /// <summary>Gets the default <see cref="ILockManager"/> to be used with the request, or null if no lock manager is configured.</summary>
  public ILockManager LockManager { get; private set; }

  /// <summary>Gets the default <see cref="IPropertyStore"/> to be used with the request, or null if no property store is configured.</summary>
  public IPropertyStore PropertyStore { get; private set; }

  /// <summary>Gets the <see cref="HttpRequest"/> associated with the WebDAV request.</summary>
  public HttpRequest Request { get; private set; }

  /// <summary>Gets the path of the request URL, relative to <see cref="ServiceRoot"/>. As a relative path, it will not contain a leading
  /// slash. This might not be the canonical path to the resource.
  /// </summary>
  public string RequestPath { get; private set; }

  /// <summary>Gets the <see cref="IWebDAVResource"/> mapped to the WebDAV request URI, or null if the request was made to a URL that was
  /// not mapped to any resource in the <see cref="Service"/>.
  /// </summary>
  public IWebDAVResource RequestResource { get; private set; }

  /// <summary>Gets the <see cref="HttpResponse"/> associated with the WebDAV request. In general, you should not write directly to the
  /// response, but instead use the helper methods provided by the WebDAV framework, such as <see cref="OpenXmlResponse"/> and
  /// <see cref="OpenMultiStatusResponse"/>.
  /// </summary>
  public HttpResponse Response { get; private set; }

  /// <summary>Gets the <see cref="IWebDAVService"/> responsible for serving this request.</summary>
  public IWebDAVService Service { get; private set; }

  /// <summary>Gets the absolute path to the root of the <see cref="Service"/>, including the trailing slash. Unlike the
  /// <see cref="RequestPath"/> the service root will be encoded in a form that is legal to insert directly into a URI path.
  /// </summary>
  public string ServiceRoot { get; private set; }

  /// <summary>Gets additional configuration settings for the WebDAV service in the current context.</summary>
  public Configuration Settings { get; private set; }

  /// <summary>Gets the <see cref="IPrincipal"/> reported by ASP.NET as the user making the current request.</summary>
  public IPrincipal User { get; private set; }

  /// <summary>Determines whether the user represented by the <see cref="CurrentUserId"/> is allowed to delete the given lock.</summary>
  /// <remarks>This property will interrogate the <see cref="IAuthorizationFilter">authorization filters</see> as well as the
  /// <see cref="Service"/>.
  /// </remarks>
  public bool CanDeleteLock(ActiveLock lockObject)
  {
    if(lockObject == null) throw new ArgumentNullException();
    if(CurrentUserId.OrdinalEquals(lockObject.OwnerId)) return true;

    if(authFilters != null)
    {
      foreach(IAuthorizationFilter filter in authFilters)
      {
        bool? canUnlock = filter.CanDeleteLock(this, lockObject);
        if(canUnlock.HasValue) return canUnlock.Value;
      }
    }

    return Service.CanDeleteLock(this, lockObject);
  }

  /// <summary>Chooses a <see cref="ContentEncoding"/> for the response based on the types supported by the client.</summary>
  /// <param name="enableCompression">Determines whether compression is enabled. If true, a compressed content encoding will be chosen if
  /// it's preferred by the client. Otherwise, only the uncompressed <see cref="ContentEncoding.Identity"/> encoding can be chosen.
  /// </param>
  public ContentEncoding ChooseResponseEncoding(bool enableCompression)
  {
    // the identity encoding is assumed to always be available unless it's explicitly disallowed
    ContentEncoding encoding = ContentEncoding.Identity;
    string encodingName = "identity";

    // parse the Accept-Encoding header so that we can encode the content appropriately (see RFC 7231 section 5.3.4)
    string[] encodings = DAVUtility.ParseHttpTokenList(Request.Headers[DAVHeaders.AcceptEncoding]);
    if(encodings != null)
    {
      List<Accept> acceptedEncodings = new List<Accept>();
      int starIndex = -1; // the index of the * pseudo-encoding, or -1 if it was not submitted
      for(int i=0; i<encodings.Length; i++)
      {
        Accept acceptable = new Accept(encodings[i].ToLowerInvariant());
        if(acceptable.Name.OrdinalEquals("*")) starIndex = i;
        acceptedEncodings.Add(acceptable);
      }

      // if the * encoding was given, expand it
      if(starIndex != -1)
      {
        bool hasGzip = false, hasDeflate = false, hasIdentity = false;
        foreach(Accept acceptable in acceptedEncodings)
        {
          if(!hasGzip && acceptable.Name.OrdinalEquals("gzip")) hasGzip = true;
          else if(!hasDeflate && acceptable.Name.OrdinalEquals("deflate")) hasDeflate = true;
          else if(!hasIdentity && acceptable.Name.OrdinalEquals("identity")) hasIdentity = true;
        }

        float preference = acceptedEncodings[starIndex].Preference;
        if(enableCompression)
        {
          if(!hasGzip) acceptedEncodings.Add(new Accept("gzip", preference));
          if(!hasDeflate) acceptedEncodings.Add(new Accept("deflate", preference));
        }
        if(!hasIdentity) acceptedEncodings.Add(new Accept("identity", preference));
      }

      // sort the encodings by preference, putting the most-preferred ones first
      acceptedEncodings.Sort((a, b) =>
      {
        int cmp = b.Preference.CompareTo(a.Preference);
        if(cmp == 0) cmp = GetDefaultPreference(b.Name).CompareTo(GetDefaultPreference(a.Name));
        return cmp;
      });

      foreach(Accept acceptable in acceptedEncodings)
      {
        string name = acceptable.Name;
        if(acceptable.Preference != 0) // if the encoding is allowed...
        {
          if(enableCompression && (name.OrdinalEquals("gzip") || name.OrdinalEquals("deflate")) || name.OrdinalEquals("identity"))
          {
            encodingName = name; // use it
            break;
          }
        }
        else if(name.OrdinalEquals("identity")) // otherwise, if identity is disallowed...
        {
          encodingName = null; // we can't use it
          break;
        }
      }

      if(encodingName == null)
      {
        throw new WebDAVException((int)HttpStatusCode.NotAcceptable,
                                  "No content encoding supported by the server was acceptable to the client.");
      }

      if(encodingName.OrdinalEquals("gzip")) encoding = ContentEncoding.GZip;
      else if(encodingName.OrdinalEquals("deflate")) encoding = ContentEncoding.Deflate;
    }

    return encoding;
  }

  /// <summary>Returns <see cref="IWebDAVResource.CanonicalPath"/> if <see cref="RequestResource"/> is not null, or the result of calling
  /// <see cref="IWebDAVService.GetCanonicalUnmappedPath"/> on the <see cref="RequestPath"/> otherwise.
  /// </summary>
  public string GetCanonicalPath()
  {
    return RequestResource != null ? RequestResource.CanonicalPath : Service.GetCanonicalUnmappedPath(this, RequestPath);
  }

  /// <summary>Returns an <see cref="XmlDocument"/> containing the request body loaded as XML, or null if the body is empty.</summary>
  public XmlDocument LoadRequestXml()
  {
    XmlDocument xml = null;
    using(XmlReader reader = OpenRequestXml())
    {
      if(reader != null)
      {
        xml = new XmlDocument();
        xml.Load(reader);
      }
    }
    return xml;
  }

  /// <summary>Returns an <see cref="XmlReader"/> that will read the request body as XML, or null if the body is empty.</summary>
  public XmlReader OpenRequestXml()
  {
    string bodyText = ReadRequestText();
    if(StringUtility.IsNullOrSpace(bodyText))
    {
      return null;
    }
    else
    {
      XmlReaderSettings settings = new XmlReaderSettings();
      settings.IgnoreComments = true; // comments are irrelevant
      settings.IgnoreProcessingInstructions = true; // processing instructions are irrelevant
      settings.IgnoreWhitespace = false; // RFC4918 section 4.3 requires WebDAV servers to treat some whitespace as significant
      settings.ProhibitDtd = false; // allow DTDs within the body, since they are valid
      settings.MaxCharactersFromEntities = 100; // but prohibit entities longer than 100 charaters
      settings.XmlResolver = RestrictiveResolver.Instance; // disallow external entities
      return XmlReader.Create(new StringReader(bodyText), settings);
    }
  }

  /// <summary>Returns a <see cref="MultiStatusResponse"/> object that writes a 207 Multi-Status response to the client.</summary>
  /// <param name="namespaces">A set of XML namespaces used within the response. These namespaces will be given prefixes defined on the
  /// root element of the response, making the namespaces prefixes available throughout the response. The DAV: namespace is always defined
  /// as the default namespace in the response, and need not be provided explicitly. If null, no additional namespaces will be defined. The
  /// prefixes allocated during this method are "xs" and "xsi" for XML Schema and XML Schema Instance respectively, the single letters 'a'
  /// through 'z' for the first 26 namespaces, and prefixes of the form "nsX" where "X" can be replaced by any positive integer for the
  /// rest. If you define your own namespace prefixes within the response, be careful not to use prefixes that would clash in incompatible
  /// ways.
  /// </param>
  /// <remarks>The <see cref="MultiStatusResponse"/> object returned must be disposed to complete the response. The best practice is to use
  /// a <c>using</c> statement to ensure that the response is disposed. The disposal of the response does not terminate the web request.
  /// </remarks>
  public MultiStatusResponse OpenMultiStatusResponse(HashSet<string> namespaces)
  {
    // begin outputting a multistatus (HTTP 207) response as defined in RFC 4918
    Response.SetStatus(207);
    return new MultiStatusResponse(this, OpenXmlResponse(null), namespaces);
  }

  /// <summary>Returns the request stream after decoding it according to the <c>Content-Encoding</c> header. The returned should be closed
  /// when you are done reading from it.
  /// </summary>
  /// <remarks>This method supports the <c>gzip</c>, <c>deflate</c>, and <c>identity</c> content encodings. Any other content encoding will
  /// cause a <see cref="WebDAVException"/> to be thrown with a 415 Unsupported Media Type status. If you need to support additional
  /// content encodings, you must do so yourself, using the <see cref="HttpRequest.InputStream"/> directly.
  /// </remarks>
  public Stream OpenRequestBody()
  {
    Stream stream = Request.InputStream;
    bool wrappedStream = false;

    // process the Content-Encoding header so that we can decode the content appropriately
    string[] encodings = DAVUtility.ParseHttpTokenList(Request.Headers[DAVHeaders.ContentEncoding]);
    if(encodings != null)
    {
      bool hadEncoding = false;
      foreach(string encoding in encodings)
      {
        if(!hadEncoding && encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
        {
          stream = new GZipStream(stream, CompressionMode.Decompress, true);
          hadEncoding = wrappedStream = true;
        }
        else if(!hadEncoding && encoding.Equals("deflate", StringComparison.OrdinalIgnoreCase))
        {
          stream = new DeflateStream(stream, CompressionMode.Decompress, true);
          hadEncoding = wrappedStream = true;
        }
        else if(!hadEncoding && encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) // identity means no encoding
        {
          hadEncoding = true;
        }
        else
        {
          throw new WebDAVException((int)HttpStatusCode.UnsupportedMediaType, "Unsupported or multiple content encoding: " + encoding);
        }
      }
    }

    return wrappedStream ? stream : new DelegateStream(stream, false); // make sure the real output stream won't get closed
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVContext/OpenResponseBody/*[not(@name)]" />
  public Stream OpenResponseBody()
  {
    return OpenResponseBody(true, false);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVContext/OpenResponseBody/*[not(@name='encoding')]" />
  public Stream OpenResponseBody(bool enableCompression, bool disableBuffering)
  {
    return OpenResponseBody(ChooseResponseEncoding(enableCompression), disableBuffering);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVContext/OpenResponseBody/*[not(@name='enableCompression')]" />
  public Stream OpenResponseBody(ContentEncoding encoding, bool disableBuffering)
  {
    if(Response.BufferOutput) Response.SetContentEncodingHeader(encoding); // set the Content-Encoding header if we still can
    if(disableBuffering) Response.BufferOutput = false;
    return DAVUtility.EncodeOutputStream(Response.OutputStream, encoding, true);
  }

  /// <summary>Returns an <see cref="XmlWriter"/> object that writes an XML response body to the client.</summary>
  /// <param name="status">A <see cref="ConditionCode"/> used to set the HTTP status. If null, the status is assumed to have been set
  /// already.
  /// </param>
  /// <remarks>The <see cref="XmlWriter"/> object returned must be disposed to complete the response. The best practice is to use
  /// a <c>using</c> statement to ensure that the response is disposed. The disposal of the response does not terminate the web request.
  /// </remarks>
  public XmlWriter OpenXmlResponse(ConditionCode status)
  {
    if(status != null) Response.SetStatus(status);
    Response.ContentEncoding = System.Text.Encoding.UTF8;
    Response.SetContentType("application/xml"); // media type specified by RFC 4918 section 8.2
    return XmlWriter.Create(OpenResponseBody(), new XmlWriterSettings() { CloseOutput = true, IndentChars = "\t", NewLineChars = "\n" });
  }

  /// <summary>Determines whether access should be denied to the resource named by the given path in the context of the current request.</summary>
  /// <param name="relativePath">The path to a resource, relative to <see cref="ServiceRoot"/>, or null to reference the request resource.
  /// If this parameter is not equal to <see cref="RequestPath"/>, then the resource must exist.
  /// </param>
  /// <include file="documentation.xml" path="/DAV/Common/ShouldDenyAccess/*[not(@name='denyExistence')]" />
  public bool ShouldDenyAccess(string relativePath, XmlQualifiedName access)
  {
    bool denyAccess;
    return ShouldDenyAccess(relativePath, access, out denyAccess);
  }

  /// <summary>Determines whether access should be denied to the resource named by the given path in the context of the current request.</summary>
  /// <param name="relativePath">The path to a resource, relative to <see cref="ServiceRoot"/>, or null to reference the request resource.
  /// If this parameter is not equal to <see cref="RequestPath"/>, then the resource must exist.
  /// </param>
  /// <include file="documentation.xml" path="/DAV/Common/ShouldDenyAccess/node()" />
  public bool ShouldDenyAccess(string relativePath, XmlQualifiedName access, out bool denyExistence)
  {
    IWebDAVResource resource;
    if(relativePath == null || relativePath.OrdinalEquals(RequestPath))
    {
      resource = RequestResource;
    }
    else
    {
      resource = Service.ResolveResource(this, relativePath);
      if(resource == null) throw new ArgumentException("The resource \"" + relativePath + "\" could not be found.");
    }
    return Service.ShouldDenyAccess(this, resource, authFilters, access, out denyExistence);
  }

  /// <summary>Determines whether access should be denied to the given resource in the context of the current request.</summary>
  /// <param name="resource">The resource to check.</param>
  /// <include file="documentation.xml" path="/DAV/Common/ShouldDenyAccess/*[not(@name='denyExistence')]" />
  public bool ShouldDenyAccess(IWebDAVResource resource, XmlQualifiedName access)
  {
    bool denyAccess;
    return ShouldDenyAccess(resource, access, out denyAccess);
  }

  /// <summary>Determines whether access should be denied to the given resource in the context of the current request.</summary>
  /// <param name="resource">The resource to check.</param>
  /// <include file="documentation.xml" path="/DAV/Common/ShouldDenyAccess/node()" />
  public bool ShouldDenyAccess(IWebDAVResource resource, XmlQualifiedName access, out bool denyExistence)
  {
    if(resource == null) throw new ArgumentNullException();
    return Service.ShouldDenyAccess(this, resource, authFilters, access, out denyExistence);
  }

  /// <summary>Writes a response to the client based on the given <see cref="ConditionCode"/>.</summary>
  /// <remarks>This method does not terminate the response.</remarks>
  public void WriteStatusResponse(ConditionCode status)
  {
    if(status == null) throw new ArgumentNullException();
    DAVUtility.WriteStatusResponse(Request, Response, status);
  }

  /// <summary>Returns the request body as text.</summary>
  public string ReadRequestText()
  {
    // use a DelegateStream to prevent the StreamReader from closing the HTTP request stream
    using(StreamReader reader = new StreamReader(OpenRequestBody(), Request.ContentEncoding)) return reader.ReadToEnd();
  }

  internal HttpApplication Application { get; private set; }

  /// <summary>Resolves <see cref="RequestPath"/> and sets <see cref="RequestResource"/>.</summary>
  internal void ResolveResource()
  {
    RequestResource = Service.ResolveResource(this, RequestPath);
  }

  /// <summary>Determines whether the client is the WebDAV mini-redirector client used by Windows Explorer, which has numerous bugs and
  /// limitations that we need to work around.
  /// </summary>
  internal bool UseExplorerHacks()
  {
    // at the very least we need to 1) specify a prefix for the DAV: namespace (e.g. <D:prop>). Explorer can't understand responses that
    // use the default namespace. 2) truncate timestamp precision to no more than 3 decimal places. Explorer can't parse timestamps with
    // greater than millisecond precision
    return Request.UserAgent != null && Request.UserAgent.StartsWith("Microsoft-WebDAV-MiniRedir/", StringComparison.Ordinal);
  }

  /// <summary>Writes a 207 Multi-Status response describing the members that failed the operation. The failed members collection must not
  /// be empty.
  /// </summary>
  internal void WriteFailedMembers(FailedResourceCollection failedMembers)
  {
    if(failedMembers == null) throw new ArgumentNullException();
    if(failedMembers.Count == 0) throw new ArgumentException("There were no failed members.");

    using(MultiStatusResponse response = OpenMultiStatusResponse(null))
    {
      foreach(ResourceStatus member in failedMembers)
      {
        response.Writer.WriteStartElement(DAVNames.response);
        response.Writer.WriteStartElement(DAVNames.href);
        response.Writer.WriteString(member.ServiceRoot);
        response.Writer.WriteString(DAVUtility.UriPathPartialEncode(member.RelativePath));
        response.Writer.WriteEndElement();
        response.WriteStatus(member.Status);
        response.Writer.WriteEndElement();
      }
    }
  }

  #region Accept
  /// <summary>Represents an HTTP token and qvalue from an Accept-* header.</summary>
  struct Accept
  {
    public Accept(string headerValue)
    {
      Name       = headerValue;
      Preference = 0.5f;

      Match m = acceptRe.Match(headerValue);
      if(m.Success)
      {
        Name = m.Groups["token"].Value;
        if(m.Groups["q"].Success) Preference = float.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture);
      }
    }

    public Accept(string name, float preference)
    {
      Name       = name;
      Preference = preference;
    }

    public string Name;
    public float Preference;

    // note: this regular expression doesn't match all valid encodings, because tokens can contain more than letters. however, it matches
    // all of the encodings that we support (i.e. gzip, deflate, and identity, and the * pseudo-encoding)
    static readonly Regex acceptRe = new Regex(@"^(?<token>\*|[A-Za-z]+)(?:;q=(?<q>\d(?:\.\d+)?)(?:;.*)?)?$",
                                               RegexOptions.Compiled | RegexOptions.ECMAScript);
  }
  #endregion

  #region RestrictiveResolver
  /// <summary>Implements an <see cref="XmlResolver"/> that disallows external entities.</summary>
  sealed class RestrictiveResolver : XmlResolver
  {
    RestrictiveResolver() { }

    public override System.Net.ICredentials Credentials
    {
      set { } // we don't care about the credentials
    }

    public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
    {
      throw new WebDAVException(ConditionCodes.NoExternalEntities); // no external entities are allowed
    }

    public static readonly RestrictiveResolver Instance = new RestrictiveResolver();
  }
  #endregion

  readonly IAuthorizationFilter[] authFilters;
  string _currentUserId;

  /// <summary>Returns a default preference value for a content encoding.</summary>
  static int GetDefaultPreference(string encoding)
  {
    if(encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase)) return 3;
    else if(encoding.Equals("deflate", StringComparison.OrdinalIgnoreCase)) return 2;
    else if(encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) return 1;
    else return 0;
  }
}

} // namespace AdamMil.WebDAV.Server
