// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>PUT</c> request.</summary>
/// <remarks>The <c>PUT</c> request is described in section 9.7 of RFC 4918.</remarks>
public class PutRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PutRequest"/> based on a new WebDAV request.</summary>
  public PutRequest(WebDAVContext context) : base(context) { }
}

} // namespace HiA.WebDAV.Server
