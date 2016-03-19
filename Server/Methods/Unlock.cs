/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2016 by Adam Milazzo.

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
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>UNLOCK</c> request.</summary>
/// <remarks>
/// <para>The <c>UNLOCK</c> request is described in section 9.11 of RFC 4918. To service an <c>UNLOCK</c> request, you can normally just
/// call the <see cref="ProcessStandardRequest()"/> method or one of its overrides.
/// </para>
/// <para>If you would like to handle it yourself, you should remove the lock whose token is in <see cref="LockToken"/> if the client has
/// permission to remove it, and return a status of 204 No Content. The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/></term>
///   <description>The lock was removed, and a body was included in the response. There is no standard body for successful <c>UNLOCK</c>
///     requests, but you can define your own if the client can understand it.
///   </description>
/// </item>
/// <item>
/// <term>204 <see cref="ConditionCodes.NoContent">No Content</see> (default)</term>
/// <description>The lock was removed, and there is no body in the response. This is the default status code returned when
///   <see cref="WebDAVRequest.Status"/> is null.
/// </description>
/// </item>
/// <item>
///   <term>401 <see cref="ConditionCodes.Unauthorized"/></term>
///   <description>The user doesn't have permission to delete the lock, but can gain permission by authenticating with different
///     HTTP credentials.
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to delete the lock, or the server refuses to remove the lock for some other reason.</description>
/// </item>
/// <item>
///   <term>405 <see cref="ConditionCodes.MethodNotAllowed">Method Not Allowed</see></term>
///   <description>The request resource does not support locking. If you return this status code, then you must not include the <c>LOCK</c>
///     or <c>UNLOCK</c> method in responses to <c>OPTIONS</c> requests (i.e. <see cref="OptionsRequest.SupportsLocking"/> must be false).
///   </description>
/// </item>
/// <item>
///   <term>409 <see cref="ConditionCodes.Conflict"/></term>
///   <description>The lock does not exist, or the request URI is not in the scope of the lock. The
///     <c>DAV:lock-token-matches-request-uri</c> precondition code should be returned in the body.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A conditional request was not executed because the condition wasn't true.</description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="ProcessStandardRequest(string)"/></term>
///   <description>You want to change the standard request processing.</description>
/// </item>
/// </list>
/// </remarks>
public class UnlockRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="UnlockRequest"/> based on a new WebDAV request.</summary>
  public UnlockRequest(WebDAVContext context) : base(context)
  {
    // parse the Timeout header if specified
    string value = context.Request.Headers[DAVHeaders.LockToken];
    if(value != null)
    {
      int start, length;
      value.Trim(out start, out length);
      if(length >= 5 && value[start] == '<' && value[start+length-1] == '>')
      {
        value = value.Substring(start+1, length-2);
        Uri uri;
        if(Uri.TryCreate(value, UriKind.Absolute, out uri)) LockToken = value;
      }
    }

    if(LockToken == null) throw Exceptions.BadRequest("Expected a valid Lock-Token header.");
  }

  /// <summary>Gets the lock token that the client has requested to unlock.</summary>
  public string LockToken { get; private set; }

  /// <summary>Processes a standard <c>UNLOCK</c> request.</summary>
  /// <remarks>This method will attempt to unlock the request resource.</remarks>
  public void ProcessStandardRequest()
  {
    ProcessStandardRequest(null);
  }

  /// <summary>Processes a standard <c>UNLOCK</c> request.</summary>
  /// <remarks>This method will attempt to unlock the resource named by <paramref name="canonicalPath"/>, or
  /// <see cref="WebDAVContext.GetCanonicalPath"/> if that is null. This override may be useful for services that have non-canonical
  /// URIs and also may allow resources to be deleted outside of WebDAV. In that case, passing the canonical URL of the nonexistent
  /// resource will allow dangling locks to be removed.
  /// </remarks>
  public virtual void ProcessStandardRequest(string canonicalPath)
  {
    if(canonicalPath == null) canonicalPath = Context.GetCanonicalPath();
    ConditionCode precondition = CheckPreconditions(null, canonicalPath);
    if(precondition != null && precondition.IsError)
    {
      Status = precondition;
      return;
    }
    else if(Context.LockManager == null)
    {
      Status = ConditionCodes.MethodNotAllowed;
      return;
    }

    ActiveLock lockObject = Context.LockManager.GetLock(LockToken, canonicalPath);
    if(lockObject == null)
    {
      Status = ConditionCodes.LockTokenMatchesRequestUri409;
    }
    else if(!Context.CanDeleteLock(lockObject))
    {
      Status = new ConditionCode(HttpStatusCode.Forbidden, "You do not have permission to delete this lock.");
    }
    else if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      Context.LockManager.RemoveLock(lockObject);
    }
  }
}

} // namespace AdamMil.WebDAV.Server
