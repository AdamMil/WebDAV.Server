namespace HiA.WebDAV
{

#region IWebDAVResource
/// <summary>Represents a DAV-compliant resource.</summary>
/// <remarks>Before implementing a WebDAV resource, it is strongly recommended that be familiar with the WebDAV specification as outlined
/// in RFC 4918.
/// </remarks>
public interface IWebDAVResource : ISupportAuthorization
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  void PropPatch(PropPatchRequest request);
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

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public abstract void PropFind(PropFindRequest request);

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  public abstract void PropPatch(PropPatchRequest request);

  /// <include file="documentation.xml" path="/DAV/ISupportAuthorization/ShouldDenyAccess/node()" />
  /// <remarks>The default implementation always grants access to the resource.</remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, out bool denyExistence)
  {
    denyExistence = false;
    return false;
  }
}
#endregion

} // namespace HiA.WebDAV