﻿/*
 * Version: EUPL 1.1
 * 
 * The contents of this file are subject to the European Union Public Licence Version 1.1 (the "Licence"); 
 * you may not use this file except in compliance with the Licence. 
 * You may obtain a copy of the Licence at:
 * http://joinup.ec.europa.eu/software/page/eupl/licence-eupl
 */
using System;

// TODO: facilitate cross-service copies where either the source or destination can handle it. for instance, we may want the FlairPoint
// service to allow files to be copied to/from the filesystem service. the logic would be implemented in the FlairPoint service regardless
// of whether it was the source or destination

// TODO: implement CheckSubmittedLockTokens()

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>COPY</c> or <c>MOVE</c> request.</summary>
/// <remarks>The <c>COPY</c> and <c>MOVE</c> requests are described in sections 9.8 and 9.9 of RFC 4918.</remarks>
public class CopyOrMoveRequest : WebDAVRequest, IDisposable
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
    if(!DAVUtility.TryParseSimpleRef(value, out destination))
    {
      throw Exceptions.BadRequest("The Destination header was not a valid absolute URI or absolute path.");
    }
    Destination = destination;

    // resolve the destination URL to see which service it's under (if any), and see if we can resolve it to a specific resource there
    IWebDAVService destService;
    IWebDAVResource destResource;
    string destServiceRoot, destPath;
    if(WebDAVModule.ResolveUri(context, destination, false, out destService, out destResource, out destServiceRoot, out destPath))
    {
      DestinationResource = destResource;
    }
    // destService and destServiceRoot may be set even if ResolveLocation returns false
    if(destService != null) DestinationService = destServiceRoot.OrdinalEquals(Context.ServiceRoot) ? Context.Service : destService;
    DestinationPath = destPath;
  }

  /// <summary>Gets the absolute URI submitted by the client in the <c>Destination</c> header. The URI may point to a location outside the
  /// WebDAV service root, and may even point a location on another server. If you only support copies and moves within the same WebDAV
  /// service, then it is easier to use <see cref="DestinationPath"/> instead.
  /// </summary>
  public Uri Destination { get; private set; }

  /// <summary>Gets the destination path, relative to the root of the <see cref="DestinationService"/>, if the <see cref="Destination"/>
  /// could be resolved to a service within the WebDAV server.
  /// </summary>
  public string DestinationPath { get; private set; }

  /// <summary>Gets the destination resource, if the <see cref="DestinationPath"/> could be resolved to a specific resource within the
  /// <see cref="DestinationService"/>.
  /// </summary>
  public IWebDAVResource DestinationResource { get; private set; }

  /// <summary>Gets the destination service, if the <see cref="Destination"/> could be resolved to a specific service within the
  /// WebDAV server. If equal to <see cref="WebDAVContext.Service"/>, the copy or move is within the same service. Otherwise, it's a
  /// cross-service operation. If null, the <see cref="Destination"/> does not correspond to any WebDAV service within the server.
  /// </summary>
  public IWebDAVService DestinationService { get; private set; }

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
  /// resources.) If null, application-specific default behavior should be applied.
  /// </summary>
  public bool? Overwrite { get; private set; }

  /// <inheritdoc/>
  public void Dispose()
  {
    Dispose(true);
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

} // namespace HiA.WebDAV.Server
