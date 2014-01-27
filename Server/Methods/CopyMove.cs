/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2013 by Adam Milazzo.

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/
using System;
using AdamMil.Utilities;

// TODO: facilitate cross-service copies where either the source or destination can handle it. for instance, we may want the FlairPoint
// service to allow files to be copied to/from the filesystem service. the logic would be implemented in the FlairPoint service regardless
// of whether it was the source or destination
// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>COPY</c> or <c>MOVE</c> request.</summary>
/// <remarks>The <c>COPY</c> and <c>MOVE</c> requests are described in sections 9.8 and 9.9 of RFC 4918.</remarks>
public class CopyOrMoveRequest : WebDAVRequest, IDisposable
{
  /// <summary>Initializes a new <see cref="CopyOrMoveRequest"/> based on a new WebDAV request.</summary>
  public CopyOrMoveRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // RFC 4918 sections 9.8.3 and 9.9.3 require recursion as the default

    // parse the Overwrite header. (if it's missing, it must be treated as true, according to RFC 4918 section 10.6
    Overwrite = !string.Equals(context.Request.Headers[HttpHeaders.Overwrite], "F", StringComparison.OrdinalIgnoreCase);

    // parse the Destination header
    string value = context.Request.Headers[HttpHeaders.Destination];
    if(string.IsNullOrEmpty(value)) throw Exceptions.BadRequest("The Destination header was missing.");
    Uri destination;
    if(!DAVUtility.TryParseSimpleRef(value, out destination))
    {
      throw Exceptions.BadRequest("The Destination header was not a valid absolute URI or absolute path.");
    }
    Destination = destination;

    // resolve the destination URL to see which service it's under (if any), and see if we can resolve it to a specific resource there
    IWebDAVService destService;
    IWebDAVResource destResource;
    string destServiceRoot, destPath;
    if(WebDAVModule.ResolveUri(context, destination, out destService, out destResource, out destServiceRoot, out destPath))
    {
      DestinationResource = destResource;
    }
    // destService and destServiceRoot may be set even if ResolveLocation returns false
    if(destService != null)
    {
      DestinationPath        = destPath;
      DestinationService     = destServiceRoot.OrdinalEquals(Context.ServiceRoot) ? Context.Service : destService;
      DestinationServiceRoot = destServiceRoot;
      if(destService != DestinationService && !destService.IsReusable) Utility.Dispose(destService);
    }
  }

  /// <summary>Gets the absolute URI submitted by the client in the <c>Destination</c> header. The URI may point to a location outside the
  /// WebDAV service root, and may even point a location on another server. If you only support copies and moves within the same WebDAV
  /// server, then it is easier to use <see cref="DestinationService"/> and <see cref="DestinationPath"/> instead.
  /// </summary>
  public Uri Destination { get; private set; }

  /// <summary>Gets the destination path, relative to <see cref="DestinationServiceRoot"/>, if the <see cref="Destination"/> could be
  /// resolved to a service within the WebDAV server.
  /// </summary>
  public string DestinationPath { get; private set; }

  /// <summary>Gets the destination resource, if the <see cref="DestinationPath"/> could be resolved to a specific resource within the
  /// <see cref="DestinationService"/>. Access checks will not have been performed against the destination resource, so you should
  /// perform them yourself.
  /// </summary>
  public IWebDAVResource DestinationResource { get; private set; }

  /// <summary>Gets the destination service, if the <see cref="Destination"/> could be resolved to a specific service within the
  /// WebDAV server. If equal to <see cref="WebDAVContext.Service"/>, the copy or move is within the same service. Otherwise, it's a
  /// cross-service operation. If null, the <see cref="Destination"/> does not correspond to any WebDAV service within the server.
  /// </summary>
  public IWebDAVService DestinationService { get; private set; }

  /// <summary>Gets the root of the <see cref="DestinationService"/>, if the <see cref="Destination"/> could be resolved to a specific
  /// service within the WebDAV server.
  /// </summary>
  public string DestinationServiceRoot { get; private set; }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing the members of the collection
  /// that could not be copied or moved, if the source resource is a collection resource.
  /// </summary>
  public FailedResourceCollection FailedMembers { get; private set; }

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
  /// resources.)
  /// </summary>
  public bool Overwrite { get; private set; }

  /// <inheritdoc/>
  public void Dispose()
  {
    Dispose(true);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    // check the source if we're moving it, as well as the destination
    ConditionCode code = IsMove ? CheckSubmittedLockTokens(LockType.ExclusiveWrite, IsMove, Depth != Depth.Self) : null;
    if(code == null && DestinationService != null)
    {
      code = CheckSubmittedLockTokens(LockType.ExclusiveWrite, true, true, DestinationServiceRoot + DestinationPath, DestinationService);
    }
    return code;
  }

  /// <summary>Called to dispose the request.</summary>
  protected virtual void Dispose(bool manualDispose)
  {
    if(DestinationService != null && !DestinationService.IsReusable) Utility.Dispose(DestinationService);
  }

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

} // namespace AdamMil.WebDAV.Server
