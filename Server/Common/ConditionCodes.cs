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
using System.Xml;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

#region ConditionCode
/// <summary>A representation of a WebDAV status code, which gives additional information about a result.</summary>
[Serializable]
public class ConditionCode
{
  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code.</summary>
  public ConditionCode(HttpStatusCode statusCode) : this((int)statusCode, null, null) { }
  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code.</summary>
  public ConditionCode(int statusCode) : this(statusCode, null, null) { }
  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code plus an additional message.</summary>
  public ConditionCode(HttpStatusCode statusCode, string message) : this((int)statusCode, null, message) { }
  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code plus an additional message.</summary>
  public ConditionCode(int statusCode, string message) : this(statusCode, null, message) { }

  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code, the name of a WebDAV error element, and an
  /// additional message.
  /// </summary>
  public ConditionCode(HttpStatusCode statusCode, XmlQualifiedName errorElement, string message)
    : this((int)statusCode, errorElement, message) { }

  /// <summary>Initializes a new <see cref="ConditionCode"/> based on an HTTP status code, the name of a WebDAV error element, and an
  /// additional message.
  /// </summary>
  public ConditionCode(int httpStatusCode, XmlQualifiedName errorElement, string message)
  {
    StatusCode   = httpStatusCode;
    ErrorElement = errorElement;
    Message      = message;
  }
  
  /// <summary>Gets the name of the root element that will be rendered within the <c>DAV:error</c> element, or null if no <c>DAV:error</c>
  /// element should be sent to the client. This is used to send WebDAV precondition and postcondition codes to the client and to provide
  /// additional structured information about errors.
  /// </summary>
  public XmlQualifiedName ErrorElement { get; private set; }

  /// <summary>Gets whether the <see cref="StatusCode"/> is a 4xx client error code.</summary>
  public bool IsClientError
  {
    get { return StatusCode >= 400 && StatusCode < 500; }
  }

  /// <summary>Gets whether the <see cref="StatusCode"/> is a 1xx information code.</summary>
  public bool IsInformational
  {
    get { return StatusCode < 200 && StatusCode >= 100; }
  }

  /// <summary>Gets whether the <see cref="StatusCode"/> is a 3xx redirection code. Note that this does not necessarily indicate a typical
  /// 301 Moved Permanently code, but can be any 3xx code, including 304 Not Modified which is a redirection in name only.
  /// </summary>
  public bool IsRedirection
  {
    get { return StatusCode < 200 && StatusCode >= 100; }
  }

  /// <summary>Gets whether the <see cref="StatusCode"/> is a 5xx server error code.</summary>
  public bool IsServerError
  {
    get { return StatusCode >= 500 && StatusCode < 600; }
  }

  /// <summary>Gets whether the <see cref="StatusCode"/> is a 2xx success code.</summary>
  public bool IsSuccessful
  {
    get { return StatusCode < 300 && StatusCode >= 200; }
  }

  /// <summary>Gets the additional description of the result, which will be rendered in the <c>DAV:responsedescription</c> element, or null
  /// if no <c>DAV:responsedescription</c> element should be sent to the client.
  /// </summary>
  public string Message { get; private set; }

  /// <summary>Gets the HTTP status code representing the status of the result.</summary>
  public int StatusCode { get; private set; }

  /// <inheritdoc/>
  public sealed override bool Equals(object obj)
  {
    return Equals(obj as ConditionCode);
  }

  /// <include file="documentation.xml" path="/DAV/ConditionCode/Equals/node()" />
  public virtual bool Equals(ConditionCode other)
  {
    return other == this ||
           other != null && StatusCode == other.StatusCode && ErrorElement == other.ErrorElement &&
           string.Equals(Message, other.Message, StringComparison.Ordinal) && GetType() == other.GetType();
  }

  /// <inheritdoc/>
  public override int GetHashCode()
  {
    int hash = StatusCode;
    if(ErrorElement != null) hash ^= ErrorElement.GetHashCode();
    if(Message != null) hash ^= Message.GetHashCode();
    return hash;
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return StringUtility.Combine(" ", StatusCode.ToStringInvariant(), DAVUtility.GetStatusCodeMessage(StatusCode),
                                 ErrorElement == null ? null : ErrorElement.ToString(), Message);
  }

