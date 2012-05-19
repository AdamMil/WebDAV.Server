using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;

// TODO: move DAVUtility to the HiA.WebDAV namespace so it can be used by clients?

namespace HiA.WebDAV.Server
{

#region DAVUtility
/// <summary>Contains useful utilities for DAV services.</summary>
public static class DAVUtility
{
  /// <summary>Encodes a string into the canonical URL path form so that it can be used to construct URL paths.</summary>
  /// <remarks>This method only encodes the question mark (<c>?</c>) and number sign (<c>#</c>), which is the minimal encoding required
  /// within a URL path.
  /// </remarks>
  public static string CanonicalPathEncode(string path)
  {
    if(path != null)
    {
      for(int i=0; i<path.Length; i++)
      {
        char c = path[i];
        if(c == '?' || c == '#')
        {
          StringBuilder sb = new StringBuilder(path.Length + 10);
          sb.Append(path, 0, i);
          while(true)
          {
            if(c == '?') sb.Append("%3f");
            else if(c == '#') sb.Append("%23");
            else sb.Append(c);
            if(++i == path.Length) break;
            c = path[i];
          }
          path = sb.ToString();
          break;
        }
      }
    }

    return path;
  }

  /// <summary>Gets the canonical message corresponding to an HTTP status code, or the message for the given status code is unknown.</summary>
  public static string GetStatusCodeMessage(int httpStatusCode)
  {
    return statusMessages.TryGetValue(httpStatusCode);
  }

  /// <summary>Returns a random MIME boundary.</summary>
  internal static string CreateMimeBoundary()
  {
    // technically, a MIME boundary must be guaranteed to not collide with any data in the message body, but that is unreasonably difficult
    // to ensure (i.e. MIME sucks!), so we'll use a random MIME boundary. MIME boundaries can be up to 69 characters in length, and we'll
    // use all 69 characters to provide the lowest chance of a collision with any data in the message body (although even 16 characters
    // would provide an insanely low chance of collision). we'll use a strong random number generator to provide us with random bytes.
    // since there are 74 characters in the MIME boundary alphabet, each character requires log2(74) ~= 6.21 bits. each random byte
    // provides us with 8 bits, so we need ceil(log2(74) / 8 * 69) = 54 total random bytes. we actually care about reducing the number of
    // random bytes generated because we're using a cryptographic RNG and want to consume no more entropy from the system than is necessary
    // (just in case entropy is actually consumed by the RNG)
    const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ',.()-=_+/?:"; // legal MIME boundary characters
    char[] chars = new char[69]; // 69 characters in the boundary
    byte[] bytes = new byte[54]; // we'll need 54 random bytes
    System.Security.Cryptography.RandomNumberGenerator.Create().GetBytes(bytes);

    for(int value=0, ci=0, bi=0, bits=0; ci < chars.Length; ci++)
    {
      if(bits < 621) // use a simple form of integer math to keep track of how many random bits we have (times 100). the accumulated error
      {              // over the length of a MIME boundary is insignificant (but it would be significant if we only multiplied by 10)
        value = value*74 + bytes[bi++];
        bits += 800; // we received 8 new random bits
      }

      chars[ci] = Alphabet[value % 74];
      value /= 74;
      bits -= 621; // we consumed ~6.21 random bits
    }
    return new string(chars);
  }

  /// <summary>Encodes an ASCII string as an RFC 2616 <c>quoted-string</c> if it has any characters that need encoding.</summary>
  internal static string HeaderEncode(string ascii)
  {
    if(ascii != null)
    {
      for(int i=0; i<ascii.Length; i++)
      {
        char c = ascii[i];
        if(c < 32 && c != '\t' || c == 0x7f)
        {
          ascii = QuoteString(ascii);
          break;
        }
      }
    }

    return ascii;
  }

  /// <summary>Quotes an ASCII string (which must not be null) in accordance with the <c>quoted-string</c> format defined in RFC 2616.</summary>
  internal static string QuoteString(string ascii)
  {
    if(ascii == null) throw new ArgumentNullException();
    StringBuilder sb = new StringBuilder(ascii.Length + 20);
    sb.Append('\"');
    for(int i=0; i<ascii.Length; i++)
    {
      char c = ascii[i];
      if(c < 32 && c != '\t' || c == '"' || c == '\\' || c == 0x7f) sb.Append('\\');
      sb.Append(c);
    }
    sb.Append('"');
    ascii = sb.ToString();
    return ascii;
  }

