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
/// <remarks>The <c>MKCOL</c> request is described in section 9.3 of RFC 4918 and in RFC 5689.</remarks>
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
  /// <see cref="IWebDAVService.GetCanonicalPath"/> on the <see cref="WebDAVContext.RequestPath"/> will be used.
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
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources because a new
  /// collection is assumed to not contain any mapped member URLs.
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, true, false);
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

} // namespace AdamMil.WebDAV.Server