  /// <summary>If <see cref="ErrorElement"/> is not null, this method may be called to write a description of the error. The context will
  /// be a <c>DAV:error</c> element (which will have already been written) where the <c>DAV:</c> namespace has been defined as the default
  /// namespace. If any other namespaces are needed, appropriate <c>xmlns</c> attributes should be used to define them.
  /// </summary>
  protected virtual void WriteErrorElement(XmlWriter writer)
  {
    if(ErrorElement == null) throw new InvalidOperationException();
    writer.WriteEmptyElement(ErrorElement);
  }

  /// <summary>Gets the content that should be sent in the <c>DAV:status</c> element.</summary>
  internal string DAVStatusText
  {
    get { return StringUtility.Combine(" ", "HTTP/1.1 " + StatusCode.ToStringInvariant(), DAVUtility.GetStatusCodeMessage(StatusCode)); }
  }

  /// <summary>If <see cref="ErrorElement"/> is not null, this method should write a <c>DAV:error</c> element representing the error.</summary>
  internal void WriteErrorXml(XmlWriter writer)
  {
    if(ErrorElement != null)
    {
      writer.WriteStartElement(DAVNames.error);
      if(writer.LookupPrefix(DAVNames.DAV) == null) writer.WriteAttributeString("xmlns", DAVNames.DAV); // define our namespace if necessary
      WriteErrorElement(writer);
      writer.WriteEndElement();
    }
  }
}
#endregion

#region LockConditionCodeWithUrls
/// <summary>Represents a <see cref="ConditionCode"/> for lock-based errors that return a list of related resource URLs.</summary>
public abstract class LockConditionCodeWithUrls : ConditionCode
{
  /// <summary>Initializes a <see cref="LockConditionCodeWithUrls"/> condition code.</summary>
  /// <param name="httpStatusCode">The HTTP status code related to the error condition.</param>
  /// <param name="errorElement">The name of the WebDAV precondition error element. This parameter is required.</param>
  /// <param name="message">An additional message to send to the client.</param>
  /// <param name="absoluteResourcePaths">An array of absolute paths to the locked resources related to the error.</param>
  protected LockConditionCodeWithUrls(int httpStatusCode, XmlQualifiedName errorElement, string message, string[] absoluteResourcePaths)
    : base(httpStatusCode, errorElement, message)
  {
    if(absoluteResourcePaths == null || errorElement == null) throw new ArgumentNullException();
    foreach(string path in absoluteResourcePaths)
    {
      if(path == null) throw new ArgumentException("The list of locked resource paths contained a null value.");
      if(path.Length == 0 || path[0] != '/') throw new ArgumentException("All paths must be absolute.");
    }
    this.resourcePaths = (string[])absoluteResourcePaths.Clone(); // clone the array to prevent later modifications
  }

  /// <include file="documentation.xml" path="/DAV/ConditionCode/Equals/node()" />
  public override bool Equals(ConditionCode other)
  {
    if(other == this) return true;
    else if(!base.Equals(other)) return false;

    LockConditionCodeWithUrls code = (LockConditionCodeWithUrls)other; // base.Equals() checked that the types are the same
    if(resourcePaths.Length == code.resourcePaths.Length)
    {
      for(int i=0; i<resourcePaths.Length; i++)
      {
        if(!resourcePaths[i].OrdinalEquals(code.resourcePaths[i])) return false;
      }
      return true;
    }

    return false;
  }

  /// <summary>Writes the <see cref="ConditionCode.ErrorElement"/> containing <c>DAV:href</c> tags pointing to the related locked
  /// resources.
  /// </summary>
  protected override void WriteErrorElement(XmlWriter writer)
  {
    if(writer == null) throw new ArgumentNullException();
    writer.WriteStartElement(ErrorElement);
    foreach(string path in resourcePaths) writer.WriteElementString(DAVNames.href, path);
    writer.WriteEndElement();
  }

  readonly string[] resourcePaths;
}
#endregion

