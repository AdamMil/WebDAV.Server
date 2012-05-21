using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Xml;
using HiA.IO;

namespace HiA.WebDAV.Server
{

/// <summary>Contains information about the current WebDAV request.</summary>
public sealed class WebDAVContext
{
  internal WebDAVContext(string serviceRootPath, string requestPath, HttpApplication app, Configuration config)
  {
    ServiceRoot = serviceRootPath;
    RequestPath = requestPath;
    Request     = app.Request;
    Response    = app.Response;
    Settings    = config;
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
  /// response, but instead use the helper methods provided by the WebDAV framework.
  /// </summary>
  public HttpResponse Response { get; private set; } // TODO: give example of some of those helper methods (after we create them!)

  /// <summary>Gets the absolute path to the root of the WebDAV service, including the trailing slash.</summary>
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
  /// as the default namespace in the response, and need not be named explicitly. If null, no additional namespaces will be defined. The
  /// prefixes allocated during this method are the single letters 'a' through 'z', and prefixes of the form "ns30" where "30" can be
  /// replaced by any positive integer. If you define your own namespace prefixes within the response, be careful not to use prefixes that
  /// would clash in incompatible ways.
  /// </param>
  /// <remarks>The <see cref="MultiStatusResponse"/> object returned must be disposed to complete the response. The best practice is to use
  /// a <c>using</c> statement to ensure that the response is disposed. The disposal of the response does not terminate the web request.
  /// </remarks>
  public MultiStatusResponse OpenMultiStatusResponse(HashSet<string> namespaces)
  {
    // begin outputting a multistatus (HTTP 207) response as defined in RFC 4918
    Response.StatusCode        = 207;
    Response.StatusDescription = DAVUtility.GetStatusCodeMessage(207); // 207 is an extension, so set the description manually
    Response.ContentEncoding   = System.Text.Encoding.UTF8;
    Response.ContentType       = "application/xml"; // media type specified by RFC 4918 section 8.2

    // TODO: we remove Indent and IndentChars unless we can easily preserve whitespace in property values
    XmlWriterSettings settings = new XmlWriterSettings() { CloseOutput = false, Indent = true, IndentChars = "\t" };
    return new MultiStatusResponse(XmlWriter.Create(Response.OutputStream, settings), namespaces);
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
    using(StreamReader reader = new StreamReader(new DelegateStream(Request.InputStream, false), Request.ContentEncoding))
    {
      return reader.ReadToEnd();
    }
  }

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
}

} // namespace HiA.WebDAV.Server
