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
using System.IO;
using AdamMil.IO;
using AdamMil.Utilities;

// TODO: should the default Status depend on whether Context.RequestResource is null? ditto for LOCK

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>PUT</c> request.</summary>
/// <remarks>
/// <para>The <c>PUT</c> request is described in section 4.3.4 of RFC 7231 and section 9.7 of RFC 4918. To service a <c>PUT</c> request,
/// you can normally just call <see cref="ProcessStandardRequest(Stream)"/> or one of its overrides and then set the new <c>ETag</c> and
/// <c>Last-Modified</c> headers if the request was successful. This method supports partial PUTs using the <c>Content-Range</c> header,
/// even though such requests are now disallowed by the latest HTTP specification in RFC 7231. The standard requires the server to reply
/// with 400 <see cref="ConditionCodes.BadRequest">Bad Request</see>, but this implementation does not. If you require the
/// standard-compliant behavior, you should override <see cref="WebDAVRequest.ParseRequest"/> and set <see cref="WebDAVRequest.Status"/> to
/// 400 <see cref="ConditionCodes.BadRequest">Bad Request</see> if <see cref="ContentRange"/> is not null.
/// </para>
/// <para>If you want to handle the request yourself, you should use the entity body supplied by the client to replace the entity body of
/// the request resource, creating it if it doesn't exist, and reply with 201 <see cref="ConditionCodes.Created"/> or
/// 204 <see cref="ConditionCodes.NoContent">No Content</see> along with the <c>ETag</c> and <c>Last-Modified</c> headers for the new or
/// updated resource. The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/></term>
///   <description>The request succeeded, and an entity body is being sent to the client. There is no standard entity body defined for
///     <c>PUT</c> requests, but you can send one if the client can understand it.
///   </description>
/// </item>
/// <item>
///   <term>201 <see cref="ConditionCodes.Created"/></term>
///   <description>The request succeeded and a new resource was created at the request URI.</description>
/// </item>
/// <item>
///   <term>202 <see cref="ConditionCodes.Accepted"/></term>
///   <description>The request was processed, is valid, and will succeed (barring exceptional circumstances), but the execution of the
///     request has been deferred until later.
///   </description>
/// </item>
/// <item>
///   <term>204 <see cref="ConditionCodes.NoContent">No Content</see> (default)</term>
///   <description>An existing resource was successfully updated by the request, and no entity body is being sent to the client. This is
///     the default status code returned when <see cref="WebDAVRequest.Status"/> is null.
///   </description>
/// </item>
/// <item>
///   <term>400 <see cref="ConditionCodes.BadRequest">Bad Request</see></term>
///   <description>The client submitted an invalid request, such as a <c>Content-Range</c> header. (The HTTP standard now disallows
///     <c>Content-Range</c> headers in <c>PUT</c> requests, but it's still supported by the <see cref="PutRequest"/> class by default.)
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to create or alter the resource, or the server refuses to create or alter the resource
///     for some other reason.
///   </description>
/// </item>
/// <item>
///   <term>405 <see cref="ConditionCodes.MethodNotAllowed">Method Not Allowed</see></term>
///   <description>The request resource does not support content updates, for instance because it's read-only, or it's a collection
///     resource and the server doesn't support <c>PUT</c> requests on collection resources. If you return this status code, then you must
///     not include the <c>PUT</c> method in responses to <c>OPTIONS</c> requests.
///   </description>
/// </item>
/// <item>
///   <term>409 <see cref="ConditionCodes.Conflict"/></term>
///   <description>The entity body sent by the client is incompatible with the resource based on criteria other than its media type, or
///     a resource could not be created at the request URI because the parent collection does not exist. The parent collection
///     must not be created automatically.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A conditional request was not executed because the condition wasn't true.</description>
/// </item>
/// <item>
///   <term>415 <see cref="ConditionCodes.UnsupportedMediaType">Unsupported Media Type</see></term>
///   <description>The entity body or <c>Content-Type</c> header sent by the client is incompatible with the resource due to its media
///     type.
///   </description>
/// </item>
/// <item>
///   <term>423 <see cref="ConditionCodes.Locked"/></term>
///   <description>The request resource was locked and no valid lock token was submitted. You should include the
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
///   <term><see cref="ProcessStandardRequest(Stream,FileCreator,EntityMetadata,string)"/></term>
///   <description>You want to change the standard request processing.</description>
/// </item>
/// </list>
/// </remarks>
public class PutRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PutRequest"/> based on a new WebDAV request.</summary>
  public PutRequest(WebDAVContext context) : base(context)
  {
    // parse the Content-Range header if supplied by the client
    ContentRange range = null;
    if(!string.IsNullOrEmpty(context.Request.Headers[DAVHeaders.ContentRange]))
    {
      range = ContentRange.TryParse(context.Request.Headers[DAVHeaders.ContentRange]);
      if(range == null || range.Start == -1) throw Exceptions.BadRequest("Invalid Content-Range header.");
    }
    ContentRange = range;
  }

  /// <summary>A function called to create a new file. The function should return a <see cref="ConditionCode"/> indicating whether
  /// the attempt succeeded or failed, or null for the standard success code.
  /// </summary>
  /// <param name="stream">A variable that will receive a writable and preferably seekable <see cref="Stream"/> where the new file
  /// content will be written, or null if the attempt to create the file failed.
  /// </param>
  public delegate ConditionCode FileCreator(out Stream stream);

  /// <summary>The <see cref="ContentRange"/> value parsed from the HTTP <c>Content-Range</c> header. If not null, the client either
  /// requested a partial <c>PUT</c> (if <see cref="AdamMil.WebDAV.Server.ContentRange.Start"/> is not -1) or indicated knowledge of the length
  /// of the entity body (if <see cref="AdamMil.WebDAV.Server.ContentRange.TotalLength"/> is not -1) or both.
  /// </summary>
  /// <remarks>If the client requested a partial <c>PUT</c>, then only the specified portion of the entity body should be replaced. If the
  /// client indicated knowledge of the entity body length and the length is incorrect, a 416 Requested Range Not Satisfiable response
  /// should be returned to the client with the correct length in the <c>Content-Range</c> header. The
  /// <see cref="ProcessStandardRequest(Stream,EntityMetadata)"/> method handles partial <c>PUT</c> requests automatically, so you only
  /// need to use this property if you handle the response yourself.
  /// </remarks>
  public ContentRange ContentRange { get; private set; }

  /// <include file="documentation.xml" path="/DAV/PutRequest/ProcessStandardRequest/*[not(@name='canonicalPath') and not(@name='metadata')]" />
  public long ProcessStandardRequest(Stream entityBody)
  {
    return ProcessStandardRequest(entityBody, null, null);
  }

  /// <include file="documentation.xml" path="/DAV/PutRequest/ProcessStandardRequest/*[not(@name='canonicalPath')]" />
  public long ProcessStandardRequest(Stream entityBody, EntityMetadata metadata)
  {
    return ProcessStandardRequest(entityBody, metadata, null);
  }

  /// <include file="documentation.xml" path="/DAV/PutRequest/ProcessStandardRequest/node()" />
  public long ProcessStandardRequest(Stream entityBody, EntityMetadata metadata, string canonicalPath)
  {
    return ProcessStandardRequest(entityBody, null, metadata, canonicalPath);
  }

  /// <include file="documentation.xml" path="/DAV/PutRequest/ProcessStandardRequestNew/*[not(@name='canonicalPath')]" />
  public long ProcessStandardRequest(FileCreator createFile)
  {
    return ProcessStandardRequest(null, createFile);
  }

  /// <include file="documentation.xml" path="/DAV/PutRequest/ProcessStandardRequestNew/node()" />
  public long ProcessStandardRequest(string canonicalPath, FileCreator createFile)
  {
    if(createFile == null) throw new ArgumentNullException();
    if(Context.RequestResource != null) throw new InvalidOperationException("This method is not suitable for existing resources.");
    return ProcessStandardRequest(null, createFile, null, canonicalPath);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks><include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensRemarks/remarks/node()" />
  /// <note type="inherit">This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant
  /// resources. That is, the implementation does not support <c>PUT</c> requests made to collection resources. If
  /// <see cref="WebDAVContext.RequestResource"/> is null, the implementation assumes that the <c>PUT</c> request would create a new
  /// resource and checks the parent collection for locks.
  /// </note>
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, Context.RequestResource == null, false);
  }

  /// <summary>Performs standard processing of a <c>PUT</c> request. See <see cref="ProcessStandardRequest(Stream,EntityMetadata,string)"/>
  /// and <see cref="ProcessStandardRequest(string,FileCreator)"/> for documentation of how this method should operate.
  /// </summary>
  protected virtual long ProcessStandardRequest(Stream entityBody, FileCreator createFile, EntityMetadata metadata, string canonicalPath)
  {
    if(entityBody == null) // if there's no entity body to start with...
    {
      if(createFile == null) throw new ArgumentNullException();
      entityBody = new MemoryStream(); // create a temporary, empty body so we can run the precondition checks
    }

    if(!entityBody.CanWrite) throw new ArgumentException("The entity body is not writable.");

    bool needEntityTag = PreconditionsMayNeedEntityTag();
    if(metadata == null)
    {
      metadata = Context.RequestResource == null ? new EntityMetadata() { Exists = false }
                                                 : Context.RequestResource.GetEntityMetadata(needEntityTag);
    }

    long entityLength = metadata.Length.HasValue ? metadata.Length.Value : entityBody.CanSeek ? entityBody.Length : -1;

    // if no entity tag was provided and we need one and the stream is readable and seekable (or it's readable and we'll be replacing it),
    // compute the entity tag using the default method
    if(needEntityTag && metadata.EntityTag == null && metadata.Exists && entityBody.CanRead && (createFile != null || entityBody.CanSeek))
    {
      metadata = metadata.Clone();
      metadata.EntityTag = DAVUtility.ComputeEntityTag(entityBody, entityBody.CanSeek); // compute an entity tag from the body
      if(entityBody.CanSeek) entityBody.Position = 0;
    }

    // check request preconditions. as per the CheckPreconditions documentation, allow errors to take precedence over 304 Not Modified
    ConditionCode status = CheckPreconditions(metadata, canonicalPath);
    if(status != null && status.IsError)
    {
      Status = status;
      return entityLength;
    }

    Stream replacementStream = null;
    try
    {
      if(createFile != null)
      {
        status = createFile(out entityBody);
        if(status != null && status.IsError)
        {
          Status = status;
          return entityLength;
        }
        if(entityBody == null) throw new ContractViolationException("createStream returned a null stream.");
        if(!metadata.Length.HasValue) entityLength = entityBody.CanSeek ? entityBody.Length : -1;
      }

      replacementStream = Context.OpenRequestBody();
      bool isPartialUpdate; // verify that any requested partial update is valid
      if(!CheckPartialUpdate(entityBody, entityLength, ref replacementStream, out isPartialUpdate)) return entityLength;

      if(status != null) // if we have a 304 Not Modified precondition status
      {
        Status = status;
      }
      else
      {
        if(!isPartialUpdate) // otherwise, if it's a full update (i.e. replacement) of the entity body...
        {
          replacementStream.CopyTo(entityBody); // copy the replacement data to the stream and truncate it if necessary
          if(entityBody.CanSeek && entityBody.Position < entityLength) entityBody.SetLength(entityBody.Position);
          entityLength = entityBody.Position;
        }
        else // otherwise, it's a partial update...
        {
          entityLength = PartialPut(entityBody, entityLength, replacementStream);
        }

        // at this point, the entity body stream has been updated
        Status = metadata.Exists ? ConditionCodes.NoContent : ConditionCodes.Created;
      }
    }
    finally
    {
      Utility.Dispose(replacementStream);
    }

    return entityLength;
  }

  bool CheckPartialUpdate(Stream entityBody, long entityLength, ref Stream replacementStream, out bool isPartialUpdate)
  {
    isPartialUpdate = false;

    if(ContentRange != null) // if the client requested a partial PUT and/or indicated knowledge of the entity length...
    {
      // note that partial PUT support was never really specified or documented in the RFC 2616, but it seems like a fairly
      // straightforward extension/interpretation. however, i'm not 100% sure how error cases should be reported to the client. (for
      // instance, if partial PUTs are not allowed, what should we reply with? 416 Requested Range Not Satisfiable is for Range headers
      // rather than Content-Range headers and may cause the client to retry with another partial PUT, while 403 Forbidden or 501 Not
      // Implemented may cause the client to give up entirely. we'll use 416 with no Content-Range header and hope it works.)
      // TODO: do a bit more research on this. in particular, see if we can find and read some existing client and server code
      // NOTE: partial PUTs are now explicitly disallowed by the latest HTTP specification (RFC 7231). technically we should stop
      // supporting them, but they seem useful for a WebDAV server, especially given that RFC 4918 says that PUT requests should not
      // alter the properties of an existing resource, so WebDAV PUT requests are already a kind of partial update even if they replace
      // the entire entity body. so i'll keep the support in here for now

      // if the client indicated knowledge of the entity length but was incorrect...
      if(ContentRange.TotalLength >= 0 && entityLength >= 0 && ContentRange.TotalLength != entityLength)
      {
        // respond with a 416 Requested Range Not Satisfiable status that includes the correct entity length
        Context.Response.Headers[DAVHeaders.ContentRange] = new ContentRange(entityLength).ToHeaderString();
        Status = ConditionCodes.RequestedRangeNotSatisfiable;
        return false;
      }

      // if the client requested a partial PUT...
      if(ContentRange.Start >= 0)
      {
        // we need to know how long the replacement stream is, so copy it to a temporary location if necessary
        if(!replacementStream.CanSeek)
        {
          Stream replacement = replacementStream;
          Impersonation.RunWithImpersonation(Impersonation.RevertToSelf, false, delegate // revert to self so we can open a temp file
          {                                                                              // if necessary
            replacement = new SeekableStreamWrapper(replacement);
          });
          replacementStream = replacement;
        }

        // whether it's really updating only a subset of the entity body (assuming entityLength is valid)...
        isPartialUpdate = ContentRange.Start != 0 || ContentRange.Start + ContentRange.Length < entityLength;

        // if the range doesn't start at the beginning or if the client is replacing a piece of the entity with a smaller one, or if the
        // client is not known to be replacing data through the end of the entity, then we'll need to be able to seek within the file.
        // also, if we need to shift any data over (i.e. the part we're replacing isn't the same length as the new data), then we'll need
        // the ability to read from the stream as well. we also disallow the start position to be past the end of the stream, because
        // there's unlikely to be a good use case and it makes DOS attacks too easy. (a client could ask to write at the trillionth byte)
        if((isPartialUpdate || entityLength < 0) && // if it's a partial update or we can't be sure...
            (!entityBody.CanSeek || ContentRange.Start > Math.Max(0, entityLength) ||
            !entityBody.CanRead && ContentRange.Length != replacementStream.Length))
        {
          Status = ConditionCodes.RequestedRangeNotSatisfiable; // if we can't, reply with a 416 status without a Content-Range header
          return false;
        }
      }
    }

    return true;
  }

  long PartialPut(Stream entityBody, long entityLength, Stream replacementStream)
  {
    long rangeEnd = ContentRange.Start + ContentRange.Length;
    bool hasDataPastEnd = rangeEnd < entityLength; // is there any data after the end of the update region?
    // if we need to move data from beyond the end of the range into a temporary location (to prevent it from being overwritten)...
    byte[] saved = null;
    if(hasDataPastEnd && replacementStream.Length > ContentRange.Length)
    {
      entityBody.Position = ContentRange.Start + ContentRange.Length;
      saved = entityBody.LongRead(replacementStream.Length - ContentRange.Length);
    }

    // now we need to move to the beginning of the update range. if we can't seek, then the stream is already at the right place
    if(entityBody.CanSeek) entityBody.Position = ContentRange.Start;

    // write the replacement data into the entity body
    replacementStream.CopyTo(entityBody);

    if(hasDataPastEnd && replacementStream.Length != ContentRange.Length) // if we have to shift any data over
    {
      byte[] buffer = new byte[64*1024];
      if(replacementStream.Length < ContentRange.Length) // if we need to shift the remaining data to the left...
      {
        long offset = ContentRange.Length - replacementStream.Length; // how far we need to shift it
        do
        {
          entityBody.Position = rangeEnd; // move to the start of the data we need to shift
          int read = entityBody.FullRead(buffer, 0, buffer.Length); // read as much data as we can
          entityBody.Position = rangeEnd - offset; // move over
          entityBody.Write(buffer, 0, read); // write it
          rangeEnd += read; // move to the next chunk of data
        } while(rangeEnd < entityBody.Length);

        entityLength = rangeEnd - offset;
        entityBody.SetLength(entityLength); // truncate the stream to the new length now that we've shifted the data over
      }
      else // otherwise, if we need to shift the remaining data to the right...
      {
        // 'end' is the end of the replacement data. readPtr points to the end of the block we want to shift
        long end = ContentRange.Start + replacementStream.Length, readPtr = entityLength;
        long offset = replacementStream.Length - ContentRange.Length; // the distance the data needs to be moved
        do
        {
          int read = (int)Math.Min(readPtr - end, buffer.Length); // read up to a full buffer backwards from readPtr
          readPtr -= read; // move the readPtr to the start of the block that we're going to read (and end of the next block)
          entityBody.Position = readPtr; // seek to the start of the block we're going to read
          entityBody.ReadOrThrow(buffer, 0, read); // read the full block
          entityBody.Position = readPtr + offset; // move over to the write position
          entityBody.Write(buffer, 0, read); // and write it
        } while(readPtr > end); // if the end of the block to shift is after the start of the data, there's more work to do

        // now there should be a gap into which we can place the extra bytes we saved previously
        entityBody.Position = end; // seek to the start of that gap
        entityBody.Write(saved); // and write the saved bytes into it
        entityLength += offset;
      }
    }

    return entityLength;
  }
}

} // namespace AdamMil.WebDAV.Server