#region LockTokenSubmittedConditionCode
/// <summary>The DAV:lock-token-submitted precondition, used when the request attempts to modify a locked resource, and the corresponding
/// lock token was not submitted.
/// </summary>
public class LockTokenSubmittedConditionCode : LockConditionCodeWithUrls
{
  /// <summary>Initializes a new <see cref="LockTokenSubmittedConditionCode"/>.</summary>
  /// <param name="absoluteResourcePaths">An array of absolute paths to the locked resources related to the error. There must be at
  /// least one path in the array.
  /// </param>
  public LockTokenSubmittedConditionCode(params string[] absoluteResourcePaths)
    : base(423, new XmlQualifiedName("lock-token-submitted", DAVNames.DAV),
           "A lock token was not submitted for one or more locked resources.", absoluteResourcePaths)
  {
    if(absoluteResourcePaths.Length == 0) throw new ArgumentException("The list of locked resource paths was empty.");
  }
}
#endregion

#region NoConflictingLockConditionCode
/// <summary>The DAV:no-conflicting-lock precondition, used when a LOCK request fails due to the presence of a preexisting, conflicting
/// lock.
/// </summary>
public class NoConflictingLockConditionCode : LockConditionCodeWithUrls
{
  /// <summary>Initializes a new <see cref="NoConflictingLockConditionCode"/>.</summary>
  /// <param name="absoluteResourcePaths">An array of absolute paths to the locked resources related to the error.</param>
  public NoConflictingLockConditionCode(params string[] absoluteResourcePaths) : this(423, absoluteResourcePaths) { }
  /// <summary>Initializes a new <see cref="NoConflictingLockConditionCode"/> with a particular HTTP status code.</summary>
  /// <param name="httpStatusCode">The HTTP status code related to the error condition.</param>
  /// <param name="absoluteResourcePaths">An array of absolute paths to the locked resources related to the error.</param>
  public NoConflictingLockConditionCode(int httpStatusCode, params string[] absoluteResourcePaths)
    : base(httpStatusCode, new XmlQualifiedName("no-conflicting-lock", DAVNames.DAV), "The requested lock conflicts with an existing lock.",
           absoluteResourcePaths) { }
}
#endregion

#region ConditionCodes
/// <summary>Contains the WebDAV precondition and postcondition codes defined by RFC 4918 that do not require additional parameters, as
/// well as commonly-used condition codes based on generic HTTP status codes.
/// </summary>
public static class ConditionCodes
{
  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 502 Bad Gateway status code.</summary>
  public static readonly ConditionCode BadGateway = new ConditionCode(HttpStatusCode.BadGateway);

  /// <summary>The DAV:cannot-modify-protected-property precondition, used when a PROPPATCH request attempts to modify a protected
  /// property.
  /// </summary>
  public static readonly ConditionCode CannotModifyProtectedProperty =
    new ConditionCode(HttpStatusCode.Forbidden, new XmlQualifiedName("cannot-modify-protected-property", DAVNames.DAV),
                      "An attempt was made to set a protected property.");

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 409 Conflict status code.</summary>
  public static readonly ConditionCode Conflict = new ConditionCode(HttpStatusCode.Conflict);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 201 Created status code.</summary>
  public static readonly ConditionCode Created = new ConditionCode(HttpStatusCode.Created);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 424 Failed Dependency status code.</summary>
  public static readonly ConditionCode FailedDependency = new ConditionCode(424);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 403 Forbidden status code.</summary>
  public static readonly ConditionCode Forbidden = new ConditionCode(HttpStatusCode.Forbidden);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 507 Insufficient Storage status code.</summary>
  public static readonly ConditionCode InsufficientStorage = new ConditionCode(507);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 423 Locked status code. You may want to use the more specific
  /// <see cref="LockTokenSubmittedConditionCode"/> and <see cref="NoConflictingLockConditionCode"/> condition codes.
  /// </summary>
  public static readonly ConditionCode Locked = new ConditionCode(423);

  /// <summary>The DAV:lock-token-matches-request-uri precondition, based on the HTTP 409 Conflict status code, used when the request URL
  /// does not lie within the scope of the lock token submitted in the <c>Lock-Token</c> header.
  /// </summary>
  public static readonly ConditionCode LockTokenMatchesRequestUri409 =
    new ConditionCode(HttpStatusCode.Conflict, new XmlQualifiedName("lock-token-matches-request-uri", DAVNames.DAV),
      "The request URI does not fall within the scope of the lock token.");

