/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2015 by Adam Milazzo.

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

// TODO: add support for extended MKCOL requests as described in RFC 5689. also update OPTIONS to report support for it

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>MKCOL</c> request.</summary>
/// <remarks>
/// <para>The <c>MKCOL</c> request is described in section 9.3 of RFC 4918 and in RFC 5689. To service a <c>MKCOL</c> request, you can
/// normally just call the <see cref="ProcessStandardRequest(Func{ConditionCode})"/> method or one of its overrides.
/// </para>
/// <para>If you would like to handle it yourself, you should create a new collection resource at the request URI and return a 201 Created
/// response. The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>201 <see cref="ConditionCodes.Created"/> (default)</term>
///   <description>The collection was successfully created. Usually no response body is included, although you can send one if the client
///     can understand it. This is the default status code returned when <see cref="WebDAVRequest.Status"/> is null.
///   </description>
/// </item>
/// <item>
///   <term>202 <see cref="ConditionCodes.Accepted"/></term>
///   <description>The request was processed, is valid, and will succeed (barring exceptional circumstances), but the execution of the
///     request has been deferred until later.
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to create a new collection, or the server refuses to create the resource for some
///     other reason.
///   </description>
/// </item>
/// <item>
///   <term>405 <see cref="ConditionCodes.MethodNotAllowed">Method Not Allowed</see></term>
///   <description>A resource already exists at the request URI.</description>
/// </item>
/// <item>
///   <term>409 <see cref="ConditionCodes.Conflict"/></term>
///   <description>The resource could not be created because the parent collection does not exist. The parent collection must not be
///     created automatically.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A conditional request was not executed because the condition wasn't true.</description>
/// </item>
/// <item>
///   <term>415 <see cref="ConditionCodes.UnsupportedMediaType">Unsupported Media Type</see></term>
///   <description>The client submitted a request body that the server doesn't know how to process.</description>
/// </item>
/// <item>
///   <term>423 <see cref="ConditionCodes.Locked"/></term>
///   <description>The resource URI was locked, and no valid lock token was submitted. You should include the
///     <c>DAV:lock-token-submitted</c> precondition code in the response.
///   </description>
/// </item>
/// <item>
///   <term>507 <see cref="ConditionCodes.InsufficientStorage">Insufficient Storage</see></term>
///   <description>The collection could not be created because there was insufficient storage space.</description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="ProcessStandardRequest(string,Func{ConditionCode})"/></term>
///   <description>You want to change the standard request processing.</description>
/// </item>
/// </list>
/// </remarks>
public class MkColRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="MkColRequest"/> based on a new WebDAV request.</summary>
  public MkColRequest(WebDAVContext context) : base(context) { }

  /// <summary>Processes a standard <c>MKCOL</c> request to create a new collection at the <see cref="WebDAVContext.RequestPath"/>.</summary>
  /// <param name="createCollection">A function that should create the collection and return a <see cref="ConditionCode"/> indicating
  /// whether the attempt succeeded or failed, or null for a standard success code.
  /// </param>
  public void ProcessStandardRequest(Func<ConditionCode> createCollection)
  {
    ProcessStandardRequest(null, createCollection);
  }

  /// <summary>Processes a standard <c>MKCOL</c> request to create a new collection at the given canonical path.</summary>
  /// <param name="canonicalPath">The canonical path of the location where the directory will be created. If null, the result of calling
  /// <see cref="IWebDAVService.GetCanonicalUnmappedPath"/> on the <see cref="WebDAVContext.RequestPath"/> will be used.
  /// </param>
  /// <param name="createCollection">A function that should create the collection and return a <see cref="ConditionCode"/> indicating
  /// whether the attempt succeeded or failed, or null for a standard success code.
  /// </param>
  public virtual void ProcessStandardRequest(string canonicalPath, Func<ConditionCode> createCollection)
  {
    if(createCollection == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null, canonicalPath);
    Status = precondition ?? // if the client submitted a body, reply with 415 Unsupported Media Type as per RFC 4918 section 9.3
      (Context.Request.InputStream.Length != 0 ? ConditionCodes.UnsupportedMediaType : DAVUtility.TryExecute(createCollection));
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks><include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensRemarks/remarks/node()" />
  /// <note type="inherit">This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources
  /// because a new collection is assumed to not contain any mapped member URLs.
  /// </note></remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, true, false);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks><note type="inherit">This implementation sets the response status based on <see cref="WebDAVRequest.Status"/>, using
  /// <see cref="ConditionCodes.Created"/> if the status is null.
  /// </note></remarks>
  protected internal override void WriteResponse()
  {
    Context.WriteStatusResponse(Status ?? ConditionCodes.Created);
  }
}

} // namespace AdamMil.WebDAV.Server
