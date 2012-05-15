namespace HiA.WebDAV
{

#region IWebDAVResource
/// <summary>Represents a DAV-compliant resource.</summary>
public interface IWebDAVResource : ISupportAuthorization
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  string CanonicalPath { get; }
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  void PropFind(PropFindRequest request);
}
#endregion

#region WebDAVResource
/// <summary>Implements an abstract class to simplify the implementation of <see cref="IWebDAVResource"/>.</summary>
public abstract class WebDAVResource : IWebDAVResource
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  public abstract string CanonicalPath { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public abstract void PropFind(PropFindRequest request);

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