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
using System.Net;
using AdamMil.Utilities;

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>UNLOCK</c> request.</summary>
/// <remarks>The <c>UNLOCK</c> request is described in section 9.11 of RFC 4918.</remarks>
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
  /// <remarks>This method attempts to unlock the canonical resource URL if <see cref="WebDAVContext.RequestResource"/> is not null, and
  /// to unlock the <see cref="WebDAVContext.RequestPath"/> otherwise.
  /// </remarks>
  public void ProcessStandardRequest()
  {
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null && precondition.StatusCode != (int)HttpStatusCode.NotModified)
    {
      Status = precondition;
      return;
    }

    ActiveLock lockObject = null;
    if(Context.LockManager != null) lockObject = Context.LockManager.GetLock(LockToken, Context.CanonicalPathIfKnown);

    if(lockObject == null) Status = ConditionCodes.LockTokenMatchesRequestUri409;
    else if(precondition != null) Status = precondition;
    else Context.LockManager.RemoveLock(lockObject);
  }
}

} // namespace AdamMil.WebDAV.Server
