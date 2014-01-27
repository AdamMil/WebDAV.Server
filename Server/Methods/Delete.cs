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

  /// <summary>Completes processing of a standard <c>DELETE</c> request for an existing resource. This does not delete the resource, but
  /// does remove the resource's properties and locks. This method is intended to be called after the deletion was successfully performed.
  /// </summary>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass true if the resource is a
  /// directory and false if not.
  /// </param>
  public void PostProcessRequest(bool recursive)
  {
    PostProcessRequest(null, recursive);
  }

  /// <summary>Completes processing of a standard <c>DELETE</c> request for an existing resource. This does not delete the resource, but
  /// does remove the resource's properties and locks. This method is intended to be called after the deletion was successfully performed.
  /// </summary>
  /// <param name="absolutePath">The absolute, canonical path to the resource that was deleted.</param>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass true if the resource is a
  /// directory and false if not.
  /// </param>
  public void PostProcessRequest(string absolutePath, bool recursive)
  {
    if(absolutePath == null)
    {
      if(Context.RequestResource == null) throw new ArgumentException("A path must be provided if there is no request resource.");
      absolutePath = Context.ServiceRoot + Context.RequestResource.CanonicalPath;
    }
    if(Context.PropertyStore != null) Context.PropertyStore.ClearProperties(absolutePath, recursive);
    if(Context.LockManager != null)
    {
      Context.LockManager.RemoveLocks(absolutePath, recursive ? LockRemoval.Recursive : LockRemoval.Nonrecursive);
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and on descendant resources.</remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, true, true);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    if(Depth != Depth.SelfAndDescendants) // require recursive DELETE requests, as per section 9.6.1 of RFC 4918
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
