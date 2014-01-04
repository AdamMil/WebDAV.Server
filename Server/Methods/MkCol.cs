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

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>MKCOL</c> request.</summary>
/// <remarks>The <c>MKCOL</c> request is described in section 9.3 of RFC 4918.</remarks>
public class MkColRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="MkColRequest"/> based on a new WebDAV request.</summary>
  public MkColRequest(WebDAVContext context) : base(context) { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources because a new
  /// collection is assumed to not contain any mapped member URLs.
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, true, false);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation sets the response status based on <see cref="WebDAVRequest.Status"/>, using
  /// <see cref="ConditionCodes.Created"/> if the status is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    Context.WriteStatusResponse(Status ?? ConditionCodes.Created);
  }
}

} // namespace AdamMil.WebDAV.Server
