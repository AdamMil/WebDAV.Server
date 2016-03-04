/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2016 by Adam Milazzo

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
using System.Security.Principal;
using System.Collections.Generic;
using System.Xml;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

#region IWebDAVService
/// <summary>Represents a WebDAV service that serves a subtree within a URL namespace. In most cases, you'll want to derive from
/// <see cref="WebDAVService"/> rather than directly implementing this interface.
/// </summary>
/// <remarks>A WebDAV service must be usable across multiple requests on multiple threads simultaneously. Before implementing a WebDAV
/// service, it is strongly recommended that you be familiar with the WebDAV specification in RFC 4918 and the HTTP specification in
/// RFCs 7230 through 7235.
/// </remarks>
/// <seealso cref="WebDAVService"/>
public interface IWebDAVService
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CanDeleteLock/node()" />
  /// <example>This example shows a typical implementation of this method - the same implementation used by <see cref="WebDAVService"/>.
  /// <code>
  /// public bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  /// {
  ///   if(context == null || lockObject == null) throw new ArgumentNullException();
  ///   return string.Equals(context.CurrentUserId, lockObject.OwnerId, StringComparison.Ordinal);
  /// }
  /// </code>
  /// This example shows a typical case where you might want to customize this method.
  /// <code>
  /// public bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  /// {
  ///   if(context == null || lockObject == null) throw new ArgumentNullException();
  ///   // allow the administrator to delete any lock
  ///   return string.Equals(context.CurrentUserId, lockObject.OwnerId, StringComparison.Ordinal) ||
  ///          string.Equals(context.CurrentUserId, "admin", StringComparison.Ordinal);
  /// }
  /// </code>
  /// </example>
  bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CopyResource/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.CopyResource"/>.</example>
  /// <seealso cref="CopyOrMoveRequest"/>
  ConditionCode CopyResource(CopyOrMoveRequest request, string destinationPath, IStandardResource sourceResource);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateAndLock/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.CreateAndLock"/>.</example>
  /// <seealso cref="LockRequest"/> <seealso cref="ILockManager"/>
  void CreateAndLock(LockRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateCopyOrMove/node()" />
  /// <example>A typical implementation simply returns a new <see cref="CopyOrMoveRequest"/>.</example>
  CopyOrMoveRequest CreateCopyOrMove(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateDelete/node()" />
  /// <example>A typical implementation simply returns a new <see cref="DeleteRequest"/>.</example>
  DeleteRequest CreateDelete(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateGetOrHead/node()" />
  /// <example>A typical implementation simply returns a new <see cref="GetOrHeadRequest"/>.</example>
  GetOrHeadRequest CreateGetOrHead(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateLock/node()" />
  /// <example>A typical implementation simply returns a new <see cref="LockRequest"/>.</example>
  LockRequest CreateLock(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateMkCol/node()" />
  /// <example>A typical implementation simply returns a new <see cref="MkColRequest"/>.</example>
  MkColRequest CreateMkCol(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateOptions/node()" />
  /// <example>A typical implementation simply returns a new <see cref="OptionsRequest"/>.</example>
  OptionsRequest CreateOptions(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePost/node()" />
  /// <example>A typical implementation simply returns a new <see cref="PostRequest"/>.</example>
  PostRequest CreatePost(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropFind/node()" />
  /// <example>A typical implementation simply returns a new <see cref="PropFindRequest"/>.</example>
  PropFindRequest CreatePropFind(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropPatch/node()" />
  /// <example>A typical implementation simply returns a new <see cref="PropPatchRequest"/>.</example>
  PropPatchRequest CreatePropPatch(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePut/node()" />
  /// <example>A typical implementation simply returns a new <see cref="PutRequest"/>.</example>
  PutRequest CreatePut(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateUnlock/node()" />
  /// <example>A typical implementation simply returns a new <see cref="UnlockRequest"/>.</example>
  UnlockRequest CreateUnlock(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/GetCanonicalUnmappedPath/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.GetCanonicalUnmappedPath"/>.</example>
  string GetCanonicalUnmappedPath(WebDAVContext context, string relativePath);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/GetCurrentUserId/node()" />
  /// <example>This example shows a typical implementation of this method - the same implementation used by <see cref="WebDAVService"/>.
  /// <code>
  /// public string GetCurrentUserId(WebDAVContext context)
  /// {
  ///   IIdentity identity = context.User == null ? null : context.User.Identity;
  ///   return identity == null || !identity.IsAuthenticated || string.IsNullOrEmpty(identity.Name) ?
  ///     null : identity.AuthenticationType + ":" + identity.Name;
  /// }
  /// </code>
  /// </example>
  string GetCurrentUserId(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/HandleGenericRequest/node()" />
  bool HandleGenericRequest(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/MakeCollection/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.MakeCollection"/>.</example>
  /// <seealso cref="MkColRequest"/>
  void MakeCollection(MkColRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Options/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.Options"/>.</example>
  void Options(OptionsRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Post/node()" />
  void Post(PostRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Put/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.Put"/>.</example>
  /// <seealso cref="PutRequest"/>
  void Put(PutRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.ResolveResource"/>.</example>
  IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ShouldDenyAccess1/node()" />
  /// <example>This example shows a typical implementation of this method - the same implementation used by <see cref="WebDAVService"/>.
  /// <code>
  /// public bool ShouldDenyAccess(WebDAVContext context, IEnumerable&lt;IAuthorizationFilter&gt; authFilters, out ConditionCode response)
  /// {
  ///   if(context == null) throw new ArgumentNullException();
  ///   return ShouldDenyAccess(context, context.RequestResource, authFilters, GetDefaultPermissionCheck(context), out response);
  /// }
  /// 
  /// protected virtual XmlQualifiedName GetDefaultPermissionCheck(WebDAVContext context)
  /// {
  ///   if(context == null) throw new ArgumentNullException();
  ///   // get the permission used for the HTTP method, either read (null) or write
  ///   string method = context.Request.HttpMethod;
  ///   if(method.OrdinalEquals(DAVMethods.Lock) || method.OrdinalEquals(DAVMethods.Unlock) || method.OrdinalEquals(DAVMethods.Put) ||
  ///      method.OrdinalEquals(DAVMethods.PropPatch) || method.OrdinalEquals(DAVMethods.Delete) || method.OrdinalEquals(DAVMethods.MkCol))
  ///   {
  ///     return DAVNames.write;
  ///   }
  ///   return null;
  /// }
  /// </code>
  /// </example>
  bool ShouldDenyAccess(WebDAVContext context, IEnumerable<IAuthorizationFilter> authFilters, out ConditionCode response);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ShouldDenyAccess2/node()" />
  /// <example>This example shows a typical implementation of this method - the same implementation used by <see cref="WebDAVService"/>.
  /// <code>
  /// public bool ShouldDenyAccess(WebDAVContext context, IWebDAVResource resource, IEnumerable&lt;IAuthorizationFilter&gt; authFilters,
  ///                              XmlQualifiedName access, out ConditionCode response)
  /// {
  ///   if(context == null) throw new ArgumentNullException();
  ///   response = null;
  ///
  ///   bool denyAccess = false;
  ///   if(authFilters != null)
  ///   {
  ///     foreach(IAuthorizationFilter filter in authFilters)
  ///     {
  ///       denyAccess |= filter.ShouldDenyAccess(context, this, resource, access, out response);
  ///       if(denyAccess &amp;&amp; response != null) break;
  ///     }
  ///   }
  /// 
  ///   // if access hasn't been denied yet or we aren't sure what response to send, give the resource a chance to answer
  ///   if(resource != null &amp;&amp; (!denyAccess || response == null)) denyAccess |= resource.ShouldDenyAccess(context, this, access, out response);
  ///
  ///   return denyAccess;
  /// }
  /// </code>
  /// </example>
  bool ShouldDenyAccess(WebDAVContext context, IWebDAVResource resource, IEnumerable<IAuthorizationFilter> authFilters,
                        XmlQualifiedName access, out ConditionCode response);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Unlock/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVService.Unlock"/>.</example>
  /// <seealso cref="UnlockRequest"/> <see cref="ILockManager"/>
  void Unlock(UnlockRequest request);
}
#endregion

#region WebDAVService
/// <summary>Provides a base class to simplify the implementation of <see cref="IWebDAVService"/>.</summary>
/// <remarks>
/// For a read-only service deriving from this class, the only method that must be implemented is <see cref="ResolveResource"/>,
/// although some other methods may be of interest for certain types of read-only services.
/// <list type="table">
/// <listheader>
///   <term>Method</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term>Create* (request creators)</term>
///   <description>Your service uses a custom <see cref="WebDAVRequest"/> class for one or more HTTP methods.</description>
/// </item>
/// <item>
///   <term><see cref="CreateAndLock"/> and <see cref="Unlock"/></term>
///   <description>Your service wants to support locking despite being read-only.</description>
/// </item>
/// <item>
///   <term><see cref="GetCanonicalUnmappedPath"/></term>
///   <description>Your service normalizes or canonicalizes URLs in any special way, including treating them case-insensitively.</description>
/// </item>
/// <item>
///   <term><see cref="GetCurrentUserId"/></term>
///   <description>
///   Your service has a custom authentication scheme that's not integrated with ASP.NET. (The default implementation should work with any
///   authentication scheme that correctly sets up the ASP.NET <see cref="System.Web.HttpContext.User"/>.)
///   </description>
/// </item>
/// <item>
///   <term><see cref="GetDefaultPermissionCheck"/></term>
///   <description>You need to change the initial type of permission check used for requests.</description>
/// </item>
/// <item>
///   <term><see cref="HandleGenericRequest"/></term>
///   <description>Your service wants to handle custom HTTP methods (verbs).</description>
/// </item>
/// <item>
///   <term><see cref="Options"/></term>
///   <description>
///   Your service wants to report custom WebDAV extensions, support locking despite being read-only, or otherwise change the default
///   <see cref="OptionsRequest"/> response.
///   </description>
/// </item>
/// <item>
///   <term><see cref="Post"/></term>
///   <description>Your service wants to handle <c>POST</c> requests.</description>
/// </item>
/// <item>
///   <term><see cref="ShouldDenyAccess(WebDAVContext,IEnumerable{IAuthorizationFilter},out ConditionCode)"/></term>
///   <description>Your service wants to change how the initial access check against the request resource is done.</description>
/// </item>
/// <item>
///   <term><see cref="ShouldDenyAccess(WebDAVContext,IWebDAVResource,IEnumerable{IAuthorizationFilter},XmlQualifiedName,out ConditionCode)"/></term>
///   <description>Your service wants to change how all access checks are done.</description>
/// </item>
/// </list>
/// <para>
/// If your service supports writing resources, then in addition to the methods listed above you should usually implement the following:
/// <see cref="CopyResource"/> (to support being the target of <c>COPY</c> and <c>MOVE</c> requests), <see cref="MakeCollection"/> (to
/// create new collection resources), <see cref="Options"/> (to report support for HTTP write methods), and <see cref="Put"/> (to create
/// new resources, usually files). If you support locking, you must implement <see cref="CreateAndLock"/> and <see cref="Unlock"/>, as
/// well as <see cref="Options"/> (to report support for locking). Similar considerations apply to the <see cref="IWebDAVResource"/>
/// objects created by your service.
/// </para>
/// <note type="caution">A WebDAV service must be usable across multiple requests on multiple threads simultaneously. Before implementing a
/// WebDAV service, it is strongly recommended that you be familiar with the WebDAV specification in RFC 4918 and the HTTP specification in
/// RFCs 7230 through 7235.
/// </note>
/// </remarks>
public abstract class WebDAVService : IWebDAVService
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CanDeleteLock/node()" />
  /// <remarks><note type="inherit">The default implementation returns true if the two user IDs are identical, and false otherwise.</note></remarks>
  /// <example>This example shows a typical case where you might want to override this method. This can also be implemented using an
  /// <see cref="IAuthorizationFilter"/>.
  /// <code>
  /// public override bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  /// {
  ///   // allow the administrator to delete any lock
  ///   return base.CanDeleteLock(context, lockObject) || context.CurrentUserId == "admin";
  /// }
  /// </code>
  /// </example>
  public virtual bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  {
    if(context == null || lockObject == null) throw new ArgumentNullException();
    return string.Equals(context.CurrentUserId, lockObject.OwnerId, StringComparison.Ordinal);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CopyResource/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 502 Bad Gateway, indicating that the service is not allowed to
  /// be the target of a <c>COPY</c> or <c>MOVE</c> request.
  /// </note></remarks>
  /// <example>This example shows a typical pattern of implementation for this method.
  /// <code>
  /// public override ConditionCode CopyResource(CopyOrMoveRequest request, string destinationPath, IStandardResource sourceResource)
  /// {
  ///   if(request == null || destinationPath == null || sourceResource == null) throw new ArgumentNullException();
  ///   if(IsReadOnly || !CanUserWrite(request, destinationPath)) return ConditionCodes.Forbidden; // access check
  ///   if(IsIllegalPath(destinationPath)) return ConditionCodes.BadPathCharacters; // if the path is bad and we can't fix it, give up
  ///       
  ///   // potentially overwrite the destination resource if it exists
  ///   ConditionCode status;
  ///   bool overwrote = false;
  ///   IWebDAVResource resource = ResolveResource(request.Context, destinationPath);
  ///   if(resource != null) // if a resource already exists at the destination path...
  ///   {
  ///     if(!request.Overwrite) return ConditionCodes.PreconditionFailed; // return 412 Precondition Failed if we can't overwrite it
  ///     IStandardResource stdResource = resource as IStandardResource;
  ///     if(stdResource == null) return ConditionCodes.Forbidden; // if it's not a standard resource, then we don't know how to delete it
  ///     status = stdResource.Delete(); // otherwise, try to delete the existing resource
  ///     if(!DAVUtility.IsSuccess(status)) return status;
  ///     request.PostProcessOverwrite(stdResource.CanonicalPath); // delete its locks and dead properties
  ///     overwrote = true; // and remember that we overwrote it
  ///   }
  /// 
  ///   // create the new destination resource
  ///   if(sourceResource.IsCollection) // if we should create a directory...
  ///   {
  ///     destinationPath = DAVUtility.WithTrailingSlash(destinationPath); // canonicalize the path for later
  ///     status = CreateCollection(destinationPath);
  ///   }
  ///   else
  ///   {
  ///     status = CreateFile(destinationPath, sourceResource);
  ///   }
  /// 
  ///   // if the creation was successful, copy properties and delete locks
  ///   if(DAVUtility.IsSuccess(status))
  ///   {
  ///     if(request.IsMove) // preserve creation and modification time when moving objects
  ///     {
  ///       EntityMetadata metadata = sourceResource.GetEntityMetadata(false);
  ///       if(metadata.LastModifiedTime.HasValue) SetLastModifiedTime(destinationPath, metadata.LastModifiedTime.Value);
  ///
  ///       DateTime createdDate;
  ///       var properties = sourceResource.GetLiveProperties(request.Context);
  ///       if(GetPropertyValue(properties, DAVNames.creationdate, out createdDate)) SetCreatedDate(destinationPath, createdDate);
  ///     }
  ///     request.PostProcessCopy(sourceResource.CanonicalPath, destinationPath); // copy dead properties and delete locks
  ///     status = overwrote ? ConditionCodes.NoContent : ConditionCodes.Created; // return 204 or 201 as RFC 4918 says we should
  ///   }
  /// 
  ///   return status;
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="CopyOrMoveRequest"/>
  public virtual ConditionCode CopyResource(CopyOrMoveRequest request, string destinationPath, IStandardResource sourceResource)
  {
    return ConditionCodes.BadGateway;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateAndLock/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 403 Forbidden, indicating that the service does not support
  /// the locking of new resources.
  /// </note></remarks>
  /// <example>This example shows the basic implementation pattern for this method.
  /// <code>
  /// public override void CreateAndLock(LockRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.CreateAndLock(request); // call the base class, which denies the request
  ///   else request.ProcessStandardRequest(LockType.WriteLocks, () => CreateFile(request.Context));
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="LockRequest"/> <seealso cref="ILockManager"/>
  public virtual void CreateAndLock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This service does not support the locking of new resources.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateCopyOrMove/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="CopyOrMoveRequest"/>.</note></remarks>
  public virtual CopyOrMoveRequest CreateCopyOrMove(WebDAVContext context)
  {
    return new CopyOrMoveRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateDelete/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="DeleteRequest"/>.</note></remarks>
  public virtual DeleteRequest CreateDelete(WebDAVContext context)
  {
    return new DeleteRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateGetOrHead/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="GetOrHeadRequest"/>.</note></remarks>
  public virtual GetOrHeadRequest CreateGetOrHead(WebDAVContext context)
  {
    return new GetOrHeadRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateLock/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="LockRequest"/>.</note></remarks>
  public virtual LockRequest CreateLock(WebDAVContext context)
  {
    return new LockRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateMkCol/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="MkColRequest"/>.</note></remarks>
  public virtual MkColRequest CreateMkCol(WebDAVContext context)
  {
    return new MkColRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateOptions/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="OptionsRequest"/>.</note></remarks>
  public virtual OptionsRequest CreateOptions(WebDAVContext context)
  {
    return new OptionsRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePost/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="PostRequest"/>.</note></remarks>
  public virtual PostRequest CreatePost(WebDAVContext context)
  {
    return new PostRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropFind/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="PropFindRequest"/>.</note></remarks>
  public virtual PropFindRequest CreatePropFind(WebDAVContext context)
  {
    return new PropFindRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropPatch/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="PropPatchRequest"/>.</note></remarks>
  public virtual PropPatchRequest CreatePropPatch(WebDAVContext context)
  {
    return new PropPatchRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePut/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="PutRequest"/>.</note></remarks>
  public virtual PutRequest CreatePut(WebDAVContext context)
  {
    return new PutRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateUnlock/node()" />
  /// <remarks><note type="inherit">The default implementation returns a new <see cref="UnlockRequest"/>.</note></remarks>
  public virtual UnlockRequest CreateUnlock(WebDAVContext context)
  {
    return new UnlockRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/GetCanonicalUnmappedPath/node()" />
  /// <remarks><note type="inherit">The default implementation returns <paramref name="relativePath"/> unchanged.</note></remarks>
  /// <example>
  /// This is an example implementation for a service that treats paths case-insensitively but otherwise only has one path per resource.
  /// <code>
  /// public override string GetCanonicalUnmappedPath(WebDAVContext context, string relativePath)
  /// {
  ///   if(context == null || relativePath == null) throw new ArgumentNullException();
  ///   return NormalizeCase(relativePath);
  /// }
  /// 
  /// static string NormalizeCase(string path)
  /// {
  ///   return path.ToUpperInvariant();
  /// }
  /// </code>
  /// This is an example implementation for a service that allows aliases for various resources (so multiple paths may map to the same
  /// resource).
  /// <code>
  /// public override string GetCanonicalUnmappedPath(WebDAVContext context, string relativePath)
  /// {
  ///   if(relativePath.Length > 2) // if the path could have multiple segments...
  ///   {
  ///     // try to canonicalize each of the previous segments. for example, if the unmapped path was "dir/a/b", then try resolving "dir/a/"
  ///     // and "dir/". let's say "dir/" resolves to a resource with a canonical path of "Resources/Dir/". then we return "Resources/Dir/a/b"
  ///     for(int index=relativePath.Length-1; ; )
  ///     {
  ///       index = relativePath.LastIndexOf('/', index-1);
  ///       if(index &lt; 0) break;
  ///       IWebDAVResource resource = ResolveResource(context, relativePath.Substring(0, index+1)); // resolve the prefix, including the slash
  ///       if(resource != null) return DAVUtility.WithTrailingSlash(resource.CanonicalPath) + relativePath.Substring(index+1);
  ///     }
  ///   }
  ///   return relativePath;
  /// }
  /// </code>
  /// </example>
  public virtual string GetCanonicalUnmappedPath(WebDAVContext context, string relativePath)
  {
    if(relativePath == null) throw new ArgumentNullException();
    return relativePath;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/GetCurrentUserId/node()" />
  /// <remarks><note type="inherit">The default implementation returns a user ID based on ASP.NET authentication providers by combining the
  /// currently authenticated user's <see cref="IIdentity.AuthenticationType"/> and <see cref="IIdentity.Name"/> in the format
  /// <c>authType:userName</c>. If you have a better mechanism to identify users, you should override this method as well as the
  /// <see cref="CanDeleteLock"/> method. The <see cref="IIdentity.AuthenticationType"/> is "Negotiate" for basic/digest
  /// authentication and "NTLM" for NTLM authentication.
  /// </note></remarks>
  public virtual string GetCurrentUserId(WebDAVContext context)
  {
    if(context == null) throw new ArgumentNullException();
    IIdentity identity = context.User == null ? null : context.User.Identity;
    return identity == null || !identity.IsAuthenticated || string.IsNullOrEmpty(identity.Name) ?
      null : identity.AuthenticationType + ":" + identity.Name;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/HandleGenericRequest/node()" />
  /// <remarks><note type="inherit">The default implementation does not handle any generic requests and always returns false.</note></remarks>
  public virtual bool HandleGenericRequest(WebDAVContext context)
  {
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/MakeCollection/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 403 Forbidden, indicating that the service does not support
  /// the creation of new collections.
  /// </note></remarks>
  /// <example>This example shows the basic implementation pattern for this method.
  /// <code>
  /// public override void MakeCollection(MkColRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.MakeCollection(request); // call the base class, which denies the request
  ///   else request.ProcessStandardRequest(() => CreateDirectory(request.Context));
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="MkColRequest"/>
  public virtual void MakeCollection(MkColRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This service does not support the creation of new collections.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Options/node()" />
  /// <remarks><note type="inherit">The default implementation uses the defaults, which are suitable for a read-only service that does not
  /// support locking.
  /// </note></remarks>
  /// <example>This example shows the basic implementation pattern for this method.
  /// <code>
  /// public override void Options(OptionsRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(!isReadOnly) // the defaults are usually sufficient for services in read-only mode
  ///   {
  ///     // if IsServerQuery is true, it's asking about the capabilities of the service in general, so report that we support
  ///     // MKCOL and PUT. otherwise, it's asking about a specific (non-existent) resource, so look at the URL to see what
  ///     // methods we support. for example, we might support MKCOL on paths like /foo and /foo/, but PUT only on paths like /foo
  ///     if(request.IsServerQuery || IsLegalCollectionPath(request.Context.RequestPath)) request.AllowedMethods.Add(DAVMethods.MkCol);
  ///     if(request.IsServerQuery || IsLegalFilePath(request.Context.RequestPath)) request.AllowedMethods.Add(DAVMethods.Put);
  ///     request.SupportsLocking = request.Context.LockManager != null; // enable locking if there's a lock manager
  ///   }
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="OptionsRequest"/>
  public virtual void Options(OptionsRequest request) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Post/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 404 Not Found.</note></remarks>
  /// <seealso cref="PostRequest"/>
  public virtual void Post(PostRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.NotFound;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Put/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 403 Forbidden, indicating that the service does not support
  /// the creation of resources.
  /// </note></remarks>
  /// <example>This example shows a typical implementation pattern for this method.
  /// <code>
  /// public override void Put(PutRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.Put(request); // call the base class, which denies the request
  ///   else if(!IsLegalFilePath(request.Context.RequestPath)) request.Status = ConditionCodes.MethodNotAllowed;
  ///   else
  ///   {
  ///     request.ProcessStandardRequest(delegate(out Stream stream) { stream = CreateFile(request); return null; });
  ///     if(DAVUtility.IsSuccess(request.Status)) // if the PUT request succeeded, send the ETag and Last-Modified headers to the client
  ///     {
  ///       request.Context.Response.Headers[DAVHeaders.ETag] = ComputeEntityTag(stream, true).ToHeaderString();
  ///       request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(GetLastModifiedDate(stream));
  ///     }
  ///     else // otherwise, delete the file we created
  ///     {
  ///       DeleteFile(request);
  ///     }
  ///   }
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="PutRequest"/>
  public virtual void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status =
      new ConditionCode(HttpStatusCode.Forbidden, "This service does not support the creation or alteration of resource entities.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public abstract IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ShouldDenyAccess1/*[not(self::remarks)]" />
  /// <remarks><include file="documentation.xml" path="/DAV/IWebDAVService/ShouldDenyAccess1/remarks/node()" />
  /// <note type="inherit">The default implementation calls
  /// <see cref="ShouldDenyAccess(WebDAVContext,IWebDAVResource,IEnumerable{IAuthorizationFilter},XmlQualifiedName,out ConditionCode)"/>
  /// with <see cref="WebDAVContext.RequestResource"/> and the access check from <see cref="GetDefaultPermissionCheck"/>.
  /// </note>
  /// </remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, IEnumerable<IAuthorizationFilter> authFilters, out ConditionCode response)
  {
    if(context == null) throw new ArgumentNullException();
    return ShouldDenyAccess(context, context.RequestResource, authFilters, GetDefaultPermissionCheck(context), out response);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ShouldDenyAccess2/node()" />
  /// <remarks><note type="inherit">The default implementation queries the authorization filters and the given resource (if any), and
  /// denies access if any filter or the resource says we should.
  /// </note></remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, IWebDAVResource resource, IEnumerable<IAuthorizationFilter> authFilters,
                                       XmlQualifiedName access, out ConditionCode response)
  {
    if(context == null) throw new ArgumentNullException();
    response = null;

    bool denyAccess = false;
    if(authFilters != null)
    {
      foreach(IAuthorizationFilter filter in authFilters)
      {
        denyAccess |= filter.ShouldDenyAccess(context, this, resource, access, out response);
        if(denyAccess && response != null) break;
      }
    }

    // if access hasn't been denied yet or we aren't sure what response to send, give the resource a chance to answer
    if(resource != null && (!denyAccess || response == null)) denyAccess |= resource.ShouldDenyAccess(context, this, access, out response);

    return denyAccess;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Unlock/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 403 Forbidden, indicating that the service does not support
  /// the unlocking of unmapped URLs.
  /// </note></remarks>
  /// <example>This example shows the basic implementation pattern for this method.
  /// <code>
  /// public override void Unlock(UnlockRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.Unlock(request); // deny the request if locking is not supported
  ///   else request.ProcessStandardRequest(CanonicalizePath(request.Context.RequestPath)); // otherwise, remove the dangling lock
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="UnlockRequest"/> <seealso cref="ILockManager"/>
  public virtual void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This service does not support the unlocking of unmapped URLs.");
  }

  /// <summary>Gets the default permission that should be used for checking access to the request resource.</summary>
  /// <remarks>The permission returned by this function is used to grant or deny access to the resource before any resource processing
  /// begins.
  /// <note type="inherit">The default implementation returns <see cref="DAVNames.write"/> for <c>DELETE</c>, <c>LOCK</c>, <c>MKCOL</c>,
  /// <c>PROPPATCH</c>, <c>PUT</c>, and <c>UNLOCK</c> requests, and null (read access) otherwise.
  /// </note>
  /// </remarks>
  /// <example>This example shows a typical implementation pattern for this method.
  /// <code>
  /// protected override XmlQualifiedName GetDefaultPermissionCheck(WebDAVContext context)
  /// {
  ///   // perform a write access check by default for requests using the SWIZZLE method. otherwise, fall back on the base class
  ///   if(string.Equals(context.Request.HttpMethod, "SWIZZLE", StringComparison.Ordinal)) return DAVNames.write;
  ///   else return base.GetDefaultPermissionCheck(context);
  /// }
  /// </code>
  /// </example>
  protected virtual XmlQualifiedName GetDefaultPermissionCheck(WebDAVContext context)
  {
    if(context == null) throw new ArgumentNullException();
    // get the permission used for the HTTP method, either read (null) or write
    string method = context.Request.HttpMethod;
    if(method.OrdinalEquals(DAVMethods.Lock) || method.OrdinalEquals(DAVMethods.Unlock) || method.OrdinalEquals(DAVMethods.Put) ||
       method.OrdinalEquals(DAVMethods.PropPatch) || method.OrdinalEquals(DAVMethods.Delete) || method.OrdinalEquals(DAVMethods.MkCol))
    {
      return DAVNames.write;
    }
    return null;
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
