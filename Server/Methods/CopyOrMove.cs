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
using System.IO;
using System.Net;
using System.Xml;
using AdamMil.Utilities;

// TODO: add processing examples and documentation

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
    /// (<see cref="ProcessStandardRequest{T,I}(T, Func{T,string,I}, Func{T,string,ConditionCode}, Func{CopyOrMoveRequest,string,I,ConditionCode})"/>
    /// will do this for you.) Even if true, this does not imply that the user has access to create or overwrite the destination resource or any
    /// descendant resources. That must be checked separately.
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
    /// request represents an inter- or intra-service operation. (For example, a <see cref="FileSystemResource"/> knows how to check
    /// whether both services are <see cref="FileSystemService"/>s, and if so, whether they are serving the same root directory.)
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

    readonly WebDAVContext context;
    readonly IEnumerable<IAuthorizationFilter> authFilters;
    string _currentUserId;
  }
  #endregion

  #region ISourceResource
  /// <summary>Represents a resource being processed by a WebDAV <c>COPY</c> or <c>MOVE</c> request.</summary>
  public interface ISourceResource
  {
    /// <summary>Gets the canonical path to the resource in the same form as that returned by <see cref="IWebDAVResource.CanonicalPath"/>.</summary>
    string CanonicalPath { get; }

    /// <summary>Gets whether this resource is a collection resource, which may contain child resources.</summary>
    bool IsCollection { get; }

    /// <summary>Gets the <see cref="EntityMetadata"/> about the resource's entity body. In general, the metadata does not require an
    /// <see cref="EntityMetadata.EntityTag"/>, so no significant effort should be made to compute one.
    /// </summary>
    EntityMetadata Metadata { get; }

    /// <summary>Returns a dictionary containing the live properties of the resource. The property values should be in the same form as
    /// those given to a <see cref="PropFindRequest"/>, including the ability to include <c>Func&lt;object&gt;</c> delegates to compute
    /// expensive property values on demand. (See
    /// <see cref="PropFindRequest.ProcessStandardRequest(IDictionary{XmlQualifiedName, object})"/> for details.)
    /// </summary>
    /// <remarks>Generally, live properties need not be returned if they are would not be respected by the destination service. For
    /// example, the <c>DAV:lockdiscovery</c>, <c>DAV:supportedlock</c>, and <c>DAV:getetag</c> properties are determined entirely by the
    /// destination resource, and should not be returned (although returning them isn't illegal).
    /// </remarks>
    IDictionary<XmlQualifiedName, object> GetLiveProperties(WebDAVContext context);

    /// <summary>Returns the name of the resource as a minimally escaped path segment that maps to the resource within its parent
    /// collection.
    /// </summary>
    /// <remarks>If a resource has multiple valid names within a collection (e.g. on a case-insensitive service), the name that would be
    /// preferred by humans should be returned, even if it differs from the canonical name used by the service. The name may also differ
    /// depending on the request URI. For example, if a service exposes files in two collections and this resource is mapped to paths
    /// <c>filesById/173</c> and <c>filesByName/hello.txt</c> the name returned would depend on whether the request URL referred to
    /// <c>filesById/</c> or <c>filesByName/</c>. (The name would be "173" in the former case and "hello.txt" in the latter case.) If the
    /// resource is at the root of a DAV service and thus has no name, an empty string must be returned. Otherwise, if the resource is a
    /// child collection, the name should end with a trailing slash.
    /// </remarks>
    string GetMemberName(WebDAVContext context);

    /// <summary>Returns a stream containing the resource's data. This is usually but not necessarily the body that would be returned
    /// from a GET request. For instance, a collection resource might respond with an HTML list of member resources in response to a GET
    /// request, but this HTML listing should not be returned from this method. Instead, if the resource logically has no data stream,
    /// like most collections, this method should return null.
    /// </summary>
    /// <remarks>The stream does not need to be seekable, but a seekable stream is preferred if one can be cheaply obtained. The stream
    /// must be closed by the caller when no longer needed.
    /// </remarks>
    Stream OpenStream(WebDAVContext context);
  }
  #endregion

  #region ISourceResource<T>
  /// <summary>Represents a resource being processed by a WebDAV <c>COPY</c> or <c>MOVE</c> request, and provides a way to obtain its
  /// descendants.
  /// </summary>
  /// <typeparam name="T">The type of object internally used by the <see cref="IWebDAVResource"/> to represent this resource. This type
  /// parameter is provided for the convenience of the <see cref="IWebDAVResource"/>, to avoid type casts from <see cref="ISourceResource"/>
  /// back to the internal type.
  /// </typeparam>
  public interface ISourceResource<T> : ISourceResource // it's split up so IWebDAVService.CopyResource doesn't have to be a generic method
  {
    /// <summary>Returns the children of this resource if it's a collection resource, or null if it's a non-collection resource.</summary>
    IEnumerable<T> GetChildren();
  }
  #endregion

  /// <summary>Implements standard processing for a <c>COPY</c> request. This method is suitable for read-only resources from services that
  /// don't support specialized handling of intra-service copies or moves.
  /// </summary>
  public void ProcessStandardRequest<T>(T requestResource) where T : ISourceResource<T>
  {
    ProcessStandardRequest(requestResource, (resource,path) => resource);
  }

  /// <summary>Implements standard processing for a <c>COPY</c> request. This method is suitable for read-only resources from services that
  /// don't support specialized handling of intra-service copies or moves.
  /// </summary>
  /// <param name="requestResource">The object representing the <see cref="WebDAVContext.RequestResource"/>, which is to be copied or
  /// moved.
  /// </param>
  /// <param name="getInfo">A function that accepts a source resource (of type <typeparamref name="T"/>) and the canonical path to it, and
  /// returns data about the resource (of type <see cref="ISourceResource{T}"/> of <typeparamref name="T"/>).
  /// </param>
  public void ProcessStandardRequest<T>(T requestResource, Func<T, string, ISourceResource<T>> getInfo)
  {
    if(IsMove) Status = ConditionCodes.Forbidden; // this method is only suitable for read-only resources, so moves aren't allowed
    else ProcessStandardRequest(requestResource, getInfo, null, null);
  }

  /// <summary>Implements standard processing for a <c>COPY</c> or <c>MOVE</c> request.</summary>
  /// <typeparam name="T">The type of object used internally by the caller to represent its resources. This type parameter is provided for
  /// the convenience of the caller, to avoid needing to cast back to the internal type in callback methods that accept resources.
  /// </typeparam>
  /// <typeparam name="TInfo">The type of object used by the caller to represent information about its resources. This may be identical
  /// to <typeparamref name="T"/> if the resource type provides its own metadata. This type must implement <see cref="ISourceResource{T}"/>
  /// of <typeparamref name="T"/>.
  /// </typeparam>
  /// <param name="requestResource">The object representing the <see cref="WebDAVContext.RequestResource"/>, which is to be copied or
  /// moved.
  /// </param>
  /// <param name="getInfo">A function that accepts a source resource (of type <typeparamref name="T"/>) and the canonical path to it, and
  /// returns data about the resource (of type <typeparamref name="TInfo"/>).
  /// </param>
  /// <param name="deleteSource">A function that accepts a source resource (of type <typeparamref name="T"/>) and the canonical path to it,
  /// and deletes the source resource if the user has access to do so. This parameter is only used for <c>MOVE</c> operations, and may be
  /// null for <c>COPY</c> operations. The deletion must fail with <see cref="ConditionCodes.Forbidden"/> if the user does not have
  /// permission to delete the given resource. The result of the operation should be returned as a <see cref="ConditionCode"/> as described
  /// by RFC 4918 section 9.9. This function is called only if <paramref name="createDest"/> succeeds, to delete the source resource and
  /// complete the move. The function does not need to delete the dead properties or locks of the source resource, as that will be done
  /// automatically if the function returns a successful status code (or null, which is assumed to be success). If
  /// <paramref name="createDest"/> is clever enough to do an actual move  of the source resource rather than creating a copy, then it is
  /// acceptable for this method to be a no-op (simply returning a success code).
  /// </param>
  /// <param name="createDest">A function that has the same signature as <see cref="IWebDAVService.CopyResource"/> except that the path
  /// will be an absolute URL if the <see cref="DestinationInfo.Service"/> was not known, and the resource info will be of type
  /// <typeparamref name="TInfo"/> rather than the generic type <see cref="ISourceResource"/>. The function must perform a copy or move of
  /// the source resource to the destination path if the user has access to write to the destination, and return a
  /// <see cref="ConditionCode"/> as described by sections 9.8 and 9.9 of RFC 4918.
  /// <para>
  /// When servicing a <c>COPY</c> request, the function must attempt to create a copy of the source resource. When servicing a
  /// <c>MOVE</c> request, the function may create a copy (after which <paramref name="deleteSource"/> is responsible for deleting the
  /// source) or may directly move the source to the destination (in which case <paramref name="deleteSource"/> should be a no-op).
  /// Dead properties must be copied or moved as well, along with selected live properties. (See <see cref="IWebDAVService.CopyResource"/>
  /// for additional details. Locks are not transferred.)
  /// Normally these copies and moves are done non-recursively, relying on this method to use <see cref="ISourceResource{T}.GetChildren"/>
  /// to recursively copy or move the descendants. (A non-recursive "move" can be usually done by creating the destination object with the
  /// same attributes that it would have if it was moved, such as the same creation date, but without any children.) However, as an
  /// optimization you may copy or move items recursively when <see cref="Depth"/> is <see cref="Depth.SelfAndDescendants"/>.
  /// In that case, <see cref="ISourceResource{T}.GetChildren"/> must not return any children, or else this method will attempt to
  /// recursively invoke <paramref name="createDest"/> on them even though they have already been copied or moved by the parent. Also,
  /// if you do a recursive copy or move, then the copy or move should succeed or fail as a unit. If the copy or move can partially
  /// fail, then you should do it non-recursively and allow this method to handle the recursion, so that it can report errors accurately.
  /// </para>
  /// <para>
  /// If this parameter is null, the <see cref="IWebDAVService.CopyResource"/> method of <see cref="DestinationInfo.Service"/> will be
  /// used. (If both <paramref name="createDest"/> and <see cref="DestinationInfo.Service"/> are null, an exception will be thrown.)
  /// See <see cref="IWebDAVService.CopyResource"/> for additional details about how the parameters should be interpreted and how the
  /// copy should be performed.
  /// </para>
  /// </param>
  /// <remarks><note type="caution">This method rejects attempts to copy or move the source resource to a descendant or ancestor of the
  /// source resource if the attempt can be detected simply be examining the paths involved. However, this is not sufficient to detect all
  /// such requests. It is possible that even if the destination refers to a different location than the source in URL space, it can refer
  /// to overlapping locations in the underlying data store. For example, two WebDAV services at /root and /users could point to
  /// overlapping parts of the same filesystem (e.g. C:\ and C:\Users). Another possibility is that the request
  /// <see cref="WebDAVContext.Service"/> and destination <see cref="DestinationInfo.Service"/> happen to be different due to a rare
  /// combination of an unusual <c>Destination</c> header and an error and race condition with another thread. (See
  /// <see cref="DestinationInfo.Service"/> for details.) The <see cref="IWebDAVResource"/> that processes this request is responsible for
  /// making sure that it either disallows copies/moves from the request resource to a descendant of the request resource (such as copying
  /// or moving C:\Foo to C:\Foo\Bar\Baz), or that it can handle such operations correctly. (Copies to a descendant or ancestor and moves
  /// to an ancestor are possible, but care must be taken to avoid data loss or infinite recursion; moves to a descendant are impossible
  /// and must be rejected, because they would result in an inconsistent URL namespace.)
  /// </note></remarks>
  public void ProcessStandardRequest<T,TInfo>(T requestResource, Func<T,string,TInfo> getInfo, Func<T,string,ConditionCode> deleteSource,
                                              Func<CopyOrMoveRequest,string,TInfo,ConditionCode> createDest)
    where TInfo : ISourceResource<T>
  {
    if(getInfo == null || IsMove && deleteSource == null) throw new ArgumentNullException();

    TInfo rootInfo = getInfo(requestResource, Context.CanonicalPathIfKnown);
    if(rootInfo == null) throw new ContractViolationException("getInfo returned null for " + Convert.ToString(requestResource));
    if(IsMove && Depth != Depth.SelfAndDescendants && rootInfo.IsCollection)
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
      string source = DAVUtility.WithTrailingSlash(Context.CanonicalPathIfKnown);
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
      createDest = (request,path,info) => Destination.Service.CopyResource(this, path, info);
    }

    // now return non-error preconditions before doing the operation
    if(precondition != null)
    {
      Status = precondition;
      return;
    }

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
        destRequestPath = DAVUtility.UriPathDecode(Destination.Uri.AbsolutePath).TrimStart('/');
      }
      success = ProcessStandardRequest(requestResource, rootInfo, Context.RequestPath, Context.CanonicalPathIfKnown, destServiceRoot,
                                       destRequestPath, getInfo, deleteSource, createDest);
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

  bool ProcessStandardRequest<T,TInfo>(T resource, TInfo info, string requestPath, string canonicalPath, string destServiceRoot,
                                       string destRequestPath, Func<T,string,TInfo> getInfo, Func<T,string,ConditionCode> deleteSource,
                                       Func<CopyOrMoveRequest,string,TInfo,ConditionCode> createDest) where TInfo : ISourceResource<T>
  {
    // copy over the resource (non-recursively)
    ConditionCode status;
    try
    {
      status = createDest(this, destRequestPath, info);
    }
    catch(System.Web.HttpException ex)
    {
      WebDAVException wde = ex as WebDAVException;
      status =
        (wde != null ? wde.ConditionCode : null) ?? new ConditionCode(ex.GetHttpCode(), WebDAVModule.FilterErrorMessage(ex.Message));
    }
    catch(UnauthorizedAccessException)
    {
      status = ConditionCodes.Forbidden;
    }
    catch(Exception ex)
    {
      status = new ConditionCode(HttpStatusCode.InternalServerError, WebDAVModule.FilterErrorMessage(ex.Message));
    }

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
    if(Depth == Server.Depth.SelfAndDescendants && info.IsCollection)
    {
      string requestBase = DAVUtility.WithTrailingSlash(requestPath), destRequestBase = DAVUtility.WithTrailingSlash(destRequestPath);
      foreach(T child in info.GetChildren())
      {
        TInfo childInfo = getInfo(child, canonicalPath);
        if(childInfo == null) throw new ContractViolationException("getInfo returned null for " + Convert.ToString(child));
        string name = childInfo.GetMemberName(Context);
        success &= ProcessStandardRequest(child, childInfo, requestBase + name, childInfo.CanonicalPath, destServiceRoot,
                                          destRequestBase + name, getInfo, deleteSource, createDest);
      }
    }

    // now delete the source if it's a move
    if(success && IsMove)
    {
      try
      {
        status = deleteSource(resource, canonicalPath);
      }
      catch(System.Web.HttpException ex)
      {
        WebDAVException wde = ex as WebDAVException;
        status =
          (wde != null ? wde.ConditionCode : null) ?? new ConditionCode(ex.GetHttpCode(), WebDAVModule.FilterErrorMessage(ex.Message));
      }
      catch(UnauthorizedAccessException)
      {
        status = ConditionCodes.Forbidden;
      }
      catch(Exception ex)
      {
        status = new ConditionCode(HttpStatusCode.InternalServerError, WebDAVModule.FilterErrorMessage(ex.Message));
      }

      if(status == null || status.IsSuccessful)
      {
        if(Context.LockManager != null) Context.LockManager.RemoveLocks(canonicalPath, LockRemoval.Nonrecursive);
        if(Context.PropertyStore != null) Context.PropertyStore.ClearProperties(canonicalPath, false);
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
