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
using System.Collections.Generic;
using AdamMil.Utilities;

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

  /// <summary>Completes processing of a standard <c>DELETE</c> request for the request resource. This does not delete the resource, but
  /// does remove its properties and locks. This method is intended to be called after the deletion was successfully performed, but you do
  /// not need to call this method if you call <see cref="ProcessStandardRequest(Func{ConditionCode},bool)"/>, because it calls this method
  /// automatically.
  /// </summary>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass false if the request resource did
  /// not have children and true if it did have (or may have had) children.
  /// </param>
  public void PostProcessRequest(bool recursive)
  {
    PostProcessRequest(null, recursive);
  }

  /// <summary>Completes processing of a standard <c>DELETE</c> request for a resource. This does not delete the resource, but does remove
  /// its properties and locks. This method is intended to be called after the deletion was successfully performed, but you do not need to
  /// call this method if you call <see cref="o:AdamMil.WebDAV.Server.DeleteRequest.ProcessStandardRequest"/>, because it calls this method
  /// automatically.
  /// </summary>
  /// <param name="canonicalPath">The canonical, relative path to the resource that was deleted.</param>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass false if the request resource did
  /// not have children and true if it did have (or may have had) children.
  /// </param>
  public void PostProcessRequest(string canonicalPath, bool recursive)
  {
    if(canonicalPath == null)
    {
      if(Context.RequestResource == null) throw new ArgumentException("A path must be provided if there is no request resource.");
      canonicalPath = Context.RequestResource.CanonicalPath;
    }
    if(Context.PropertyStore != null) Context.PropertyStore.ClearProperties(canonicalPath, recursive);
    if(Context.LockManager != null)
    {
      Context.LockManager.RemoveLocks(canonicalPath, recursive ? LockRemoval.Recursive : LockRemoval.Nonrecursive);
    }
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a non-collection resource.</summary>
  /// <param name="delete">A function that should delete the request resource and return a <see cref="ConditionCode"/> indicating whether
  /// the attempt succeeded or failed, or null as the standard success code.
  /// </param>
  public void ProcessStandardRequest(Func<ConditionCode> delete)
  {
    ProcessStandardRequest(delete, false);
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a non-collection resource or a collection resource that can be
  /// atomically deleted.
  /// </summary>
  /// <param name="delete">A function that should recursively delete the request resource and return a <see cref="ConditionCode"/>
  /// indicating whether the attempt succeeded or failed, or null as the standard success code. If the resource is a collection, the
  /// function must be able to either delete the entire collection or none of it. If it's possible that some descendants within the
  /// collection may fail to be deleted, you must use the <see cref="ProcessStandardRequest{T}(T,Func{T,ConditionCode})"/> override
  /// instead.
  /// </param>
  /// <param name="recursive">False if the request resource did not have children or true if it did have (or may have had) children.</param>
  public void ProcessStandardRequest(Func<ConditionCode> delete, bool recursive)
  {
    if(delete == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      Status = DAVUtility.TryExecute(delete);
      if(Status == null || Status.IsSuccessful) PostProcessRequest(recursive);
    }
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a resource that might not be able to be deleted atomically.</summary>
  /// <typeparam name="T">The type of <paramref name="requestResource"/>, which must implement <see cref="IStandardResource{T}"/>.</typeparam>
  /// <param name="requestResource">The <see cref="IStandardResource{T}"/> object representing the request resource.</param>
  /// <param name="deleteResource">A function that should delete the request resource and return a <see cref="ConditionCode"/> indicating
  /// whether the attempt succeeded or failed, or null as the standard success code. At the time the function is called on a given
  /// resource, <paramref name="deleteResource"/> will have already been called successfully on all of its descendants, so, barring race
  /// conditions that create new resources during the deletion, the resource should have no children. If such race conditions are
  /// possible, you must decide how to handle them: either by deleting the resource recursively or failing with an error.
  /// </param>
  public void ProcessStandardRequest<T>(T requestResource, Func<T,ConditionCode> deleteResource) where T : IStandardResource<T>
  {
    if(requestResource == null || deleteResource == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null, requestResource.CanonicalPath);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      ProcessStandardRequest(requestResource, Context.RequestPath, deleteResource);
      // if only the request resource failed, report that in Status rather than a 207 Multi-Status response
      if(FailedMembers.Count == 1 && FailedMembers[0].RelativePath.OrdinalEquals(Context.RequestPath))
      {
        Status = FailedMembers[0].Status; // grab the reason for failure so we can report it
        FailedMembers.Clear(); // prevent a 207 Multi-Status response later
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and on descendant resources.</remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, true, true);
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

  bool ProcessStandardRequest<T>(T resource, string requestPath, Func<T, ConditionCode> delete) where T : IStandardResource<T>
  {
    bool failed = false; // keep track of whether any child failed to be deleted
    IEnumerable<T> children = resource.GetChildren(Context);
    if(children != null)
    {
      string pathBase = DAVUtility.WithTrailingSlash(requestPath);
      foreach(T child in children) failed |= ProcessStandardRequest(child, pathBase + child.GetMemberName(Context), delete);
    }
    if(!failed)
    {
      ConditionCode status = DAVUtility.TryExecute(delete, resource);
      if(status == null || status.IsSuccessful)
      {
        PostProcessRequest(resource.CanonicalPath, false);
      }
      else
      {
        FailedMembers.Add(Context.ServiceRoot, requestPath, status);
        failed = true;
      }
    }
    return failed;
  }
}

} // namespace AdamMil.WebDAV.Server
