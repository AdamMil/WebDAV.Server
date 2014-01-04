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
using System.IO;
using System.Net;
using AdamMil.IO;
using AdamMil.Utilities;

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>PUT</c> request.</summary>
/// <remarks>The <c>PUT</c> request is described in section 9.7 of RFC 4918.</remarks>
public class PutRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PutRequest"/> based on a new WebDAV request.</summary>
  public PutRequest(WebDAVContext context) : base(context)
  {
    // parse the Content-Range header if supplied by the client
    ContentRange range = null;
    if(!string.IsNullOrEmpty(context.Request.Headers[HttpHeaders.ContentRange]))
    {
      range = ContentRange.TryParse(context.Request.Headers[HttpHeaders.ContentRange]);
      if(range == null) throw Exceptions.BadRequest("Invalid Content-Range header.");
      if(range.Start == -1 && range.TotalLength == -1) range = null; // treat useless values as though they weren't submitted at all
    }
    ContentRange = range;
  }

  /// <summary>The <see cref="ContentRange"/> value parsed from the HTTP <c>Content-Range</c> header. If not null, the client either
  /// requested a partial <c>PUT</c> (if <see cref="AdamMil.WebDAV.Server.ContentRange.Start"/> is not -1) or indicated knowledge of the length
  /// of the entity body (if <see cref="AdamMil.WebDAV.Server.ContentRange.TotalLength"/> is not -1) or both.
  /// </summary>
  /// <remarks>If the client requested a partial <c>PUT</c>, then only the specified portion of the entity body should be replaced. If the
  /// client indicated knowledge of the entity body length and the length is incorrect, a 416 Requested Range Not Satisfiable response
  /// should be returned to the client with the correct length in the <c>Content-Range</c> header. The <see cref="ProcessStandardRequest"/>
  /// method handles partial <c>PUT</c> requests automatically, so you only need to use this property if you handle the response yourself.
  /// </remarks>
  public ContentRange ContentRange { get; private set; }

  /// <summary>Performs standard processing of a <c>PUT</c> request.</summary>
  /// <param name="entityBody">A <see cref="Stream"/> to which the new entity body should be written. The stream must be writable, and it
  /// is best if the stream is also seekable and readable.
  /// </param>
  /// <param name="metadata">Metadata about the entity body. If null, the metadata will be retrieved by calling
  /// <see cref="IWebDAVResource.GetEntityMetadata"/> on the <see cref="WebDAVContext.RequestResource"/> if the request resource is
  /// available. As much information should be provided as possible. If <see cref="EntityMetadata.Length"/> is null, the length will be
  /// taken from the <paramref name="entityBody"/> if the stream is seekable. If <see cref="EntityMetadata.EntityTag"/> is null, an entity
  /// tag will be computed from the stream (using <see cref="DAVUtility.ComputeEntityTag(Stream,bool)"/>) if the stream is readable and
  /// seekable. If the entity tag or last modification time is unavailable, request preconditions (e.g. <c>If-Match</c> or
  /// <c>If-Unmodified-Since</c> headers), which are particularly important for <c>PUT</c> requests, cannot be processed correctly.
  /// </param>
  /// <remarks>This method implements the standard <c>PUT</c> method, which replaces the entity body entirely. If
  /// <paramref name="entityBody"/> is seekable, the method also supports a partial <c>PUT</c> extension that allows clients to specify the
  /// portion of the entity body to overwrite in the <c>Content-Range</c> header. If <paramref name="entityBody"/> is readable, the method
  /// additionally supports partial <c>PUT</c> invocations that replace a portion of the entity body with a larger or smaller portion (i.e.
  /// that cause the entity body to contract or expand). This is why it is best for <paramref name="entityBody"/> to be readable and
  /// seekable. If creating a readable, seekable stream is expensive, you can examine the <see cref="ContentRange"/> property and only
  /// create a readable, seekable stream if <see cref="ContentRange"/> is not null (indicating a partial <c>PUT</c> request).
  /// <para>This method does not provide any headers to the client (except the <c>Content-Range</c> header when the requested range is
  /// invalid), but you can add additional headers either before or after the method returns. In particular, you should add the <c>ETag</c>
  /// and <c>Last-Modified</c> headers if the <c>PUT</c> request was successfully processed (i.e. if <see cref="WebDAVRequest.Status"/> is
  /// null or <see cref="ConditionCode.IsSuccessful"/> is true after this method returns). If you do not have your own method for computing
  /// entity tags, you should use the <see cref="DAVUtility.ComputeEntityTag(Stream,bool)"/> method.
  /// </para>
  /// </remarks>
  public void ProcessStandardRequest(Stream entityBody, EntityMetadata metadata)
  {
    if(entityBody == null) throw new ArgumentNullException();
    if(!entityBody.CanWrite) throw new ArgumentException("The entity body is not writable.");

    if(metadata == null)
    {
      metadata = Context.RequestResource == null ? new EntityMetadata() { Exists = false }
                                                 : Context.RequestResource.GetEntityMetadata(!entityBody.CanSeek || !entityBody.CanRead);
    }

    long entityLength = metadata.Length.HasValue ? metadata.Length.Value : entityBody.CanSeek ? entityBody.Length : -1;

    // check request preconditions
    ConditionCode precondition = null;
    // if no entity tag was provided and we may need one and the stream is readable and seekable, compute it using the default method
    if(ClientSubmittedPreconditions && metadata.Exists && metadata.EntityTag == null && entityBody.CanSeek && entityBody.CanRead)
    {
      metadata = metadata.Clone();
      metadata.EntityTag = DAVUtility.ComputeEntityTag(entityBody, true); // compute an entity tag from the body
      entityBody.Position = 0;
    }

    precondition = CheckPreconditions(metadata);
    // as per the CheckPreconditions documentation, we'll allow other errors to take precedence over 304 Not Modified
    if(precondition != null && precondition.StatusCode != (int)HttpStatusCode.NotModified)
    {
      Status = precondition;
      return;
    }

    Stream replacementStream = null;
    try
    {
      replacementStream = Context.OpenRequestBody();
      bool isPartialUpdate = false;
      if(ContentRange != null) // if the client requested a partial PUT and/or indicated knowledge of the entity length...
      {
        // note that partial PUT support is not really specified or documented in the HTTP specification, but it seems like a fairly
        // straightforward extension/interpretation. however, i'm not 100% sure how error cases should be reported to the client. (for
        // instance, if partial PUTs are not allowed, what should we reply with? 416 Requested Range Not Satisfiable is for Range headers
        // rather than Content-Range headers and may cause the client to retry with another partial PUT, while 403 Forbidden or 501 Not
        // Implemented may cause the client to give up entirely. we'll use 416 with no Content-Range header and hope it works.)
        // TODO: do a bit more research on this. in particular, see if we can find and read some existing client and server code

        // if the client indicated knowledge of the entity length but was incorrect...
        if(ContentRange.TotalLength != -1 && entityLength != -1 && ContentRange.TotalLength != entityLength)
        {
          // respond with a 416 Requested Range Not Satisfiable status that includes the correct entity length
          Context.Response.Headers[HttpHeaders.ContentRange] = new ContentRange(entityLength).ToHeaderString();
          Status = ConditionCodes.RequestedRangeNotSatisfiable;
          return;
        }

        // if the client requested a partial PUT...
        if(ContentRange.Start != -1)
        {
          // we need to know how long the replacement stream is, so copy it to a temporary location if necessary
          if(!replacementStream.CanSeek)
          {
            Impersonation.RunWithImpersonation(Impersonation.RevertToSelf, false, delegate // revert to self so we can open a temp file
            {                                                                              // if necessary
              replacementStream = new SeekableStreamWrapper(replacementStream);
            });
          }

          // whether it's really updating only a subset of the entity body (assuming entityLength is valid)...
          isPartialUpdate = ContentRange.Start != 0 || ContentRange.Start + ContentRange.Length < entityLength;

          // if the range doesn't start at the beginning or if the client is replacing a piece of the entity with a smaller one, or if the
          // client is not known to be replacing data through the end of the entity, then we'll need to be able to seek within the file.
          // also, if we need to shift any data over (i.e. the part we're replacing isn't the same length as the new data), then we'll need
          // the ability to read from the stream as well. we also disallow the start position to be past the end of the stream, because
          // there's unlikely to be a good use case and it makes DOS attacks too easy. (a client could ask to write at the trillionth byte)
          if((isPartialUpdate || entityLength == -1) && // if it's a partial update or we can't be sure...
             (!entityBody.CanSeek || ContentRange.Start > Math.Max(0, entityLength) ||
              !entityBody.CanRead && ContentRange.Length != replacementStream.Length))
          {
            Status = ConditionCodes.RequestedRangeNotSatisfiable; // if we can't, reply with a 416 status without a Content-Range header
            return;
          }
        }
      }

      if(precondition != null) // if we have a 304 Not Modified precondition status
      {
        Status = precondition;
      }
      else
      {
        if(!isPartialUpdate) // otherwise, if it's a full update (i.e. replacement) of the entity body...
        {
          replacementStream.CopyTo(entityBody); // copy the replacement data to the stream and truncate it if necessary
          if(entityBody.CanSeek && entityBody.Position < entityLength) entityBody.SetLength(entityBody.Position);
        }
        else // otherwise, it's a partial update...
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

              entityBody.SetLength(rangeEnd - offset); // truncate the stream to the new length now that we've shifted the data over
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
            }
          }
        }

        // at this point, the entity body stream has been updated
        Status = metadata.Exists ? ConditionCodes.NoContent : ConditionCodes.Created;
      }
    }
    finally
    {
      Utility.Dispose(replacementStream);
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources. That is, the
  /// implementation does not support <c>PUT</c> requests made to collection resources. If <see cref="WebDAVContext.RequestResource"/> is
  /// null, the implementation assumes that the <c>PUT</c> request would create a new resource and checks the parent collection for locks.
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, Context.RequestResource == null, false);
  }
}

} // namespace AdamMil.WebDAV.Server
