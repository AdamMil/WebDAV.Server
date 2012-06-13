// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>MKCOL</c> request.</summary>
/// <remarks>The <c>MKCOL</c> request is described in section 9.3 of RFC 4918.</remarks>
public class MkColRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="MkColRequest"/> based on a new WebDAV request.</summary>
  public MkColRequest(WebDAVContext context) : base(context) { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation sets the response status based on <see cref="WebDAVRequest.Status"/>, using
  /// <see cref="ConditionCodes.Created"/> if the status is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    Context.WriteStatusResponse(Status ?? ConditionCodes.Created);
  }
}

} // namespace HiA.WebDAV.Server
