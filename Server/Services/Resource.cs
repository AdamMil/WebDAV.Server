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

namespace AdamMil.WebDAV.Server
{

#region IWebDAVResource
/// <summary>Represents a DAV-compliant resource.</summary>
/// <remarks>Before implementing a WebDAV resource, it is strongly recommended that be familiar with the WebDAV specification as outlined
/// in RFC 4918 and the HTTP specification as outlined in RFCs 7230 through 7235.
/// </remarks>
public interface IWebDAVResource
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  void CopyOrMove(CopyOrMoveRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  void Delete(DeleteRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  EntityMetadata GetEntityMetadata(bool includeEntityTag);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  bool HandleGenericRequest(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  void Lock(LockRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  void Options(OptionsRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Post/node()" />
  void Post(PostRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  void PropPatch(PropPatchRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  void Put(PutRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/ShouldDenyAccess/node()" />
  bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, out bool denyExistence);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  void Unlock(UnlockRequest request);
}
#endregion

#region WebDAVResource
/// <summary>Implements an abstract class to simplify the implementation of <see cref="IWebDAVResource"/>.</summary>
/// <remarks>Before implementing a WebDAV resource, it is strongly recommended that be familiar with the WebDAV specification as outlined
/// in RFC 4918.
/// </remarks>
public abstract class WebDAVResource : IWebDAVResource
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  public abstract string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  /// <remarks>The default implementation responds with 403 Forbidden, indicating that the resource does not support being copied or moved.</remarks>
  public virtual void CopyOrMove(CopyOrMoveRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support being copied or moved.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  /// <remarks>The default implementation responds with 403 Forbidden, indicating that the resource does not support deletion.</remarks>
  public virtual void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support deletion.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  public abstract EntityMetadata GetEntityMetadata(bool includeEntityTag);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public abstract void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  /// <remarks>The default implementation does not handle any generic requests.</remarks>
  public virtual bool HandleGenericRequest(WebDAVContext context)
  {
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  /// <remarks>The default implementation responds with 405 Method Not Allowed if locking is not enabled, and 403 Forbidden otherwise,
  /// indicating that the resource cannot be locked.
  /// </remarks>
  public virtual void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource cannot be locked.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  /// <remarks>The default implementation returns options suitable for read-only access to the resource, including the use of <c>GET</c>
  /// and <c>PROPFIND</c> methods, but excluding support for locking or writing.
  /// </remarks>
  public virtual void Options(OptionsRequest request)
  {
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Post/node()" />
  /// <remarks>The default implementation replies with 405 Method Not Allowed.</remarks>
  public virtual void Post(PostRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.MethodNotAllowed;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public abstract void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  /// <remarks>The default implementation disallows the setting of any properties.</remarks>
  public virtual void PropPatch(PropPatchRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    // decline to set any property
    ConditionCode errorStatus = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support setting properties.");
    foreach(PropertyPatch patch in request.Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove) removal.Status = errorStatus;
      foreach(PropertyPatchValue value in patch.Set.Values) value.Status = errorStatus;
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  /// <remarks>The default implementation responds with 403 Forbidden, indicating that the resource not support setting its content.</remarks>
  public virtual void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode(HttpStatusCode.Forbidden, "This resource does not support setting its content.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  /// <remarks>The default implementation responds with 405 Method Not Allowed if locking is not enabled, and 409 Conflict otherwise,
  /// indicating that the resource is not locked.
  /// </remarks>
  public virtual void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Context.LockManager == null) request.Status = ConditionCodes.MethodNotAllowed;
    else request.Status = new ConditionCode(HttpStatusCode.Conflict, "The resource is not locked.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/ShouldDenyAccess/node()" />
  /// <remarks>The default implementation always grants access to the resource.</remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, out bool denyExistence)
  {
    denyExistence = false;
    return false;
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
