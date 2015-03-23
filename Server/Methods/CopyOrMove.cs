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
using System.Net;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>COPY</c> or <c>MOVE</c> request.</summary>
/// <remarks>The <c>COPY</c> and <c>MOVE</c> requests are described in sections 9.8 and 9.9 of RFC 4918.</remarks>
public class CopyOrMoveRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="CopyOrMoveRequest"/> based on a new WebDAV request.</summary>
  public CopyOrMoveRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // RFC 4918 sections 9.8.3 and 9.9.3 require recursion as the default

    // check the method
    if(context.Request.HttpMethod.OrdinalEquals(DAVMethods.Move)) IsMove = true;
    else if(context.Request.HttpMethod.OrdinalEquals(DAVMethods.Copy)) IsCopy = true;
    else throw new InvalidOperationException("This request object may only be used for COPY or MOVE requests.");

    // parse the Overwrite header
    string value = context.Request.Headers[DAVHeaders.Overwrite] ?? "";
    char c = value == null || value.Length != 1 ? '\0' : char.ToUpperInvariant(value[0]);
    if(string.IsNullOrEmpty(value)) Overwrite = true; // if it's missing, it must be treated as true, according to RFC 4918 section 10.6
    else if(c == 'F' || c == 'T') Overwrite = value[0] == 'T';
    else throw Exceptions.BadRequest("Invalid Overwrite header: " + value);

    // parse the Destination header
    value = context.Request.Headers[DAVHeaders.Destination];
    if(string.IsNullOrEmpty(value)) throw Exceptions.BadRequest("The Destination header was missing.");
    Uri destination;
    if(!DAVUtility.TryParseSimpleRef(value, out destination))
    {
      throw Exceptions.BadRequest("The Destination header was not a valid absolute URI or absolute path.");
    }

    // resolve the destination URL to see which service it's under (if any), and see if we can resolve it to a specific resource there
    UriResolution info = WebDAVModule.ResolveUri(context, destination, true);
    IWebDAVService destService = info.Service;
    if(destService != null && info.ServiceRoot.OrdinalEquals(context.ServiceRoot)) destService = context.Service; // normalize destService
    string canonicalDestPath = info.Resource != null ? info.Resource.CanonicalPath : // get the canonical path to the destination
                               destService != null   ? destService.GetCanonicalPath(context, info.RelativePath) : null;
    Destination   = new DestinationInfo(context, destination, info, destService, canonicalDestPath);
    FailedMembers = new FailedResourceCollection();
  }

  /// <summary>Gets a <see cref="DestinationInfo"/> object describing the destination where the request resource should be copied or moved.</summary>
  public DestinationInfo Destination { get; private set; }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be copied or moved, if the source resource is a collection resource.
  /// </summary>
  public FailedResourceCollection FailedMembers { get; private set; }

  /// <summary>Gets whether the request is a <c>COPY</c> request.</summary>
  /// <remarks><c>COPY</c> requests must be processed in accordance with RFC 4918 section 9.8.</remarks>
  public bool IsCopy { get; private set; }

  /// <summary>Gets whether the request is a <c>MOVE</c> request.</summary>
  /// <remarks><c>MOVE</c> requests must be processed in accordance with RFC 4918 section 9.9.</remarks>
  public bool IsMove { get; private set; }

  /// <summary>Gets whether overwriting mapped resources is allowed. If true, resources at the destination should be overwritten. (During
  /// a move, they must be overwritten as if they had been deleted first. In particular, all properties must be reset.) If false, 
  /// existing resources at the destination must not be overwritten. (412 Precondition Failed responses should be returned for those
  /// resources.)
  /// </summary>
  public bool Overwrite { get; private set; }

  #region DestinationInfo
  /// <summary>Provides information about the destination of a <c>COPY</c> or <c>MOVE</c> request.</summary>
  public sealed class DestinationInfo
  {
    internal DestinationInfo(WebDAVContext context, Uri uri, UriResolution uriInfo, IWebDAVService service, string canonicalPath)
    {
      this.context  = context;
      AccessDenied  = uriInfo.AccessDenied;
      CanonicalPath = canonicalPath;
      LockManager   = uriInfo.LockManager;
      PropertyStore = uriInfo.PropertyStore;
      RequestPath   = uriInfo.RelativePath;
      Resource      = uriInfo.Resource;
      Service       = service;
      ServiceRoot   = uriInfo.ServiceRoot;
      Uri           = uri;
      authFilters   = uriInfo.AuthorizationFilters;
    }

    /// <summary>Gets whether access to the destination resource was in general denied to the user. If false, the copy or move should fail
    /// with a <see cref="ConditionCodes.Forbidden"/> status on the destination URL.
    /// (<see cref="ProcessStandardRequest{T}(T, Func{T,ConditionCode}, Func{CopyOrMoveRequest,string,T,ConditionCode}, Func{T,IEnumerable{T}})"/>
    /// will do this for you.) Even if true, this does not imply that the user has access to create or overwrite the destination resource or
    /// any descendant resources. That must be checked separately. You may check whether the user is denied access to a descendant resource
    /// using <see cref="ShouldDenyAccess"/>. Once again, this only checks access in general. It does not check for the right to modify the
    /// resource in particular.
    /// </summary>
    public bool AccessDenied { get; private set; }

    /// <summary>Gets the canonical path to the destination within the <see cref="Service"/>, relative to the <see cref="ServiceRoot"/>, if
    /// known, or null if the canonical path is not known.
    /// </summary>
    public string CanonicalPath { get; private set; }

    /// <summary>Gets the <see cref="CanonicalPath"/> if it's not null, or the <see cref="RequestPath"/>, which may also be null,
    /// otherwise.
    /// </summary>
    public string CanonicalPathIfKnown
    {
      get { return CanonicalPath ?? RequestPath; }
    }

    /// <summary>Get the ID of the user making the current request, according to the destination <see cref="Service"/>, or null if the user
    /// is unknown or anonymous or if the <see cref="Service"/> could not be resolved.
    /// </summary>
    public string CurrentUserId
    {
      get
      {
        if(_currentUserId == null)
        {
          string userId = null;
          if(Service != null)
          {
            foreach(IAuthorizationFilter filter in authFilters)
            {
              if(filter.GetCurrentUserId(context, out userId))
              {
                userId = userId ?? "";
                break;
              }
            }
            if(userId == null) userId = Service.GetCurrentUserId(context);
          }
          _currentUserId = userId ?? "";
        }

        return StringUtility.MakeNullIfEmpty(_currentUserId);
      }
    }

    /// <summary>Gets the <see cref="ILockManager"/> responsible for the <see cref="Service"/>, if could be resolved and supports
    /// locking.
    /// </summary>
    public ILockManager LockManager { get; private set; }

    /// <summary>Gets the <see cref="IPropertyStore"/> responsible for the <see cref="Service"/>, if could be resolved and
    /// supports dead properties.
    /// </summary>
    public IPropertyStore PropertyStore { get; private set; }

    /// <summary>Gets the requested destination path, relative to <see cref="ServiceRoot"/>, if the <see cref="Uri"/> could be resolved to
    /// a service within the WebDAV server (i.e. if <see cref="Service"/> is not null), or null if it could not be resolved.
    /// </summary>
    public string RequestPath { get; private set; }

    /// <summary>Gets the destination resource, if the <see cref="RequestPath"/> could be resolved to a specific resource within the
    /// destination <see cref="Service"/>. Access checks may not have been performed against the destination resource, so you should
    /// perform them yourself. If null, <see cref="RequestPath"/> could not be resolved to any existing resource. This is a common case as
    /// the destination usually does not exist before the request is made. Even if this property is null, <see cref="Service"/> may not be
    /// null.
    /// </summary>
    public IWebDAVResource Resource { get; private set; }

    /// <summary>Gets the destination service, if the <see cref="Uri"/> could be resolved to a specific service within the WebDAV server.
    /// If null, the <see cref="Uri"/> does not correspond to any WebDAV service within the server.
    /// </summary>
    /// <remarks><note type="caution">
    /// <para>Note that in rare cases this object may refer to the same location on the web server as <see cref="WebDAVContext.Service"/>
    /// even if it's not identical to it. This may happen if the request supplied a <c>Destination</c> header that specified the
    /// destination using a different base URI (such as <c>http://host/</c> versus <c>https://host/</c> if the WebDAV service handles
    /// both), and a server error occured on another thread that caused the cached service object to be discarded before the destination
    /// service could be resolved. The <see cref="IWebDAVResource"/> that processes this request is responsible for determining whether the
    /// request represents an inter- or intra-service operation. (For example, a <see cref="Services.FileSystemResource"/> knows how to
    /// check whether both services are <see cref="Services.FileSystemService"/>s, and if so, whether they are serving the same root
    /// directory.)
    /// </para>
    /// <para>It is also possible that even if the destination refers to a different location than the source in URL space, it can refer
    /// to overlapping locations in the underlying data store. For example, two WebDAV services at /root and /users could point to
    /// overlapping parts of the same filesystem (e.g. C:\ and C:\Users). The <see cref="IWebDAVResource"/> that processes this request is
    /// responsible for making sure that it either disallows copies/moves from the request resource to a descendant of the request resource
    /// (such as copying or moving C:\Foo to C:\Foo\Bar\Baz), or that it can handle such operations correctly. (Copies to a descendant are
    /// possible, but care must be taken to avoid infinite recursion; moves to a descendant are impossible, because they would result in an
    /// inconsistent URL namespace.)
    /// </para>
    /// </note></remarks>
    public IWebDAVService Service { get; private set; }

    /// <summary>Gets the root of the <see cref="Service"/>, if the <see cref="Uri"/> could be resolved to a specific service within the
    /// WebDAV server. If the <see cref="System.Uri.Scheme"/> or <see cref="System.Uri.Authority"/> of the destination URI is different
    /// from those of the request URI, this will be an absolute URI (e.g. <c>http://othersite/otherRoot/</c>). If the <see cref="Uri"/>
    /// could not be resolved to a service, this will be null.
    /// </summary>
    public string ServiceRoot { get; private set; }

    /// <summary>Gets the absolute URI parsed from the <c>Destination</c> header submitted by the client. The URI may point to a location
    /// outside the <see cref="WebDAVContext.Service">request service root</see>, and may even point a location on another server. If you
    /// only support copies and moves within the same WebDAV server, then it is easier to use <see cref="Service"/> and
    /// <see cref="RequestPath"/> instead.
    /// </summary>
    public Uri Uri { get; private set; }

    /// <summary>Determines whether access should be denied to the destination resource named by the given path. If this method returns
    /// true, an attempt to create or overwrite the path should fail with <see cref="ConditionCodes.Forbidden"/>. This method only checks
    /// whether the user is denied access in general, so even if it returns false the user may still not have the specific right to modify
    /// or overwrite the resource.
    /// </summary>
    public bool ShouldDenyAccess(string destPath)
    {
      if(Service != null)
      {
        IWebDAVResource resource = Service.ResolveResource(context, destPath);
        bool denyExistence;
        foreach(IAuthorizationFilter filter in authFilters)
        {
          if(filter.ShouldDenyAccess(context, Service, resource, out denyExistence)) return true;
        }
        return resource != null && resource.ShouldDenyAccess(context, Service, out denyExistence);
      }
      return false;
    }

    readonly WebDAVContext context;
    readonly IEnumerable<IAuthorizationFilter> authFilters;
    string _currentUserId;
  }
  #endregion

  /// <summary>Recursively removes all locks on the destination path and copies all dead properties from the source path to the
  /// destination path, removing all previously existing properties at the destination. This method is expected to be
  /// called from <see cref="IWebDAVService.CopyResource"/> after the copy has been successfully made.
  /// </summary>
  public virtual void PostProcessCopy(string canonicalSourcePath, string canonicalDestPath)
  {
    // remove any locks that existed at the destination and copy over the dead properties
    if(Destination.LockManager != null) Destination.LockManager.RemoveLocks(canonicalDestPath, LockRemoval.Recursive);
    if(Context.PropertyStore != null && Destination.PropertyStore != null)
    {
      IEnumerable<XmlProperty> properties = Context.PropertyStore.GetProperties(canonicalSourcePath).Values;
      Destination.PropertyStore.SetProperties(canonicalDestPath, properties, true);
    }
  }

  /// <summary>Implements standard processing for a <c>COPY</c> request. This method is suitable for read-only resources from services that
  /// don't support specialized handling of intra-service copies or moves.
  /// </summary>
  public void ProcessStandardRequest<T>(T requestResource) where T : IStandardResource<T>
  {
    if(IsMove) Status = ConditionCodes.Forbidden; // this method is only suitable for read-only resources, so moves aren't allowed
    else ProcessStandardRequest(requestResource, null, null, null);
  }

  /// <include file="documentation.xml" path="/DAV/CopyOrMoveRequest/ProcessStandardRequest/*[@name != 'createDest' and @name != 'getChildren']" />
  public void ProcessStandardRequest<T>(T requestResource, Func<T, ConditionCode> deleteSource) where T : IStandardResource<T>
  {
    ProcessStandardRequest(requestResource, deleteSource, null, null);
  }

  /// <include file="documentation.xml" path="/DAV/CopyOrMoveRequest/ProcessStandardRequest/node()" />
  public virtual void ProcessStandardRequest<T>(T requestResource, Func<T, ConditionCode> deleteSource,
                                                Func<CopyOrMoveRequest,string,T,ConditionCode> createDest,
                                                Func<T,IEnumerable<T>> getChildren) where T : IStandardResource<T>
  {
    if(requestResource == null || IsMove && deleteSource == null) throw new ArgumentNullException();

    if(IsMove && Depth != Depth.SelfAndDescendants && requestResource.IsCollection)
    {
      Status = new ConditionCode((int)HttpStatusCode.Forbidden, "The Depth header must be infinity or unspecified for " +
                                 "MOVE requests submitted to a collection resource.");
      return;
    }

    // return precondition errors before doing anything
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null && precondition.IsError)
    {
      Status = precondition;
      return;
    }

    // check for obvious copying of a resource to itself or its own descendant or ancestor
    if(Context.Service == Destination.Service)
    {
      string source = DAVUtility.WithTrailingSlash(requestResource.CanonicalPath);
      string dest   = DAVUtility.WithTrailingSlash(Destination.CanonicalPathIfKnown);
      if(source.OrdinalEquals(dest)) // if copying to the same location, make it a no-op. RFC 4918 suggests 403 Forbidden,
      {                              // but I don't see the harm in allowing (and ignoring) such a request
        // non-error preconditions take precedence over success codes
        Status = !Overwrite ? ConditionCodes.PreconditionFailed : precondition ?? ConditionCodes.NoContent;
        return;
      }
      if(dest.Length > source.Length ? dest.StartsWith(source, StringComparison.Ordinal) : // if copying/moving to a descendant or
                                       source.StartsWith(dest, StringComparison.Ordinal))  // ancestor...
      {
        Status = ConditionCodes.BadCopyOrMovePath;
        return;
      }
    }

    if(createDest == null) // if no remoteCreate function was given, try to use Destination.Service.CopyResource
    {
      if(Destination.Service == null) // if that doesn't exist, give up
      {
        Status = ConditionCodes.BadGateway; // 502 Bad Gateway is used when we don't understand how to copy or move to the destination
        return;
      }
      createDest = (request,path,resource) => Destination.Service.CopyResource(this, path, resource);
    }

    // now return non-error preconditions before doing the operation
    if(precondition != null)
    {
      Status = precondition;
      return;
    }

    if(getChildren == null) getChildren = resource => resource.GetChildren(Context);

    // do the copy/move
    bool success = false;
    if(Destination.AccessDenied)
    {
      FailedMembers.Add(Destination.ServiceRoot, Destination.RequestPath, ConditionCodes.Forbidden);
    }
    else
    {
      string destServiceRoot = Destination.ServiceRoot, destRequestPath = Destination.RequestPath;
      if(Destination.Service == null) // if the destination service couldn't be resolved, construct the root and paths from the URI
      {
        destServiceRoot = DAVUtility.WithTrailingSlash(Destination.Uri.GetLeftPart(UriPartial.Authority));
        destRequestPath = DAVUtility.UriPathPartialDecode(Destination.Uri.AbsolutePath).TrimStart('/');
      }
      success = ProcessStandardRequest(requestResource, Context.RequestPath, destServiceRoot, destRequestPath,
                                       deleteSource, createDest, getChildren);
    }

    // if a resource failed, but the status was successful, choose an appropriate status code
    if(!success && (Status == null || Status.IsSuccessful))
    {
      // at least one resource should have failed. if only the request resource failed (or only the Destination resource failed but the
      // error can be understood without giving the destination URL), report that in Status rather than a 207 Multi-Status response
      if(FailedMembers.Count == 0) throw new ContractViolationException("A resource failed but was not reported in FailedMembers.");
      if(FailedMembers.Count == 1 &&
         // during a move, 423 Locked could apply to the source or destination, so give the specific URL unless the status includes it
         (IsCopy || FailedMembers[0].Status.StatusCode != 423 || FailedMembers[0].Status is LockTokenSubmittedConditionCode) &&
         (FailedMembers[0].RelativePath.OrdinalEquals(Context.RequestPath) &&
          FailedMembers[0].ServiceRoot.OrdinalEquals(Context.ServiceRoot) ||
          FailedMembers[0].RelativePath.OrdinalEquals(Destination.RequestPath) &&
          FailedMembers[0].ServiceRoot.OrdinalEquals(Destination.ServiceRoot)))
      {
        Status = FailedMembers[0].Status; // grab the reason for failure so we can report it
        FailedMembers.Clear(); // prevent a 207 Multi-Status response later
      }
      else // otherwise, multiple resources failed or the failure was not about the request or Destination URI...
      {
        Status = ConditionCodes.MultiStatus; // report it with a 207 Multi-Status response
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    // check the source if we're moving it, as well as the destination
    ConditionCode code = IsMove ? CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, true, Depth != Depth.Self) : null;
    if(code == null && Destination.Service != null)
    {
      code = CheckSubmittedLockTokens(LockType.ExclusiveWrite, Destination.CanonicalPathIfKnown, true, true,
                                      Destination.ServiceRoot, Destination.LockManager);
    }
    return code;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/FilterSubmittedLockToken/node()" />
  protected override bool FilterSubmittedLockToken(string lockToken)
  {
    ActiveLock lockObject;
    if(IsMove)
    {
      lockObject = Context.LockManager.GetLock(lockToken, null);
      if(lockObject != null) return Context.CurrentUserId.OrdinalEquals(lockObject.OwnerId);
    }
    if(Destination.LockManager != null)
    {
      lockObject = Destination.LockManager.GetLock(lockToken, null);
      if(lockObject != null) return Destination.CurrentUserId.OrdinalEquals(lockObject.OwnerId);
    }
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // SelfAndChildren doesn't seem useful, and RFC 4918 sections 9.8 and 9.9 don't describe any behavior for it, so disallow it.
    // MOVE requests are also not allowed to submit Depth.Self for collection resources, but since we don't know whether the resource is
    // a collection yet, we can't check that here
    if(Depth == Depth.SelfAndChildren)
    {
      throw Exceptions.BadRequest("The Depth header must be 0 or infinity for COPY or MOVE requests, or unspecified.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty, and outputs a
  /// response based on <see cref="WebDAVRequest.Status"/> otherwise, using 204 No Content if <see cref="WebDAVRequest.Status"/> is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.Created);
    else Context.WriteFailedMembers(FailedMembers);
  }

  bool ProcessStandardRequest<T>(T resource, string requestPath, string destServiceRoot, string destRequestPath,
                                 Func<T,ConditionCode> deleteSource, Func<CopyOrMoveRequest,string,T,ConditionCode> createDest,
                                 Func<T,IEnumerable<T>> getChildren)
    where T : IStandardResource<T>
  {
    // copy over the resource (non-recursively)
    ConditionCode status = Destination.ShouldDenyAccess(destRequestPath) ?
      ConditionCodes.Forbidden : DAVUtility.TryExecute(createDest, this, destRequestPath, resource);
    if(status != null)
    {
      if(!status.IsSuccessful)
      {
        FailedMembers.Add(destServiceRoot, destRequestPath, status);
        return false;
      }
      else if(Overwrite && status.StatusCode == 204) // if a resource at the destination is reported to have been overwritten...
      {
        if(Status == null || Status.IsSuccessful) Status = status; // and we don't already have an error message, use that status
      }
    }

    // copy/move any descendant resources, recursively, if it's a recursive request
    bool success = true;
    if(Depth == Server.Depth.SelfAndDescendants && resource.IsCollection)
    {
      IEnumerable<T> children = getChildren(resource);
      if(children != null)
      {
        string requestBase = DAVUtility.WithTrailingSlash(requestPath), destRequestBase = DAVUtility.WithTrailingSlash(destRequestPath);
        foreach(T child in children)
        {
          string name = child.GetMemberName(Context);
          success &= ProcessStandardRequest(child, requestBase + name, destServiceRoot, destRequestBase + name,
                                            deleteSource, createDest, getChildren);
        }
      }
    }

    // now delete the source if it's a move
    if(success && IsMove)
    {
      status = DAVUtility.TryExecute(deleteSource, resource);
      if(status == null || status.IsSuccessful)
      {
        if(Context.LockManager != null) Context.LockManager.RemoveLocks(resource.CanonicalPath, LockRemoval.Nonrecursive);
        if(Context.PropertyStore != null) Context.PropertyStore.ClearProperties(resource.CanonicalPath, false);
      }
      else
      {
        FailedMembers.Add(Context.ServiceRoot, requestPath, status);
        success = false;
      }
    }

    return success;
  }
}

} // namespace AdamMil.WebDAV.Server
