using System;

// TODO: facilitate cross-service copies where either the source or destination can handle it. for instance, we may want the FlairPoint
// service to allow files to be copied to/from the filesystem service. the logic would be implemented in the FlairPoint service regardless
// of whether it was the source or destination

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>COPY</c> or <c>MOVE</c> request.</summary>
/// <remarks>The <c>COPY</c> and <c>MOVE</c> requests are described in sections 9.8 and 9.9 of RFC 4918.</remarks>
public class CopyOrMoveRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="CopyOrMoveRequest"/> based on a new WebDAV request.</summary>
  public CopyOrMoveRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // RFC 4918 sections 9.8.3 and 9.9.3 require recursion as the default

    // parse the Overwrite header
    string value = context.Request.Headers[HttpHeaders.Overwrite];
    if(string.Equals(value, "F", StringComparison.OrdinalIgnoreCase)) Overwrite = false;
    else if(string.Equals(value, "T", StringComparison.OrdinalIgnoreCase)) Overwrite = true;

    // parse the Destination header
    value = context.Request.Headers[HttpHeaders.Destination];
    if(string.IsNullOrEmpty(value)) throw Exceptions.BadRequest("The Destination header was missing.");
    Uri destination;
    if(!Uri.TryCreate(value, UriKind.Absolute, out destination))
    {
      throw Exceptions.BadRequest("The Destination header was not a valid absolute URI.");
    }
    Destination = destination;

    // resolve the destination URL to see which service it's under (if any), and if it's under the same service then save the relative path
    string serviceRoot, relativePath;
    if(WebDAVModule.ResolveLocation(context.Request, destination, out serviceRoot, out relativePath) &&
       serviceRoot.OrdinalEquals(context.ServiceRoot))
    {
      DestinationPath = relativePath;
    }
  }

  /// <summary>Gets the absolute URI submitted by the client in the <c>Destination</c> header. The URI may point to a location outside the
  /// WebDAV service root, and may even point a location on another server. If you only support copies and moves within the same WebDAV
  /// service, then it is easier to use <see cref="DestinationPath"/> instead.
  /// </summary>
  public Uri Destination { get; private set; }

  /// <summary>Gets the destination path, relative to <see cref="WebDAVContext.ServiceRoot"/>, if the <see cref="Destination"/> points to a
  /// location within the same WebDAV service that contains the source resource. If the <see cref="Destination"/> points to a location
  /// outside the source WebDAV service, this property will be null and you will have to examine the absolute <see cref="Destination"/> URI
  /// if you support out-of-service copies.
  /// </summary>
  public string DestinationPath { get; private set; }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be copied or moved, if the source resource is a collection resource.
  /// </summary>
  public FailedMemberCollection FailedMembers { get; private set; }

  /// <summary>Gets whether the request is a <c>COPY</c> request.</summary>
  /// <remarks><c>COPY</c> requests must be processed in accordance with RFC 4918 section 9.8.</remarks>
  public bool IsCopy
  {
    get { return Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Copy); }
  }

  /// <summary>Gets whether the request is a <c>MOVE</c> request.</summary>
  /// <remarks><c>MOVE</c> requests must be processed in accordance with RFC 4918 section 9.9.</remarks>
  public bool IsMove
  {
    get { return Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Move); }
  }

  /// <summary>Gets whether overwriting mapped resources is allowed. If true, resources at the destination should be overwritten. (During
  /// a move, they must be overwritten as if they had been deleted first. In particular, all properties must be reset.) If false, 
  /// existing resources at the destination must not be overwritten. (412 Precondition Failed responses should be returned for those
  /// resources.) If null, application-specific default behavior should be applied.
  /// </summary>
  public bool? Overwrite { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // SelfAndChildren doesn't seem useful, and IsMove requires SelfAndDescendants (RFC 4918 section 9.9.3)
    if(Depth == Depth.SelfAndChildren || IsMove && Depth != Depth.SelfAndDescendants)
    {
      throw Exceptions.BadRequest("The Depth header must be 0 or infinity for COPY requests and infinity for MOVE requests, " +
                                  "or unspecified.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty, and outputs a
  /// response based on <see cref="WebDAVRequest.Status"/> otherwise, using 204 No Content if <see cref="WebDAVRequest.Status"/> is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    else Context.WriteFailedMembers(FailedMembers);
  }
}

} // namespace HiA.WebDAV.Server
