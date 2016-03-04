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

namespace AdamMil.WebDAV.Server
{

#region IWebDAVResource
/// <summary>Represents a DAV-compliant resource. In most cases you'll want to derive from <see cref="WebDAVResource"/> rather than
/// directly implementing this interface.
/// </summary>
/// <remarks>Before implementing a WebDAV resource, it is strongly recommended that you be familiar with the WebDAV specification in RFC
/// 4918 and the HTTP specification in RFCs 7230 through 7235.
/// </remarks>
/// <seealso cref="WebDAVResource"/>
public interface IWebDAVResource
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.CopyOrMove"/>.</example>
  /// <seealso cref="CopyOrMoveRequest"/>
  void CopyOrMove(CopyOrMoveRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.Delete"/>.</example>
  /// <seealso cref="DeleteRequest"/>
  void Delete(DeleteRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  EntityMetadata GetEntityMetadata(bool includeEntityTag);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.GetOrHead"/>.</example>
  /// <seealso cref="GetOrHeadRequest"/>
  void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  bool HandleGenericRequest(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.Lock"/>.</example>
  /// <seealso cref="LockRequest"/>
  void Lock(LockRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.Options"/>.</example>
  /// <seealso cref="OptionsRequest"/>
  void Options(OptionsRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Post/node()" />
  /// <seealso cref="PostRequest"/>
  void Post(PostRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.PropFind"/>.</example>
  /// <seealso cref="PropFindRequest"/>
  void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatchRemarks/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.PropPatch"/>.</example>
  /// <seealso cref="PropPatchRequest"/>
  void PropPatch(PropPatchRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.Put"/>.</example>
  /// <seealso cref="PutRequest"/>
  void Put(PutRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/ShouldDenyAccess/node()" />
  bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, XmlQualifiedName access, out ConditionCode response);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  /// <example>For implementation examples, see <see cref="WebDAVResource.Unlock"/>.</example>
  /// <seealso cref="UnlockRequest"/>
  void Unlock(UnlockRequest request);
}
#endregion

#region IStandardResource
/// <summary>Represents a WebDAV resource that uses standard processing for certain requests. This interface is usually implemented by way
/// of implementing the <see cref="IStandardResource{T}"/> interface, which derives from it.
/// </summary>
public interface IStandardResource : IWebDAVResource
{
  /// <summary>Gets whether this resource is a collection resource, which may contain child resources.</summary>
  bool IsCollection { get; }

  /// <summary>Attempts to recursively delete the resource and returns a <see cref="ConditionCode"/> indicating whether the attempt
  /// succeeded, or null for a default success code.
  /// </summary>
  /// <remarks>If the resource is read-only, this method should return <see cref="ConditionCodes.Forbidden"/>.</remarks>
  ConditionCode Delete();

  /// <summary>Returns a dictionary containing the live properties of the resource. The property values should be in the same form as
  /// those given to a <see cref="PropFindRequest"/>. (See
  /// <see cref="PropFindRequest.ProcessStandardRequest(IDictionary{XmlQualifiedName, object})"/> for details.)
  /// </summary>
  /// <remarks>Generally, live properties need not be returned if they are would not be respected by the destination service. For
  /// example, the <c>DAV:lockdiscovery</c>, <c>DAV:supportedlock</c>, and <c>DAV:getetag</c> properties are determined entirely by the
  /// destination resource, and should not be returned (although returning them isn't illegal).
  /// </remarks>
  /// <example>
  /// <code>
  /// IDictionary&lt;XmlQualifiedName, object&gt; IStandardResource&lt;T&gt;.GetLiveProperties(WebDAVContext context)
  /// {
  ///   return GetLiveProperties((WebDAVRequest)null);
  /// }
  /// 
  /// IDictionary&lt;XmlQualifiedName, object&gt; GetLiveProperties(PropFindRequest request)
  /// {
  ///   var properties = new Dictionary&lt;XmlQualifiedName, object&gt;();
  ///   properties[DAVNames.resourcetype] = IsCollection ? ResourceType.Collection : null; // null indicates a non-collection resource
  /// 
  ///   // add file-related properties if this is a file
  ///   if(!IsCollection)
  ///   {
  ///     properties[DAVNames.getcontentlength] = DataLength;
  ///     properties[DAVNames.getlastmodified]  = LastWriteTime;
  /// 
  ///     string mediaType = MediaTypes.GuessMediaType(Name);
  ///     if(mediaType != null) properties[DAVNames.getcontenttype] = mediaType;
  /// 
  ///     // we don't want to return the DAV:getetag property unless it's necessary, since it's expensive to compute
  ///     if(request != null &amp;&amp; request.MustIncludeProperty(DAVNames.getetag)) // if we must include it (but not necessarily its value)...
  ///     {
  ///       // we have to report the property, but depending on the NamesOnly property we may be able avoid computing its value
  ///       properties[DAVNames.getetag] = request.NamesOnly ? null : GetEntityMetadata(true).EntityTag;
  ///     }
  ///   }
  /// 
  ///   // if we support locking and we're servicing a PROPFIND request (as opposed to a COPY/MOVE request), add lock-related properties
  ///   if(!isReadOnly &amp;&amp; request != null &amp;&amp; request.Context.LockManager != null)
  ///   {
  ///     // here we want to include the lockdiscovery value unless it must not be returned. we'll use the MustExcludePropertyValue function
  ///     // to replace the value with null whenever it's not needed
  ///     properties[DAVNames.lockdiscovery] = request.MustExcludePropertyValue(DAVNames.lockdiscovery) ?
  ///       null : request.Context.LockManager.GetLocks(CanonicalPath, LockSelection.SelfAndRecursiveAncestors, null);
  ///     properties[DAVNames.supportedlock] = LockType.WriteLocks;
  ///   }
  /// 
  ///   return properties;
  /// }
  /// </code>
  /// </example>
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

  /// <summary>Returns a readable and preferably seekable stream containing the resource's data. This is usually but not necessarily the
  /// body that would be returned from a GET request. For instance, a collection resource might respond with an HTML list of member
  /// resources in response to a GET request, but this HTML listing should not be returned from this method. Instead, if the resource
  /// logically has no data stream, like most collections, this method should return null.
  /// </summary>
  /// <remarks>The stream does not need to be seekable, but a seekable stream is preferred if one can be cheaply obtained. The stream
  /// must be closed by the caller when no longer needed. The stream does not need to be writable.
  /// </remarks>
  Stream OpenStream(WebDAVContext context);
}
#endregion

#region IStandardResource<T>
// NOTE: we split IStandardResource<T> into a generic and non-generic part so that people can access most methods without knowing the
// type parameter. if the assembly was compiled for .NET 4, we could avoid this by making T invariant, but we're using .NET 3.5...
/// <summary>Represents a WebDAV resource that uses standard processing for certain requests.</summary>
public interface IStandardResource<T> : IStandardResource where T : IStandardResource<T>
{
  /// <summary>Returns the children of this resource (if it's a collection resource), or null if it has no children.</summary>
  IEnumerable<T> GetChildren(WebDAVContext context);
}
#endregion

#region WebDAVResource
/// <summary>Implements an abstract class to simplify the implementation of <see cref="IWebDAVResource"/>.</summary>
/// <remarks>
/// For a read-only resource deriving from this class, the following properties and methods must be implemented:
/// <see cref="CanonicalPath"/>, <see cref="CopyOrMove"/>, <see cref="GetEntityMetadata"/>, <see cref="GetOrHead"/>, and
/// <see cref="PropFind"/>. In addition, the following methods may be of interest for certain types of read-only services.
/// <list type="table">
/// <listheader>
///   <term>Method</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="HandleGenericRequest"/></term>
///   <description>Your resource wants to handle custom HTTP methods (verbs).</description>
/// </item>
/// <item>
///   <term><see cref="Lock"/> and <see cref="Unlock"/></term>
///   <description>Your resource wants to support locking despite being read-only.</description>
/// </item>
/// <item>
///   <term><see cref="Options"/></term>
///   <description>
///   Your resource wants to report custom WebDAV extensions, support locking despite being read-only, or otherwise change the default
///   <see cref="OptionsRequest"/> response.
///   </description>
/// </item>
/// <item>
///   <term><see cref="Post"/></term>
///   <description>Your resource wants to handle <c>POST</c> requests.</description>
/// </item>
/// <item>
///   <term><see cref="PropPatch"/></term>
///   <description>Your resource supports setting any live properties.</description>
/// </item>
/// <item>
///   <term><see cref="ShouldDenyAccess"/></term>
///   <description>Your resource has custom access controls.</description>
/// </item>
/// </list>
/// <para>
/// If your resource is writable, then in addition to the methods listed above you should usually implement the following:
/// <see cref="Delete"/>, <see cref="Options"/> (to report support for HTTP write methods), and <see cref="Put"/> (for non-collection
/// resources). If you support locking, you must implement <see cref="Lock"/>, <see cref="Unlock"/>, as well as <see cref="Options"/> (to
/// report support for locking).
/// </para>
/// <note type="caution">If your resource may be used by more than one request (i.e. if <see cref="IWebDAVService.ResolveResource"/>
/// doesn't always allocate new resource objects), then the resource must be usable on multiple threads simultaneously. Before implementing
/// a WebDAV resource, it is strongly recommended that you be familiar with the WebDAV specification in RFC 4918 and the HTTP specification
/// in RFCs 7230 through 7235.
/// </note>
/// </remarks>
public abstract class WebDAVResource : IWebDAVResource
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  public abstract string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  /// <example>This example shows a typical implementation pattern for a read-only resource from a read-only service, or a read-only
  /// resource from a service that doesn't support optimized copy operations.
  /// <code>
  /// public override void CopyOrMove(CopyOrMoveRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(request.IsMove) request.Status = ConditionCodes.Forbidden;
  ///   else request.ProcessStandardRequest(this);
  /// }
  /// </code>
  /// This example shows a typical pattern of implementation for this method if your resource does not support any optimized copy,
  /// move, or rename operations. The resource will always be copied, and then on <c>MOVE</c> requests the source will be deleted if the
  /// copy is successful.
  /// <code>
  /// public override void CopyOrMove(CopyOrMoveRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly &amp;&amp; request.IsMove) request.Status = ConditionCodes.Forbidden; // disallow moving read-only resources
  ///   else request.ProcessStandardRequest(this);
  /// }
  /// </code>
  /// This example shows a typical pattern of implementation for resources that support optimized copies or moves. For more details, see
  /// the <see cref="IWebDAVService.CopyResource"/> method.
  /// <code>
  /// public override void CopyOrMove(CopyOrMoveRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   OurService destService = request.Destination.Service as OurService;
  ///   if(IsReadOnly &amp;&amp; request.IsMove || destService != null &amp;&amp; destService.IsReadOnly)
  ///   {
  ///     request.Status = ConditionCodes.Forbidden;
  ///   }
  ///   else
  ///   {
  ///     Func&lt;string, OurResource, ConditionCode&gt; createDest = null;
  ///     Func&lt;FileSystemResource, ConditionCode&gt; deleteSource = null;
  ///     if(destService != null) // if the destination is our service...
  ///     {
  ///       createDest = (path, sourceFile) => // create an optimized method for copying and moving resources to it
  ///       {
  ///         path = destService.CanonicalizePath(path);
  ///         
  ///         // overwrite the destination if we're allowed to
  ///         bool overwrote = false;
  ///         if(Exists(path))
  ///         {
  ///           if(!request.Overwrite) return ConditionCodes.PreconditionFailed;
  ///           Delete(path);
  ///           request.PostProcessOverwrite(path); // delete locks and dead properties on the destination
  ///           overwrote = true;
  ///         }
  ///
  ///         // then perform the copy or move
  ///         if(request.IsCopy) sourceFile.NonRecursiveCopyTo(path);
  ///         else sourceFile.NonRecursiveMoveTo(path);
  ///
  ///         // copy dead properties to the destination resource
  ///         request.PostProcessCopy(sourceFile.CanonicalPath, path);
  ///
  ///         return overwrote ? ConditionCodes.NoContent : ConditionCodes.Created;
  ///       };
  ///       
  ///       deleteSource = res =>
  ///       {
  ///         if(res.IsCollection) res.Delete(); // files will have already been moved if we use our custom createDest function
  ///         return null;
  ///       };
  ///     }
  ///
  ///     request.ProcessStandardRequest(this, deleteSource, createDest, null);
  ///   }
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="CopyOrMoveRequest"/> <seealso cref="IWebDAVService.CopyResource"/>
  public abstract void CopyOrMove(CopyOrMoveRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  /// <remarks><note type="inherit">
  /// The default implementation responds with 403 Forbidden, indicating that the resource does not support deletion.
  /// </note></remarks>
  /// <example>This example shows a typical pattern of implementation for this method when the resource can be deleted atomically
  /// (including any descendant resources).
  /// <code>
  /// public override void Delete(DeleteRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(!CanDelete) request.Status = ConditionCodes.Forbidden;
  ///   else request.ProcessStandardRequest(TryDelete, IsCollection); // TryDelete tries to delete the resource and returns a ConditionCode
  /// }
  /// </code>
  /// This example shows a typical pattern of implementation for this method when the resource cannot be deleted atomically (including any
  /// descendant resources).
  /// <code>
  /// public override void Delete(DeleteRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(!CanDelete) request.Status = ConditionCodes.Forbidden;
  ///   else request.ProcessStandardRequest(this);
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="DeleteRequest"/>
  public virtual void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support deletion.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  /// <example>This example shows a typical pattern of implementation for this method for a resource object that is not shared between
  /// requests. If the resource object is shared between requests, any changes should be made through a local variable and stored into the
  /// <c>metadata</c> field all at once to prevent other threads from seeing partially computed metadata.
  /// <code>
  /// public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  /// {
  ///   if(metadata == null) // if we haven't computed metadata for this request yet...
  ///   {
  ///     metadata = new EntityMetadata();
  ///     if(!IsCollection) // if it's not a collection, set file-related metadata
  ///     {
  ///       metadata.LastModifiedTime = LastWriteTimeUtc;
  ///       metadata.Length           = DataLength;
  ///       metadata.MediaType        = MediaTypes.GuessMediaType(Name);
  ///     }
  ///   }
  ///   if(includeEntityTag &amp;&amp; metadata.EntityTag == null &amp;&amp; !IsCollection) // if we need the entity tag but haven't computed it yet...
  ///   {
  ///     using(Stream stream = OpenStream()) metadata.EntityTag = DAVUtility.ComputeEntityTag(stream);
  ///   }
  ///   return metadata.Clone(); // clone the metadata to prevent callers from mutating cached data
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="EntityMetadata"/>
  public abstract EntityMetadata GetEntityMetadata(bool includeEntityTag);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  /// <example>This example shows a typical pattern of implementation for this method, assuming the resource implements
  /// <see cref="IStandardResource{T}"/>.
  /// <code>
  /// public override void GetOrHead(GetOrHeadRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   request.WriteStandardResponse(this);
  /// }
  /// </code>
  /// This example shows a typical pattern of implementation for a non-collection resource that does not implement
  /// <see cref="IStandardResource{T}"/>.
  /// <code>
  /// public override void GetOrHead(GetOrHeadRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   using(Stream stream = OpenStream()) request.WriteStandardResponse(stream, GetEntityMetadata(true));
  /// }
  /// </code>
  /// This example shows a typical pattern of implementation for a collection resource that does not implement
  /// <see cref="IStandardResource{T}"/>.
  /// <code>
  /// public override void GetOrHead(GetOrHeadRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   // convert each child resource into a GetOrHeadRequest.IndexItem and use them to write an index.html-like response
  ///   request.WriteSimpleIndexHtml(Children.Select(c =>
  ///   {
  ///     var item = new GetOrHeadRequest.IndexItem(c.PathSegment, c.Name, c.IsCollection);
  ///     item.LastModificationTime = c.LastModificationTime;
  ///     if(!c.IsCollection)
  ///     {
  ///       item.Size = c.Size;
  ///       string extension = Path.GetExtension(c.Name);
  ///       if(!string.IsNullOrEmpty(extension)) item.Type = extension;
  ///     }
  ///     return item;
  ///   }));
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="GetOrHeadRequest"/>
  public abstract void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  /// <remarks><note type="inherit">The default implementation does not handle any generic requests and always returns false.</note></remarks>
  public virtual bool HandleGenericRequest(WebDAVContext context)
  {
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 405 Method Not Allowed if locking is not enabled, and 403
  /// Forbidden otherwise, indicating that the resource cannot be locked.
  /// </note></remarks>
  /// <example>This example shows a typical pattern of implementation for this method.
  /// <code>
  /// public override void Lock(LockRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.Lock(request); // call the base class, which denies the request
  ///   else request.ProcessStandardRequest(LockType.WriteLocks, IsCollection);
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="LockRequest"/> <seealso cref="ILockManager"/>
  public virtual void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource cannot be locked.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  /// <remarks><note type="inherit">The default implementation returns options suitable for read-only access to the resource, including the
  /// use of <c>GET</c>, <c>PROPFIND</c>, and <c>PROPPATCH</c> methods, but excluding support for locking or writing.
  /// </note></remarks>
  /// <example>This example shows the basic implementation pattern for this method.
  /// <code>
  /// public override void Options(OptionsRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(!IsReadOnly) // the defaults are usually sufficient for read-only resources
  ///   {
  ///     if(CanDelete) request.AllowedMethods.Add(DAVMethods.Delete);
  ///     if(!IsCollection) request.AllowedMethods.Add(DAVMethods.Put); // usually only files can have their content replaced
  ///     request.SupportsLocking = request.Context.LockManager != null; // enable locking if there's a lock manager
  ///   }
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="OptionsRequest"/>
  public virtual void Options(OptionsRequest request) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Post/node()" />
  /// <remarks><note type="inherit">The default implementation replies with 405 Method Not Allowed.</note></remarks>
  /// <seealso cref="PostRequest"/>
  public virtual void Post(PostRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.MethodNotAllowed;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  /// <example>This example shows a typical implementation pattern for this method when the resource implements
  /// <see cref="IStandardResource{T}"/>.
  /// <code>
  /// public override void PropFind(PropFindRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   // see the documentation for IStandardResource.GetLiveProperties for a definition of GetLiveProperties(PropFindRequest)
  ///   request.ProcessStandardRequest(this, resource => resource.GetLiveProperties(request));
  /// }
  /// </code>
  /// This example shows a typical implementation pattern for this method when the resource is a non-collection resource that does not
  /// implement <see cref="IStandardResource{T}"/>.
  /// <code>
  /// public override void PropFind(PropFindRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   // see the documentation for IStandardResource.GetLiveProperties for a definition of GetLiveProperties(PropFindRequest)
  ///   request.ProcessStandardRequest(GetLiveProperties(request));
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="PropFindRequest"/> <seealso cref="IStandardResource.GetLiveProperties"/>
  public abstract void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  /// <remarks>
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatchRemarks/remarks/node()" />
  /// <note type="inherit">The default implementation allows the setting of dead properties outside the <c>DAV:</c> namespace.</note>
  /// </remarks>
  /// <example>This example shows how this method might be implemented if you want to allow setting <c>DAV:creationdate</c> and
  /// <c>DAV:getlastmodified</c>. If you only want to allow the setting of dead properties, you do not need to override the default
  /// implementation.
  /// <code>
  /// public override void PropPatch(PropPatchRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   // allow DAV:creationdate and DAV:getlastmodified to be set, plus dead properties, but no others
  ///   request.ProcessStandardRequest(
  ///     (name,value) => // canSetProperty: determines whether a property can be set, including whether the set would succeed
  ///     {
  ///       return !DAVUtility.IsDAVName(name) ? null : // if it's not a DAV: property, treat it as a dead property
  ///              name.Name == DAVNames.creationdate.Name || name.Name == DAVNames.getlastmodified.Name ? ConditionCodes.OK : // assume success
  ///              ConditionCodes.Forbidden; // don't allow other DAV: properties to be set
  ///     },
  ///     null, // canRemoveProperty: can't remove any DAV: properties
  ///     (name,value) => // setProperty: sets the property. if canSetProperty returned success earlier, this function should succeed
  ///     {
  ///       if(!DAVUtility.IsDAVName(name)) return null; // if it's not a DAV: property, then it's a dead property
  ///       if(name.Name == DAVNames.creationdate.Name) SetCreatedDate(value.Property.Value);
  ///       else if(name.Name == DAVNames.getlastmodified.Name) SetModifiedDate(value.Property.Value);
  ///       else return ConditionCodes.Forbidden;
  ///       return ConditionCodes.OK; // return OK for successful changes according to RFC 4918 section 9.2.1
  ///     },
  ///     null); // removeProperty: can't remove any DAV: properties
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="PropPatchRequest"/>
  public virtual void PropPatch(PropPatchRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.ProcessStandardRequest(CanonicalPath);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 403 Forbidden, indicating that the resource not support
  /// setting its content.
  /// </note></remarks>
  /// <example>This example shows a typical implementation pattern for this method.
  /// <code>
  /// public override void Put(PutRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly || IsCollection) // don't allow setting the content of directories or read-only resources
  ///   {
  ///     base.Put(request); // call the base class, which denies the request
  ///   }
  ///   else // this is a writable non-collection resource
  ///   {
  ///     using(Stream stream = OpenStream()) // open the stream for writing
  ///     {
  ///       request.ProcessStandardRequest(stream); // update it with data from the client
  ///       if(DAVUtility.IsSuccess(request.Status)) // if the request succeeded, send the new ETag and Last-Modified values to the client
  ///       {
  ///         request.Context.Response.Headers[DAVHeaders.ETag] = ComputeEntityTag(stream, true).ToHeaderString();
  ///         request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(GetLastModifiedDate(stream));
  ///       }
  ///     }
  ///   }
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="PutRequest"/>
  public virtual void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support setting its content.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  /// <remarks><note type="inherit">The default implementation responds with 405 Method Not Allowed if locking is not enabled, and 409
  /// Conflict otherwise, indicating that the resource is not locked.
  /// </note></remarks>
  /// <example>This example shows a typical implementation pattern for this method.
  /// <code>
  /// public override void Unlock(UnlockRequest request)
  /// {
  ///   if(request == null) throw new ArgumentNullException();
  ///   if(IsReadOnly) base.Unlock(request); // disallow locking read-only resources
  ///   else request.ProcessStandardRequest();
  /// }
  /// </code>
  /// </example>
  /// <seealso cref="UnlockRequest"/> <seealso cref="ILockManager"/>
  public virtual void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Conflict, "The resource is not locked.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/ShouldDenyAccess/node()" />
  /// <remarks><note type="inherit">The default implementation always grants access to the resource.</note></remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, XmlQualifiedName access, out ConditionCode response)
  {
    response = null;
    return false;
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
