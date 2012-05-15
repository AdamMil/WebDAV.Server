namespace HiA.WebDAV
{

#region IWebDAVService
/// <summary>Represents a WebDAV service that serves a subtree within a URL namespace.</summary>
public interface IWebDAVService
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVService/IsReusable/node()" />
  bool IsReusable { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropFind/node()" />
  PropFindRequest CreatePropFind(WebDAVContext context);

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  IWebDAVResource ResolveResource(WebDAVContext context);
}
#endregion

#region WebDAVService
/// <summary>Provides a base class to simplify the implementation of <see cref="IWebDAVService"/>.</summary>
public abstract class WebDAVService : IWebDAVService
{
  /// <include file="documentation.xml" path="/DAV/IWebDAVService/IsReusable/node()" />
  public abstract bool IsReusable { get; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreatePropFind/node()" />
  /// <remarks>The default implementation returns a new <see cref="PropFindRequest"/>.</remarks>
  public virtual PropFindRequest CreatePropFind(WebDAVContext context)
  {
    return new PropFindRequest(context);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public abstract IWebDAVResource ResolveResource(WebDAVContext context);
}
#endregion

} // namespace HiA.WebDAV