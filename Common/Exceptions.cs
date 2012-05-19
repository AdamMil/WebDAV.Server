using System;
using System.Net;
using System.Runtime.Serialization;
using System.Web;
using System.Xml;

namespace HiA.WebDAV.Server
{

#region Exceptions
static class Exceptions
{
  public static WebDAVException BadRequest(string message)
  {
    return new WebDAVException((int)HttpStatusCode.BadRequest, message);
  }

  public static HttpException DuplicateElement(XmlQualifiedName qname)
  {
    return BadRequest(qname.ToString() + " element was duplicated.");
  }
}
#endregion

#pragma warning disable 1591 // ignore errors about missing doc comments, since the comments on exceptions are obvious and repetitive

#region WebDAVException
/// <summary>The base class of all exceptions specific to WebDAV requests.</summary>
[Serializable]
public class WebDAVException : HttpException
{
  public WebDAVException() : base("An unknown WebDAV error has occurred.") { }

  public WebDAVException(ConditionCode conditionCode) : this(conditionCode, null) { }
  public WebDAVException(ConditionCode conditionCode, Exception innerException)
    : base(GetStatusCode(conditionCode), conditionCode.Message, innerException)
  {
    ConditionCode = conditionCode;
  }

  public WebDAVException(int httpStatusCode, string message) : base(httpStatusCode, message) { }
  public WebDAVException(int httpStatusCode, string message, Exception innerException) : base(httpStatusCode, message, innerException) { }
  public WebDAVException(string message) : base(message) { }
  public WebDAVException(string message, Exception innerException) : base(message, innerException) { }
  protected WebDAVException(SerializationInfo info, StreamingContext context) : base(info, context) { }

  /// <summary>Gets the WebDAV condition code associated with this exception, or null if no code was passed to the constructor.</summary>
  public ConditionCode ConditionCode { get; private set; }

  static int GetStatusCode(ConditionCode conditionCode)
  {
    if(conditionCode == null) throw new ArgumentNullException();
    return conditionCode.StatusCode;
  }
}
#endregion

#region ContractViolationException
[Serializable]
public class ContractViolationException : WebDAVException
{
  public ContractViolationException() : this("A WebDAV service has violated its contract.", null) { }
  public ContractViolationException(string message) : this(message, null) { }
  public ContractViolationException(string message, Exception innerException)
    : base((int)HttpStatusCode.InternalServerError, message, innerException) { }
  protected ContractViolationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
#endregion

} // namespace HiA.WebDAV.Server
