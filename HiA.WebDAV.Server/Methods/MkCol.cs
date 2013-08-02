/*
 * Version: EUPL 1.1
 * 
 * The contents of this file are subject to the European Union Public Licence Version 1.1 (the "Licence"); 
 * you may not use this file except in compliance with the Licence. 
 * You may obtain a copy of the Licence at:
 * http://joinup.ec.europa.eu/software/page/eupl/licence-eupl
 */
// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>MKCOL</c> request.</summary>
/// <remarks>The <c>MKCOL</c> request is described in section 9.3 of RFC 4918.</remarks>
public class MkColRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="MkColRequest"/> based on a new WebDAV request.</summary>
  public MkColRequest(WebDAVContext context) : base(context) { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources because a new
  /// collection is assumed to not contain any mapped member URLs.
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, true, false);
  }

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