  /// <summary>Sets the response status code to the given status code and writes an message to the page. This method does not terminate
  /// the request.
  /// </summary>
  internal static void WriteStatusResponse(HttpRequest request, HttpResponse response, int httpStatusCode, string errorText)
  {
    // TODO: should we apply logic here to filter out potentially sensitive error messages (e.g. for 5xx errors), or should we trust the
    // callers to check the settings?
    response.StatusCode        = httpStatusCode;
    response.StatusDescription = DAVUtility.GetStatusCodeMessage(httpStatusCode);
    // write a response body unless the status code is No Content (indicating that there's no body) or there was a message included
    if(httpStatusCode != (int)HttpStatusCode.NoContent || !string.IsNullOrEmpty(errorText))
    {
      response.ContentType = "text/plain";
      errorText = StringUtility.Combine(". ", response.StatusDescription, errorText);
      response.Write(string.Format(CultureInfo.InvariantCulture, "{0} {1}\n{2} {3}\n",
                                   request.HttpMethod, request.Url.AbsolutePath, httpStatusCode, errorText));
    }
  }

  /// <summary>Sets the response status code to the given status code and writes an error response based on the given
  /// <see cref="ConditionCode"/>. This method does not terminate the request.
  /// </summary>
  internal static void WriteStatusResponse(HttpRequest request, HttpResponse response, ConditionCode code)
  {
    if(code.ErrorElement == null) // if the condition code has no XML error data...
    {
      WriteStatusResponse(request, response, code.StatusCode, code.Message); // just write the error as text
    }
    else // otherwise, the condition code has some structured XML data that we can insert into the response
    {
      response.StatusCode        = code.StatusCode;
      response.StatusDescription = DAVUtility.GetStatusCodeMessage(code.StatusCode);
      response.ContentEncoding   = System.Text.Encoding.UTF8;
      response.ContentType       = "application/xml"; // media type specified by RFC 4918 section 8.2

      XmlWriterSettings settings = new XmlWriterSettings() { CloseOutput = false, Indent = true, IndentChars = "\t" };
      using(XmlWriter writer = XmlWriter.Create(response.OutputStream, settings)) code.WriteErrorXml(writer);
    }
  }

  static readonly Dictionary<int, string> statusMessages = new Dictionary<int, string>()
  {
    { 100, "Continue" }, { 101, "Switching Protocols" },
    { 200, "OK" }, { 201, "Created" }, { 202, "Accepted" }, { 203, "Non-Authoritative Information" }, { 204, "No Content" },
    { 205, "Reset Content" }, { 206, "Partial Content" }, { 207, "Multi-Status" },
    { 300, "Multiple Choices" }, { 301, "Moved Permanently" }, { 302, "Found" }, { 303, "See Other" }, { 304, "Not Modified" },
    { 305, "Use Proxy" }, { 307, "Temporary Redirect" },
    { 400, "Bad Request" }, { 401, "Unauthorized" }, { 402, "Payment Required" }, { 403, "Forbidden" }, { 404, "Not Found" },
    { 405, "Method Not Allowed" }, { 406, "Not Acceptable" }, { 407, "Proxy Authentication Required" }, { 408, "Request Timeout" },
    { 409, "Conflict" }, { 410, "Gone" }, { 411, "Length Required" }, { 412, "Precondition Failed" }, { 413, "Request Entity Too Large" },
    { 414, "Request-URI Too Long" }, { 415, "Unsupported Media Type" }, { 416, "Requested Range Not Satisfiable" },
    { 417, "Expectation Failed" }, { 422, "Unprocessable Entity" }, { 423, "Locked" }, { 424, "Failed Dependency" },
    { 500, "Internal Server Error" }, { 501, "Not Implemented" }, { 502, "Bad Gateway" }, { 503, "Service Unavailable" },
    { 504, "Gateway Timeout" }, { 505, "HTTP Version Not Supported" }, { 507, "Insufficient Storage" }
  };
}
#endregion

// TODO: make this public?
#region HttpMethods
static class HttpMethods
{
  public const string Copy = "COPY", Delete = "DELETE", Get = "GET", Head = "HEAD", Lock = "LOCK", MkCol = "MKCOL", Move = "MOVE";
  public const string Options = "OPTIONS", Post = "POST", PropFind = "PROPFIND", PropPatch = "PROPPATCH", Put = "PUT", Trace = "TRACE";
  public const string Unlock = "UNLOCK";
}
#endregion

} // namespace HiA.WebDAV.Server