  /// <summary>The DAV:lock-token-matches-request-uri precondition, based on the HTTP 412 Precondition Failed status code, used when the
  /// request URL does not lie within the scope of the lock token submitted in the <c>Lock-Token</c> header.
  /// </summary>
  public static readonly ConditionCode LockTokenMatchesRequestUri412 =
    new ConditionCode(HttpStatusCode.Conflict, new XmlQualifiedName("lock-token-matches-request-uri", DAVNames.DAV),
      "The request URI does not fall within the scope of the lock token.");

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 405 Method Not Allowed status code.</summary>
  public static readonly ConditionCode MethodNotAllowed = new ConditionCode(HttpStatusCode.MethodNotAllowed);

  /// <summary>The DAV:no-conflicting-lock precondition, used when a LOCK request fails due to the presence of a preexisting, conflicting
  /// lock. The condition code represented by this field does not provide the URL of the resource with the conflicting lock. If known, you
  /// should construct and use a <see cref="NoConflictingLockConditionCode"/> instead.
  /// </summary>
  public static readonly ConditionCode NoConflictingLock = new NoConflictingLockConditionCode();

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 204 No Content status code.</summary>
  public static readonly ConditionCode NoContent = new ConditionCode(HttpStatusCode.NoContent);

  /// <summary>The DAV:no-external-entities precondition, used when a request body contains an external XML entity and the server does not
  /// allow that.
  /// </summary>
  public static readonly ConditionCode NoExternalEntities =
    new ConditionCode(HttpStatusCode.Forbidden, new XmlQualifiedName("no-external-entities", DAVNames.DAV),
                      "This server does not allow external XML entities.");

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 406 Not Acceptable status code.</summary>
  public static readonly ConditionCode NotAcceptable = new ConditionCode(HttpStatusCode.NotAcceptable);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 404 Not Found status code.</summary>
  public static readonly ConditionCode NotFound = new ConditionCode(HttpStatusCode.NotFound);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 501 Not Implemented status code.</summary>
  public static readonly ConditionCode NotImplemented = new ConditionCode(HttpStatusCode.NotImplemented);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 304 Not Modified status code.</summary>
  public static readonly ConditionCode NotModified = new ConditionCode(HttpStatusCode.NotModified);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 200 OK status code.</summary>
  public static readonly ConditionCode OK = new ConditionCode(HttpStatusCode.OK);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 412 Precondition Failed status code.</summary>
  public static readonly ConditionCode PreconditionFailed = new ConditionCode(HttpStatusCode.PreconditionFailed);

  /// <summary>The DAV:preserved-live-properties postcondition, used when a COPY or MOVE request is unable to maintain one or more live
  /// properties with the same behavior and semantics at the destination.
  /// </summary>
  public static readonly ConditionCode PreservedLiveProperties =
    new ConditionCode(HttpStatusCode.Conflict, new XmlQualifiedName("preserved-live-properties", DAVNames.DAV),
                      "The server received a valid COPY or MOVE request, but was unable to preserve all of the live properties at the " +
                      "destination.");

  /// <summary>The DAV:propfind-finite-depth precondition, used when an infinite-depth PROPFIND request is not supported by a collection.</summary>
  public static readonly ConditionCode PropFindFiniteDepth =
    new ConditionCode(HttpStatusCode.Forbidden, new XmlQualifiedName("propfind-finite-depth", DAVNames.DAV),
                      "This server does not allow infinite-depth PROPFIND requests on this collection.");

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 416 Requested Range Not Satisfiable status code.</summary>
  public static readonly ConditionCode RequestedRangeNotSatisfiable = new ConditionCode(HttpStatusCode.RequestedRangeNotSatisfiable);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 401 Unauthorized status code.</summary>
  public static readonly ConditionCode Unauthorized = new ConditionCode(HttpStatusCode.Unauthorized);

  /// <summary>A <see cref="ConditionCode"/> based on the HTTP 415 Unsupported Media Type status code.</summary>
  public static readonly ConditionCode UnsupportedMediaType = new ConditionCode(HttpStatusCode.UnsupportedMediaType);
}
#endregion

} // namespace AdamMil.WebDAV.Server
