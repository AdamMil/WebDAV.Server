using System;
using System.IO;
using System.Web;
using System.Xml;
using HiA.IO;

namespace HiA.WebDAV
{

/// <summary>Contains information about the current WebDAV request.</summary>
public sealed class WebDAVContext
{
  internal WebDAVContext(string serviceRootPath, string requestPath, HttpApplication app)
  {
    ServiceRoot = serviceRootPath;
    RequestPath = requestPath;
    Request     = app.Request;
    Response    = app.Response;
  }

  /// <summary>Gets the <see cref="HttpRequest"/> associated with the WebDAV request.</summary>
  public HttpRequest Request { get; private set; }

  /// <summary>Gets the path of the request URL, relative to <see cref="ServiceRoot"/>. As a relative path, it will not contain a leading
  /// slash.
  /// </summary>
  public string RequestPath { get; private set; }

  /// <summary>Gets the <see cref="IWebDAVResource"/> mapped to the WebDAV request URI, or null if the resource has not yet been resolved.</summary>
  public IWebDAVResource RequestResource { get; internal set; }

  /// <summary>Gets the <see cref="HttpResponse"/> associated with the WebDAV request. In general, you should not write directly to the
  /// response, but instead use the helper methods provided by the WebDAV framework.
  /// </summary>
  public HttpResponse Response { get; private set; } // TODO: give example of some of those helper methods (after we create them!)

  /// <summary>Gets the absolute path to the root of the WebDAV service, including the trailing slash.</summary>
  public string ServiceRoot { get; private set; }

  /// <summary>Returns an <see cref="XmlDocument"/> containing the request body loaded as XML, or null if the body is empty.</summary>
  public XmlDocument LoadBodyXml()
  {
    XmlDocument xml = null;
    using(XmlReader reader = OpenBodyXml())
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
  public XmlReader OpenBodyXml()
  {
    string bodyText = ReadBodyText();
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

  /// <summary>Returns the request body as text.</summary>
  public string ReadBodyText()
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

} // namespace HiA.WebDAV