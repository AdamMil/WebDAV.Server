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
using System.Net;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Web;
using System.Xml;

namespace AdamMil.WebDAV.Server
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

#pragma warning disable 1591 // ignore errors about missing doc comments, since most comments on exceptions are obvious and repetitive

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

  protected WebDAVException(SerializationInfo info, StreamingContext context) : base(info, context)
  {
    if(info == null) throw new ArgumentNullException();
    ConditionCode = (ConditionCode)info.GetValue("ConditionCode", typeof(ConditionCode));
  }

  /// <summary>Gets the WebDAV condition code associated with this exception, or null if no code was passed to the constructor.</summary>
  public ConditionCode ConditionCode { get; private set; }

  /// <inheritdoc/>
  [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    if(info == null) throw new ArgumentNullException();
    base.GetObjectData(info, context);
    info.AddValue("ConditionCode", ConditionCode);
  }

  static int GetStatusCode(ConditionCode conditionCode)
  {
    if(conditionCode == null) throw new ArgumentNullException();
    return conditionCode.StatusCode;
  }
}
#endregion

#region ContractViolationException
/// <summary>Indicates that an <see cref="IWebDAVService"/> or <see cref="IWebDAVResource"/> is not correctly implemented.</summary>
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

#region LockConflictException
/// <summary>Indicates that an operation could not be completed because of a lock on a resource.</summary>
[Serializable]
public class LockConflictException : WebDAVException
{
  public LockConflictException() : this("There is a conflict with an existing lock.", null) { }
  public LockConflictException(string message) : this(message, null) { }
  public LockConflictException(string message, Exception innerException) : base(423, message, innerException) { }
  public LockConflictException(ActiveLock conflictingLock) : this(GetMessage(conflictingLock)) { }
  protected LockConflictException(SerializationInfo info, StreamingContext context) : base(info, context) { }

  static string GetMessage(ActiveLock conflictingLock)
  {
    if(conflictingLock == null) throw new ArgumentNullException();
    return "There is a lock conflict with " + conflictingLock.ToString();
  }
}
#endregion

#region LockLimitReachedException
/// <summary>Indicates that a resource could not be locked because a lock limit was reached.</summary>
[Serializable]
public class LockLimitReachedException : WebDAVException
{
  public LockLimitReachedException() : this("The lock limit on this resource has been reached.", null) { }
  public LockLimitReachedException(string message) : this(message, null) { }
  public LockLimitReachedException(string message, Exception innerException)
    : base((int)HttpStatusCode.ServiceUnavailable, message, innerException) { }
  protected LockLimitReachedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
#endregion

} // namespace AdamMil.WebDAV.Server
