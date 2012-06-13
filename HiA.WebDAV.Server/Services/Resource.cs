using System;
using System.Net;

namespace HiA.WebDAV.Server
{

#region IWebDAVResource
/// <summary>Represents a DAV-compliant resource.</summary>
/// <remarks>Before implementing a WebDAV resource, it is strongly recommended that be familiar with the WebDAV specification as outlined
/// in RFC 4918 and the HTTP specification as outline in RFC 2616.
/// </remarks>
public interface IWebDAVResource : ISupportAuthorization
{
  // TODO: we no longer really use the CanonicalPath within the core system (having switched to using the RequestPath instead). if we don't
  // find a new use for it (e.g. adding it to a Location header), we should remove the property from the interface
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  void CopyOrMove(CopyOrMoveRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  void Delete(DeleteRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  bool HandleGenericRequest(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  void Options(OptionsRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  void PropPatch(PropPatchRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  void Put(PutRequest request);
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
    request.Status = new ConditionCode((int)HttpStatusCode.Forbidden, "This resource does not support being copied or moved.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  /// <remarks>The default implementation responds with 403 Forbidden, indicating that the resource does not support deletion.</remarks>
  public virtual void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode((int)HttpStatusCode.Forbidden, "This resource does not support deletion.");
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public abstract void GetOrHead(GetOrHeadRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/HandleGenericRequest/node()" />
  /// <remarks>The default implementation does not handle any generic requests.</remarks>
  public virtual bool HandleGenericRequest(WebDAVContext context)
  {
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  /// <remarks>The default implementation returns options suitable for read-only access to the resource, including the use of <c>GET</c>
  /// and <c>PROPFIND</c> methods, but excluding support for locking or writing.
  /// </remarks>
  public virtual void Options(OptionsRequest request)
  {
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public abstract void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  /// <remarks>The default implementation disallows the setting of any properties.</remarks>
  public virtual void PropPatch(PropPatchRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    // decline to set any property
    ConditionCode errorStatus = new ConditionCode((int)HttpStatusCode.Forbidden, "This resource does not support setting properties.");
    foreach(PropertyPatch patch in request.Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove) removal.Status = errorStatus;
      foreach(PropertyPatchValue value in patch.Set.Values) value.Status = errorStatus;
    }
  }

  /// <include file="documentation.xml" path="/DAV/ISupportAuthorization/ShouldDenyAccess/node()" />
  /// <remarks>The default implementation always grants access to the resource.</remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, out bool denyExistence)
  {
    denyExistence = false;
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  /// <remarks>The default implementation responds with 403 Forbidden, indicating that the resource not support setting its content.</remarks>
  public virtual void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = new ConditionCode((int)HttpStatusCode.Forbidden, "This resource does not support setting its content.");
  }
}
#endregion

} // namespace HiA.WebDAV.Server
