using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Collections.Generic;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>DELETE</c> request.</summary>
/// <remarks>The <c>DELETE</c> request is described in section 9.6 of RFC 4918.</remarks>
public class DeleteRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="DeleteRequest"/> based on a new WebDAV request.</summary>
  public DeleteRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // see ParseRequest() for details
    FailedMembers = new FailedMemberCollection();
  }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be deleted, if the resource is a collection resource.
  /// </summary>
  public FailedMemberCollection FailedMembers { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // require recursive DELETE requests, as per section 9.6.1 of RFC 4918
    if(Depth != Depth.SelfAndDescendants)
    {
      throw Exceptions.BadRequest("The Depth header must be infinity or unspecified for DELETE requests.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty, and outputs a
  /// response based on <see cref="WebDAVRequest.Status"/> otherwise.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    else Context.WriteFailedMembers(FailedMembers);
  }
}

} // namespace HiA.WebDAV.Server
