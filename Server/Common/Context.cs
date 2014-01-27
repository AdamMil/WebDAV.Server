/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2013 by Adam Milazzo.

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
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.IO;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

/// <summary>Contains information about the current WebDAV request.</summary>
public sealed class WebDAVContext : IDisposable
{
  internal WebDAVContext(IWebDAVService service, string serviceRootPath, string requestPath, HttpApplication app,
                         ILockManager lockManager, IPropertyStore propertyStore, Configuration config)
  {
    Service       = service;
    ServiceRoot   = serviceRootPath;
    RequestPath   = requestPath;
    Application   = app;
    Request       = app.Request;
    Response      = app.Response;
    LockManager   = lockManager;
    PropertyStore = propertyStore is DisablePropertyStore ? null : propertyStore; // DisablePropertyStore disallows dead property setting
    Settings      = config;
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

  /// <summary>Gets the default <see cref="ILockManager"/> to be used with the request, or null if no lock manager is configured.</summary>
  public ILockManager LockManager { get; private set; }

  /// <summary>Gets the default <see cref="IPropertyStore"/> to be used with the request, or null if no property store is configured.</summary>
  public IPropertyStore PropertyStore { get; private set; }

  /// <summary>Gets the <see cref="HttpRequest"/> associated with the WebDAV request.</summary>
  public HttpRequest Request { get; private set; }

  /// <summary>Gets the path of the request URL, relative to <see cref="ServiceRoot"/>. As a relative path, it will not contain a leading
  /// slash.
  /// </summary>
  public string RequestPath { get; private set; }

  /// <summary>Gets the <see cref="IWebDAVResource"/> mapped to the WebDAV request URI, or null if the resource has not yet been resolved
  /// or the request was made to a URL that was not mapped to any resource.
  /// </summary>
  public IWebDAVResource RequestResource { get; internal set; }

  /// <summary>Gets the <see cref="HttpResponse"/> associated with the WebDAV request. In general, you should not write directly to the
  /// response, but instead use the helper methods provided by the WebDAV framework, such as <see cref="OpenXmlResponse"/> and
  /// <see cref="OpenMultiStatusResponse"/>.
  /// </summary>
  public HttpResponse Response { get; private set; }

  /// <summary>Gets the <see cref="IWebDAVService"/> responsible for serving this request.</summary>
  public IWebDAVService Service { get; private set; }

  /// <summary>Gets the absolute path to the root of the <see cref="Service"/>, including the trailing slash.</summary>
  public string ServiceRoot { get; private set; }

  /// <summary>Gets additional configuration settings for the WebDAV service in the current context.</summary>
  public Configuration Settings { get; private set; }

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

  // TODO: add example
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
    Response.StatusCode        = 207;
    Response.StatusDescription = DAVUtility.GetStatusCodeMessage(207); // 207 is an extension, so set the description manually
    return new MultiStatusResponse(OpenXmlResponse(null), namespaces);
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
    string[] encodings = DAVUtility.ParseHttpTokenList(Request.Headers[HttpHeaders.ContentEncoding]);
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

  /// <include file="documentation.xml" path="/DAV/WebDAVContext/OpenResponseBody/node()" />
  public Stream OpenResponseBody()
  {
    return OpenResponseBody(true);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVContext/OpenResponseBody/node()" />
  /// <param name="enableCompression">Determines whether compression is enabled. If true, a compressed content encoding will be chosen if
  /// it's preferred by the client. Otherwise, only the uncompressed <c>identity</c> encoding can be chosen.
  /// </param>
  public Stream OpenResponseBody(bool enableCompression)
  {
    Stream stream = Response.OutputStream;
    bool wrappedStream = false;

    // parse the Accept-Encoding header so that we can encode the content appropriately (see RFC 2616 section 14.3)
    string[] encodings = DAVUtility.ParseHttpTokenList(Request.Headers[HttpHeaders.AcceptEncoding]);
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

      string encoding = "identity"; // the identity encoding is assumed to always be available unless it's explicitly disallowed
      foreach(Accept acceptable in acceptedEncodings)
      {
        string name = acceptable.Name;
        if(acceptable.Preference != 0) // if the encoding is allowed...
        {
          if(enableCompression && (name.OrdinalEquals("gzip") || name.OrdinalEquals("deflate")) || name.OrdinalEquals("identity"))
          {
            encoding = name; // use it
            break;
          }
        }
        else if(name.OrdinalEquals("identity")) // otherwise, if identity is disallowed...
        {
          encoding = null; // we can't use it
          break;
        }
      }

      if(encoding == null)
      {
        throw new WebDAVException((int)HttpStatusCode.NotAcceptable,
                                  "No content encoding supported by the server was acceptable to the client.");
      }
      else if(encoding.OrdinalEquals("gzip"))
      {
        stream = new GZipStream(stream, CompressionMode.Compress, true);
        wrappedStream = true;
      }
      else if(encoding.OrdinalEquals("deflate"))
      {
        stream = new DeflateStream(stream, CompressionMode.Compress, true);
        wrappedStream = true;
      }

      if(wrappedStream) Response.Headers[HttpHeaders.ContentEncoding] = encoding;
    }

    return wrappedStream ? stream : new DelegateStream(stream, false); // make sure the real output stream won't get closed
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
    if(status != null)
    {
      Response.StatusCode        = status.StatusCode;
      Response.StatusDescription = DAVUtility.GetStatusCodeMessage(status.StatusCode);
    }

    Response.ContentEncoding   = System.Text.Encoding.UTF8;
    Response.ContentType       = "application/xml"; // media type specified by RFC 4918 section 8.2
    return XmlWriter.Create(OpenResponseBody(), new XmlWriterSettings() { CloseOutput = true, IndentChars = "\t" });
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
        response.Writer.WriteStartElement(DAVNames.response.Name);
        response.Writer.WriteElementString(DAVNames.href.Name, member.AbsolutePath);
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

  void IDisposable.Dispose()
  {
    if(!Service.IsReusable) Utility.Dispose(Service);
  }

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
