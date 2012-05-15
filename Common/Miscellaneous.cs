using System.Collections.Generic;
using System.Text;

namespace HiA.WebDAV
{

static class DAVUtility
{
  /// <summary>Gets the canonical message corresponding to an HTTP status code.</summary>
  public static string GetStatusCodeMessage(int httpStatusCode)
  {
    return statusMessages.TryGetValue(httpStatusCode);
  }

  /// <summary>Quotes a string in accordance with the <c>quoted-string</c> format defined in RFC 2616.</summary>
  public static string QuoteString(string value)
  {
    if(value != null)
    {
      for(int i=0; i<value.Length; i++)
      {
        char c = value[i];
        if(c == '"' || c == '\\')
        {
          StringBuilder sb = new StringBuilder(value.Length + 10);
          sb.Append(value, 0, i);
          for(; i<value.Length; i++)
          {
            c = value[i];
            if(c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
          }
          value = sb.ToString();
          break;
        }
      }
    }
    return value;
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

} // namespace HiA.WebDAV