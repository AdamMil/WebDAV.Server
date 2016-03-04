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
using System.Collections.Generic;
using System.Net;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>DELETE</c> request.</summary>
/// <remarks>
/// <para>The <c>DELETE</c> request is described in section 4.3.5 of RFC 7231 and section 9.6 of RFC 4918. To service a <c>DELETE</c>
/// request, you can normally just call the <see cref="ProcessStandardRequest{T}(T)"/> method or one of its overrides.
/// </para>
/// <para>If you would like to handle it yourself, you should recurse through the resources to be deleted, adding the specific resources
/// that failed to be deleted to the <see cref="FailedMembers"/> collection. (If the request failed completely, you should return the error
/// in <see cref="WebDAVRequest.Status"/> rather than <see cref="FailedMembers"/>.) For each resource deleted, you must also remove all
/// locks rooted at and all dead properties stored for that resource. (This can be done by calling the
/// <see cref="PostProcessDelete(string,bool)"/> method.) The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/></term>
///   <description>The request resource was successfully deleted, and a body is included in the response. There is no standard
///     body for successful <c>DELETE</c> requests, but you can define your own if the client can understand it.
///   </description>
/// </item>
/// <item>
///   <term>202 <see cref="ConditionCodes.Accepted"/></term>
///   <description>The request was processed, is valid, and will succeed (barring exceptional circumstances), but the execution of the
///     request has been deferred until later.
///   </description>
/// </item>
/// <item>
///   <term>204 <see cref="ConditionCodes.NoContent">No Content</see> (default)</term>
///   <description>The request resource was successfully deleted, and there is no body in the response. This is the default status code
///     returned when <see cref="WebDAVRequest.Status"/> is null.
///   </description>
/// </item>
/// <item>
///   <term>207 <see cref="ConditionCodes.MultiStatus">Multi-Status</see></term>
///   <description>This status code should be used along with a <c>DAV:multistatus</c> XML body when the request was partially processed
///     (i.e. when it did not completely fail). The XML body should only describe the specific resources for which deletion failed, and
///     should not include resources such as ancestors or descendants for which no deletion attempt was made. Such a response will
///     automatically be generated if items are added to <see cref="FailedMembers"/>. The error codes listed in this table may be used for
///     the resources in a 207 Multi-Status response, except 412 Precondition Failed, since preconditions should be checked before the
///     request is attempted.
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to delete the resource, or the server refuses to delete the resource for some
///     other reason.
///   </description>
/// </item>
/// <item>
///   <term>405 <see cref="ConditionCodes.MethodNotAllowed">Method Not Allowed</see></term>
///   <description>The request resource does not support deletion, for instance because it's read-only. If you return this status code,
///     then you must not include the <c>DELETE</c> method in responses to <c>OPTIONS</c> requests.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A conditional request was not executed because the condition wasn't true.</description>
/// </item>
/// <item>
///   <term>423 <see cref="ConditionCodes.Locked"/></term>
///   <description>The request resource was locked and no valid lock token was submitted. The <c>DAV:lock-token-submitted</c> precondition
///     code should be included in the response.
///   </description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="PostProcessDelete(string,bool)"/></term>
///   <description>You want to change what happens after a resource is deleted.</description>
/// </item>
/// <item>
///   <term><see cref="ProcessStandardRequest(Func{ConditionCode},string,bool)"/> and
///     <see cref="ProcessStandardRequest{T}(T,Func{T,ConditionCode})"/>
///   </term>
///   <description>You want to change the standard request processing.</description>
/// </item>
/// </list>
/// </remarks>
public class DeleteRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="DeleteRequest"/> based on a new WebDAV request.</summary>
  public DeleteRequest(WebDAVContext context) : base(context)
  {
    FailedMembers = new FailedResourceCollection();
  }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be deleted, if the resource is a collection resource.
  /// </summary>
  public FailedResourceCollection FailedMembers { get; private set; }

  /// <summary>Completes processing of a standard <c>DELETE</c> request for the request resource. This does not delete the resource, but
  /// does remove its properties and locks.
  /// </summary>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass false if the request resource did
  /// not have children and true if it did have (or may have had) children.
  /// </param>
  /// <remarks>This method is intended to be called after the deletion was successfully performed, but you do not need to call this method
  /// if you call <see cref="O:AdamMil.WebDAV.Server.DeleteRequest.ProcessStandardRequest">ProcessStandardRequest</see>, because it calls
  /// this method automatically.
  /// </remarks>
  public void PostProcessDelete(bool recursive)
  {
    PostProcessDelete(null, recursive);
  }

  /// <summary>Completes processing of a standard <c>DELETE</c> request for a resource. This does not delete the resource, but does remove
  /// its properties and locks.
  /// </summary>
  /// <param name="canonicalPath">The canonical, relative path to the resource that was deleted.</param>
  /// <param name="recursive">If true, locks and properties will be removed recursively. You should pass false if the request resource did
  /// not have children and true if it did have (or may have had) children.
  /// </param>
  /// <remarks>This method is intended to be called after the deletion was successfully performed, but you do not need to call this method
  /// if you call <see cref="O:AdamMil.WebDAV.Server.DeleteRequest.ProcessStandardRequest">ProcessStandardRequest</see>, because it calls
  /// this method automatically.
  /// </remarks>
  public virtual void PostProcessDelete(string canonicalPath, bool recursive)
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
  /// <param name="deleteResource">A function that should delete the request resource and return a <see cref="ConditionCode"/> indicating
  /// whether the attempt succeeded or failed, or null as the standard success code.
  /// </param>
  public void ProcessStandardRequest(Func<ConditionCode> deleteResource)
  {
    ProcessStandardRequest(deleteResource, false);
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a non-collection resource or a collection resource that can be
  /// atomically deleted.
  /// </summary>
  /// <param name="deleteResource">A function that should recursively delete the request resource and return a <see cref="ConditionCode"/>
  /// indicating whether the attempt succeeded or failed, or null as the standard success code. If the resource is a collection, the
  /// function must be able to either delete the entire collection or none of it. If it's possible that some descendants within the
  /// collection may fail to be deleted, you must use the <see cref="ProcessStandardRequest{T}(T,Func{T,ConditionCode})"/> override
  /// instead.
  /// </param>
  /// <param name="recursive">False if the request resource did not have children or true if it did have (or may have had) children.</param>
  public void ProcessStandardRequest(Func<ConditionCode> deleteResource, bool recursive)
  {
    ProcessStandardRequest(deleteResource, null, recursive);
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a non-collection resource or a collection resource that can be
  /// atomically deleted.
  /// </summary>
  /// <param name="deleteResource">A function that should recursively delete the request resource and return a <see cref="ConditionCode"/>
  /// indicating whether the attempt succeeded or failed, or null as the standard success code. If the resource is a collection, the
  /// function must be able to either delete the entire collection or none of it. If it's possible that some descendants within the
  /// collection may fail to be deleted, you must use the <see cref="ProcessStandardRequest{T}(T,Func{T,ConditionCode})"/> override
  /// instead.
  /// </param>
  /// <param name="canonicalPath">The canonical path to the request resource, or null to use the
  /// <see cref="IWebDAVResource.CanonicalPath"/> of <see cref="WebDAVContext.RequestResource"/>.
  /// </param>
  /// <param name="recursive">False if the request resource did not have children or true if it did have (or may have had) children.</param>
  public virtual void ProcessStandardRequest(Func<ConditionCode> deleteResource, string canonicalPath, bool recursive)
  {
    if(deleteResource == null) throw new ArgumentNullException();
    if(canonicalPath == null)
    {
      if(Context.RequestResource == null) throw new ArgumentException("A path must be provided if there is no request resource.");
      canonicalPath = Context.RequestResource.CanonicalPath;
    }
    ConditionCode precondition = CheckPreconditions(null, canonicalPath);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      Status = DAVUtility.TryExecute(deleteResource);
      if(DAVUtility.IsSuccess(Status)) PostProcessDelete(canonicalPath, recursive);
    }
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a resource that might not be able to be deleted atomically.</summary>
  /// <typeparam name="T">The type of <paramref name="requestResource"/>, which must implement <see cref="IStandardResource{T}"/>.</typeparam>
  /// <param name="requestResource">The <see cref="IStandardResource{T}"/> object representing the request resource.</param>
  public void ProcessStandardRequest<T>(T requestResource) where T : IStandardResource<T>
  {
    ProcessStandardRequest(requestResource, null);
  }

  /// <summary>Performs standard processing of a <c>DELETE</c> request on a resource that might not be able to be deleted atomically.</summary>
  /// <typeparam name="T">The type of <paramref name="requestResource"/>, which must implement <see cref="IStandardResource{T}"/>.</typeparam>
  /// <param name="requestResource">The <see cref="IStandardResource{T}"/> object representing the request resource.</param>
  /// <param name="deleteResource">A function that should delete the request resource and return a <see cref="ConditionCode"/> indicating
  /// whether the attempt succeeded or failed, or null as the standard success code. At the time the function is called on a given
  /// resource, <paramref name="deleteResource"/> will have already been called successfully on all of its descendants, so, barring race
  /// conditions that create new resources during the deletion, the resource should have no children. If such race conditions are
  /// possible, you must decide how to handle them: either by deleting the resource recursively or failing with an error. If this parameter
  /// is null, the <see cref="IStandardResource.Delete"/> method will be called on the resource.
  /// </param>
  public virtual void ProcessStandardRequest<T>(T requestResource, Func<T, ConditionCode> deleteResource) where T : IStandardResource<T>
  {
    if(requestResource == null || deleteResource == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null, requestResource.CanonicalPath);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      if(deleteResource == null) deleteResource = res => res.Delete();
      ProcessStandardRequest(requestResource, Context.RequestPath, deleteResource);
      if(FailedMembers.Count != 0) Status = ConditionCodes.MultiStatus; // let the caller know if we expect to reply with 207 Multi-Status
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks><include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensRemarks/remarks/node()" />
  /// <note type="inherit">This implementation checks <c>DAV:write</c> locks on the resource and on descendant resources.</note>
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, true, true);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    string value = Context.Request.Headers[DAVHeaders.Depth];
    if(!string.IsNullOrEmpty(value) && !"infinity".OrdinalEquals(value))
    {
      Status = new ConditionCode(HttpStatusCode.BadRequest, "The Depth header must be infinity or unspecified for DELETE requests.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks><note type="inherit">This implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty,
  /// and outputs a response based on <see cref="WebDAVRequest.Status"/> otherwise.
  /// </note></remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    else Context.WriteFailedMembers(FailedMembers);
  }

  bool ProcessStandardRequest<T>(T resource, string requestPath, Func<T, ConditionCode> delete) where T : IStandardResource<T>
  {
    if(Context.ShouldDenyAccess(resource, DAVNames.write)) // the request resource should have already been checked
    {
      FailedMembers.Add(Context.ServiceRoot, requestPath, ConditionCodes.Forbidden);
      return false;
    }

    IEnumerable<T> children = resource.GetChildren(Context);
    bool failed = false, hadChild = false; // keep track of whether any child failed to be deleted and we had any children
    if(children != null)
    {
      string pathBase = DAVUtility.WithTrailingSlash(requestPath);
      foreach(T child in children)
      {
        failed  |= ProcessStandardRequest(child, pathBase + child.GetMemberName(Context), delete);
        hadChild = true;
      }
    }
    if(!failed)
    {
      ConditionCode status = DAVUtility.TryExecute(delete, resource);
      if(DAVUtility.IsSuccess(status))
      {
        PostProcessDelete(resource.CanonicalPath, false);
      }
      else
      {
        // if the request failed entirely (i.e. nothing was deleted), report the error in Status
        if(!hadChild && FailedMembers.Count == 0 && requestPath.OrdinalEquals(Context.RequestPath)) Status = status;
        else FailedMembers.Add(Context.ServiceRoot, requestPath, status);
        failed = true;
      }
    }
    return failed;
  }
}

} // namespace AdamMil.WebDAV.Server
