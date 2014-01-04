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

/// <summary>Represents a <c>DELETE</c> request.</summary>
/// <remarks>The <c>DELETE</c> request is described in section 9.6 of RFC 4918.</remarks>
public class DeleteRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="DeleteRequest"/> based on a new WebDAV request.</summary>
  public DeleteRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // see ParseRequest() for details
    FailedMembers = new FailedResourceCollection();
  }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be deleted, if the resource is a collection resource.
  /// </summary>
  public FailedResourceCollection FailedMembers { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and on descendant resources.</remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, true, true);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // require recursive DELETE requests, as per section 9.6.1 of RFC 4918
    if(Depth != Depth.SelfAndDescendants)
    {
      throw Exceptions.BadRequest("The Depth header must be infinity or unspecified for DELETE requests.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty, and outputs a
  /// response based on <see cref="WebDAVRequest.Status"/> otherwise.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    else Context.WriteFailedMembers(FailedMembers);
  }
}

} // namespace AdamMil.WebDAV.Server
