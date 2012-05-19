namespace HiA.WebDAV.Server
{

/// <summary>Represents an object that can deny the requesting user access to execute the request.</summary>
public interface ISupportAuthorization
{
  /// <include file="documentation.xml" path="/DAV/ISupportAuthorization/ShouldDenyAccess/node()" />
  bool ShouldDenyAccess(WebDAVContext context, out bool denyExistence);
}

// TODO: should an authorization filter be able to filter children/descendants on recursive queries as well?
/// <summary>Represents a filter that can deny the requesting user access to execute the request, even the user would otherwise be allowed.</summary>
/// <remarks>Note that the <see cref="WebDAVContext.RequestResource"/> property may be null when the filter is invoked, if the request was
/// made to an unmapped URL.
/// </remarks>
public interface IAuthorizationFilter : ISupportAuthorization
{
  /// <summary>Gets whether the authorization filter is safe for reuse across multiple requests. In order to be safe, a filter must be
  /// able to be accessed simultaneously by multiple threads, and should not fail in a way that would affect its operation on later
  /// requests.
  /// </summary>
  bool IsReusable { get; }
}

} // namespace HiA.WebDAV.Server
