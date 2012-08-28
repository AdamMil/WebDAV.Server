// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>POST</c> request.</summary>
/// <remarks>The <c>POST</c> request is described in section 9.5 of RFC 4918 and section 9.5 of RFC 2616.</remarks>
public class PostRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PostRequest"/> based on a new WebDAV request.</summary>
  public PostRequest(WebDAVContext context) : base(context) { }
}

} // namespace HiA.WebDAV.Server
