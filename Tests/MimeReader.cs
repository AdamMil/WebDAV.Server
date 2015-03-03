using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.Utilities.Encodings;
using AdamMil.WebDAV.Server;

namespace WebDAV.Server.Tests
{

#region MimeReader
public sealed class MimeReader : IDisposable
{
	public MimeReader(Stream mimeStream) : this(mimeStream, null) { }

	public MimeReader(Stream mimeStream, WebHeaderCollection headers)
	{
		if(mimeStream == null) throw new ArgumentNullException();

		lineReader = new MimeLineReader(mimeStream);
		Headers = headers;
		if(Headers == null) Headers = ReadHeaders();
		if(Headers == null) throw new ArgumentException("The stream ended before the initial MIME headers.");

		// if the content length is set, wrap the stream so that we can't read more than that many bytes from it
		string contentLength = Headers[DAVHeaders.ContentLength];
		if(!string.IsNullOrEmpty(contentLength))
		{
			long length;
			if(long.TryParse(contentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) && length >= 0)
			{
				lineReader.FinishReadingLines(); // reset the position of 'mimeStream' to the beginning of the body
				mimeStream = new Substream(mimeStream, length); // wrap 'mimeStream' to limit its length
				lineReader = new MimeLineReader(mimeStream); // and create a new line reader based on the wrapped stream
			}
		}

    string contentType = Headers[DAVHeaders.ContentType];
		if(!string.IsNullOrEmpty(contentType) && contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
		{
			Match m = boundaryRe.Match(contentType);
			if(!m.Success) throw new ArgumentException("Could not parse multipart message boundary from headers.");
			boundary = "--" + m.Groups["value"].Value; // the actual boundary lines will have "--" prepended

			// skip past the first boundary
			string line;
			do line = lineReader.ReadLine();
			while(line != null && !IsBoundary(line));
		}
		else
		{
			atBeginning = true;
		}

		this.mimeStream = mimeStream;
		if(mimeStream.CanSeek) startPosition = mimeStream.Position;
	}

	public bool CanRewind
	{
		get { return mimeStream.CanSeek; }
	}

	public WebHeaderCollection Headers
	{
		get; private set;
	}

	public void Dispose()
	{
		mimeStream.Dispose();
		previousPart = null;
	}

	public IEnumerable<Part> EnumerateParts()
	{
		Rewind();

		while(true)
		{
			Part part = GetNextPart();
			if(part == null) break;
			yield return part;
		}
	}

	public Part GetNextPart()
	{
		// if we have to finish the previous part before we can read the next one, do so
		if(previousPart != null)
		{
			previousPart.AdvanceToEnd();
			previousPart = null;
		}

		// if we're in a single-part message and already read a part, then we must be at the end now
		if(!IsMultiPartMessage && !atBeginning) return null;

		// otherwise, start reading the next part, beginning with the headers. if we're at the beginning of the message, then we
		// already parsed the headers in the constructor, so we should use those. otherwise, read the headers
		WebHeaderCollection headers = atBeginning ? Headers : ReadHeaders();
		atBeginning = false;

		Part part = headers == null ? null : new Part(this, headers, headers == Headers);
		previousPart = part; // keep track of the part so we can advance the stream to the next part when we need to
		return part;
	}

	public void Rewind()
	{
		if(!atBeginning)
		{
			mimeStream.Position = startPosition;
			atBeginning  = true;
			previousPart = null;
		}
	}

  #region MimeLineReader
  sealed class MimeLineReader
  {
	  public MimeLineReader(Stream mimeStream)
	  {
		  if(mimeStream == null) throw new ArgumentNullException();
		  this.mimeStream = mimeStream;
		  streamPosition  = -1;
	  }

	  public void FinishReadingLines()
	  {
		  if(streamPosition != -1)
		  {
			  mimeStream.Position = streamPosition; // reset the stream position
			  streamPosition = -1;
			  byteBuffer.Clear();
		  }
	  }

	  public string ReadLine()
	  {
		  byte[] bytes;
		  return ReadLine(SimpleEightBitEncoding.Instance, out bytes, false);
	  }

	  public string ReadLine(Encoding encoding, out byte[] originalBytes)
	  {
		  return ReadLine(encoding, out originalBytes, true);
	  }

	  public char[] ReadLineChars()
	  {
		  byte[] originalBytes;
		  return ReadLine<char[]>(out originalBytes, false,
		                          (array, index, count) => SimpleEightBitEncoding.Instance.GetChars(array, index, count));
	  }

	  string ReadLine(Encoding encoding, out byte[] originalBytes, bool wantOriginalBytes)
	  {
		  if(encoding == null) encoding = SimpleEightBitEncoding.Instance;
		  return ReadLine<string>(out originalBytes, wantOriginalBytes,
		                          (array, index, count) => encoding.GetString(array, index, count));
	  }

	  T ReadLine<T>(out byte[] originalBytes, bool wantOriginalBytes, Func<byte[],int,int,T> resultMaker) where T : class
	  {
		  originalBytes = null;

		  int lineLength;
		  if(!mimeStream.CanSeek) // if the stream is not seekable, we'll have to read the line byte by byte to avoid reading too much
		  {
			  if(byteBuffer == null) byteBuffer = new ArrayBuffer<byte>(80);

			  while(true)
			  {
				  int byteValue = mimeStream.ReadByte();
				  if(byteValue == -1)
				  {
					  if(byteBuffer.Count != 0) break;
					  else return null;
				  }
				  byteBuffer.Add((byte)byteValue);
				  if(byteValue == (byte)'\n' && byteBuffer.Count > 1 && byteBuffer[byteBuffer.Count-2] == (byte)'\r')
				  {
					  byteBuffer.SetCount(byteBuffer.Count-2); // remove the CRLF from the end of the buffer
					  break;
				  }
			  }

			  lineLength = byteBuffer.Count;
		  }
		  else // otherwise, it is seekable, so we can read a large amount into a buffer and then read lines out of that
		  {
			  // if we haven't started reading lines yet, keep track of the stream position so we can return to it later
			  if(streamPosition == -1)
			  {
				  streamPosition = mimeStream.Position;
				  if(byteBuffer == null) byteBuffer = new ArrayBuffer<byte>(4096);
			  }

			  int newline;
			  while(true)
			  {
				  newline = byteBuffer.IndexOf((byte)'\n', bufferScanFrom);
				  bufferScanFrom = newline == -1 ? byteBuffer.Count : newline+1;

				  if(newline != -1) // if we found a newline...
				  {
					  // ignore it if it's not preceded by a carriage return
					  if(newline == 0 || byteBuffer[newline-1] != (byte)'\r')
					  {
						  continue;
					  }
					  else // otherwise, return the line ending with it
					  {
						  lineLength = newline + 1;
						  break;
					  }
				  }

				  // we couldn't find a newline, so read more data into the buffer (increasing the capacity if it's full)
				  byte[] destination = byteBuffer.GetArrayForWriting(byteBuffer.IsFull ? byteBuffer.Capacity : 0);
				  int read = mimeStream.Read(destination, byteBuffer.Offset, destination.Length-byteBuffer.End);
				  byteBuffer.AddCount(read);

				  if(read == 0) // if we couldn't read any more data, return the current data as the line if any
				  {
					  if(byteBuffer.Count == 0) return null;
					  lineLength = byteBuffer.Count;
					  break;
				  }
			  }
		  }

		  if(wantOriginalBytes)
		  {
			  originalBytes = new byte[lineLength];
			  byteBuffer.CopyTo(originalBytes, 0, lineLength);
		  }

		  T result = resultMaker(byteBuffer.Buffer, byteBuffer.Offset, lineLength);
		  byteBuffer.Remove(lineLength);
		  streamPosition += lineLength;
		  return result;
	  }

	  readonly Stream mimeStream;
	  ArrayBuffer<byte> byteBuffer;
	  long streamPosition;
	  int bufferScanFrom;
  }
  #endregion

  #region Part
  public sealed class Part
  {
	  internal Part(MimeReader mimeReader, WebHeaderCollection headers, bool isBodyPart)
	  {
		  this.reader   = mimeReader;
		  this.Headers  = headers;

		  // get the content encoding
		  string headerValue = Headers[DAVHeaders.ContentEncoding];
		  if(string.IsNullOrEmpty(headerValue))
		  {
			  contentEncoding = ContentEncoding.Identity;
		  }
		  else if(string.Equals(headerValue, "gzip", StringComparison.OrdinalIgnoreCase) ||
			        string.Equals(headerValue, "x-gzip", StringComparison.OrdinalIgnoreCase))
		  {
			  contentEncoding = ContentEncoding.GZip;
		  }
		  else if(string.Equals(headerValue, "deflate", StringComparison.OrdinalIgnoreCase))
		  {
			  contentEncoding = ContentEncoding.Deflate;
		  }
		  else
		  {
			  AdvanceToEnd();
			  throw new NotSupportedException("Unsupported content encoding: " + headerValue);
		  }

		  // get the transfer encoding
		  headerValue = Headers["Content-Transfer-Encoding"]; // the equivalent HTTP header is named differently (Transfer-Encoding)
		  if(string.IsNullOrEmpty(headerValue) || string.Equals(headerValue, "7bit", StringComparison.OrdinalIgnoreCase) ||
		     string.Equals(headerValue, "8bit", StringComparison.OrdinalIgnoreCase) ||
		     string.Equals(headerValue, "binary", StringComparison.OrdinalIgnoreCase))
		  {
			  transferEncoding = TransferEncoding.Plain;
		  }
		  else if(string.Equals(headerValue, "base64", StringComparison.OrdinalIgnoreCase))
		  {
			  transferEncoding = TransferEncoding.Base64;
		  }
		  else
		  {
			  AdvanceToEnd();
			  throw new NotSupportedException("Unsupported transfer encoding: " + headerValue);
		  }

		  // get the content type and character set
		  headerValue = Headers[DAVHeaders.ContentType];
		  if(headerValue != null)
		  {
			  if(!isBodyPart && headerValue.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
			  {
				  AdvanceToEnd();
				  throw new NotSupportedException("Nested multipart messages are not supported.");
			  }
			  // HACK: this isn't really part of the MIME standard, but some annoying X-Tee servers don't send a proper
			  // Content-Transfer-Encoding header and instead inform us that the transfer encoding is base64 using the Content-Type
			  // header, which is just evil. but we've gotta deal with it.
			  else if(transferEncoding == TransferEncoding.Plain &&
			          headerValue.Equals("{http://www.w3.org/2001/XMLSchema}base64Binary", StringComparison.OrdinalIgnoreCase))
			  {
				  transferEncoding = TransferEncoding.Base64;
			  }
			  else
			  {
				  Match m = charsetRe.Match(headerValue);
				  if(m.Success)
				  {
					  try { Encoding = Encoding.GetEncoding(m.Groups["value"].Value); }
					  catch(ArgumentException) { }
				  }
			  }
		  }

		  if(Encoding == null) Encoding = SimpleEightBitEncoding.Instance;
	  }

	  public Encoding Encoding
	  {
		  get; private set;
	  }

	  public WebHeaderCollection Headers
	  {
		  get; private set;
	  }

	  public Stream GetContent()
	  {
		  if(streamRetrieved) throw new InvalidOperationException("The content stream has already been retrieved.");
		  Stream stream = new MimePartStream(this, GetTransferEncoder());
		  if(contentEncoding == ContentEncoding.GZip) stream = new GZipStream(stream, CompressionMode.Decompress);
		  else if(contentEncoding == ContentEncoding.Deflate) stream = new DeflateStream(stream, CompressionMode.Decompress);
		  streamRetrieved = true;
		  return stream;
	  }

	  #region MimePartStream
	  sealed class MimePartStream : Stream
	  {
		  internal MimePartStream(Part part, Encoder encoder)
		  {
			  this.part    = part;
			  this.encoder = encoder;
		  }

		  public override bool CanRead
		  {
			  get { return true; }
		  }

		  public override bool CanSeek
		  {
			  get { return false; }
		  }

		  public override bool CanWrite
		  {
			  get { return false; }
		  }

		  public override long Length
		  {
			  get { throw new NotSupportedException(); }
		  }

		  public override long Position
		  {
			  get { throw new NotSupportedException(); }
			  set { throw new NotSupportedException(); }
		  }

		  public override void Flush() { }

		  public override int Read(byte[] buffer, int offset, int count)
		  {
			  int totalRead = 0;

			  if(!part.atEnd)
			  {
				  // if it's a single-part message without no encoding, we can copy the bytes directly from the stream
				  if(encoder == null && !part.reader.IsMultiPartMessage)
				  {
					  part.reader.lineReader.FinishReadingLines(); // make sure the stream is positioned correctly
					  totalRead = part.reader.mimeStream.Read(buffer, offset, count);
				  }
				  else // otherwise, it's a multi-part or encoded message, so we'll read it line by line
				  {
					  if(byteBuffer == null) byteBuffer = new ArrayBuffer<byte>(128);

					  while(count != 0)
					  {
						  // first service the request from the buffer, if we can
						  if(byteBuffer.Count != 0)
						  {
							  int read = Math.Min(byteBuffer.Count, count);
							  byteBuffer.Remove(buffer, offset, read);
							  offset      += read;
							  count       -= read;
							  totalRead   += read;
						  }

						  if(count == 0 || part.atEnd) break; // if we're done, break (to avoid reading more when part.atEnd is true)

						  if(encoder == null) // if the content is not encoded, just copy the bytes directly
						  {
							  byte[] lineBytes;
							  string line = part.reader.lineReader.ReadLine(part.Encoding, out lineBytes);

							  // if we hit the end of the stream, then we're done
							  if(line == null || part.reader.IsBoundary(line))
							  {
								  part.atEnd = true;
								  break;
							  }
							  else
							  {
								  // copy the line bytes (including the CRLF characters) directly into the output buffer
								  int read = Math.Min(lineBytes.Length, count);
								  Array.Copy(lineBytes, 0, buffer, offset, lineBytes.Length);
								  offset    += read;
								  count     -= read;
								  totalRead += read;

								  // if there was data left over in the line bytes, copy it into the byte buffer
								  if(read != lineBytes.Length) byteBuffer.AddRange(lineBytes, read, lineBytes.Length-read);
							  }
						  }
						  else // otherwise, pass the characters through the encoder
						  {
							  char[] line = part.reader.lineReader.ReadLineChars();

							  // if we hit the end of the stream, then use an empty line to flush the data from the encoder
							  if(line == null || part.reader.IsBoundary(line))
							  {
								  part.atEnd = true;
								  line = new char[0];
							  }

							  byte[] destination = byteBuffer.GetArrayForWriting(encoder.GetByteCount(line, 0, line.Length, part.atEnd));
							  byteBuffer.AddCount(encoder.GetBytes(line, 0, line.Length, destination, byteBuffer.Offset, part.atEnd));
						  }
					  }
				  }
			  }

			  return totalRead;
		  }

		  public override long Seek(long offset, SeekOrigin origin)
		  {
			  throw new NotSupportedException();
		  }

		  public override void SetLength(long value)
		  {
			  throw new NotSupportedException();
		  }

		  public override void Write(byte[] buffer, int offset, int count)
		  {
			  throw new NotSupportedException();
		  }

		  protected override void Dispose(bool disposing)
		  {
			  part.AdvanceToEnd();
			  base.Dispose(disposing);
		  }

		  readonly Part part;
		  readonly Encoder encoder;
		  ArrayBuffer<byte> byteBuffer;
	  }
	  #endregion

	  enum TransferEncoding
	  {
		  Plain, Base64
	  }

	  internal void AdvanceToEnd()
	  {
		  // if we haven't already reached the end of the part, advance to it
		  if(!atEnd)
		  {
			  if(reader.IsMultiPartMessage)
			  {
				  while(true) // it's a multi-part message, so read until we find the boundary
				  {
					  string line = reader.lineReader.ReadLine();
					  if(line == null || reader.IsBoundary(line)) break;
				  }
				  reader.lineReader.FinishReadingLines();
			  }
			  else // otherwise, it's a single-part message, just skip all the way to the end of the stream
			  {
				  reader.lineReader.FinishReadingLines();
				  if(reader.mimeStream.CanSeek)
				  {
					  reader.mimeStream.Seek(0, SeekOrigin.End);
				  }
				  else
				  {
					  byte[] buffer = new byte[4096];
					  while(reader.mimeStream.Read(buffer, 0, buffer.Length) != 0) { }
				  }
			  }

			  atEnd = true;
		  }
	  }

	  Encoder GetTransferEncoder()
	  {
		  if(transferEncoding == TransferEncoding.Base64) return new EightBitEncoder(new Base64Encoder());
		  else return null;
	  }

	  readonly MimeReader reader;
	  readonly ContentEncoding contentEncoding;
	  readonly TransferEncoding transferEncoding;
	  bool atEnd, streamRetrieved;

	  static readonly Regex charsetRe = MimeReader.MakeParamRegex("charset");
  }
  #endregion

	#region PartHeaderCollection
	sealed class PartHeaderCollection : WebHeaderCollection
	{
		internal void Initialize(string header, string rawValue)
		{
			if(rawValue != null)
			{
				// if the value looks like an encoded word, we need to decode it
				Match m = encodedWordRe.Match(rawValue);
				if(m.Success)
				{
					string charset = m.Groups["charset"].Value, encodingType = m.Groups["encoding"].Value, text = m.Groups["text"].Value;

					Encoding encoding = Encoding.ASCII;
					if(!string.IsNullOrEmpty(charset))
					{
						try { encoding = Encoding.GetEncoding(charset); }
						catch(ArgumentException) { }
					}

					if(string.Equals(encodingType, "Q", StringComparison.Ordinal))
					{
						rawValue = QDecode(encoding, text);
					}
					else if(string.Equals(encodingType, "B", StringComparison.Ordinal))
					{
						rawValue = encoding.GetString(Convert.FromBase64String(text));
					}
				}
			}

			AddWithoutValidate(header, rawValue);
		}
	}
	#endregion

  bool IsMultiPartMessage
  {
    get { return boundary != null; }
  }

  bool IsBoundary(string line)
  {
    if(line == null) return false;
    return line.StartsWith(boundary, StringComparison.Ordinal) &&
		       (line.Length == boundary.Length || line.Length == boundary.Length+2 && line.EndsWith("--", StringComparison.Ordinal));
  }

  bool IsBoundary(char[] line)
  {
    if(line == null || line.Length != boundary.Length && line.Length != boundary.Length+2) return false;

    for(int i=0; i<boundary.Length; i++)
    {
      if(boundary[i] != line[i]) return false;
    }

    return line.Length == boundary.Length || line[line.Length-1] == '-' && line[line.Length-2] == '-';
  }

	PartHeaderCollection ReadHeaders()
	{
		PartHeaderCollection headers = new PartHeaderCollection();
		string key = null, value = null;
		while(true)
		{
			string line = lineReader.ReadLine();
			if(line == null) return null;
			else if(line.Length == 0) break;

			// if the line begins with whitespace, then it's a continuation of the previous header value
			if(char.IsWhiteSpace(line[0]))
			{
				value += line;
			}
			else
			{
				if(key != null) headers.Initialize(key, value);
				key = value = null;

				Match m = headerRe.Match(line);
				if(m.Success)
				{
					key   = m.Groups["header"].Value;
					value = m.Groups["value"].Value;
				}
			}
		}

		if(key != null) headers.Initialize(key, value);

		return headers;
	}

	readonly Stream mimeStream;
	readonly MimeLineReader lineReader;
	readonly string boundary;
	Part previousPart;
	long startPosition;
	bool atBeginning;

  static int HexValue(char c)
  {
    if(c >= '0' && c <= '9') return c - '0';
    c = char.ToUpperInvariant(c);
    if(c >= 'A' && c <= 'F') return c - ('A' - 10);
    return -1;
  }

	static Regex MakeParamRegex(string paramName)
	{
		return new Regex(@"[\s;]" + Regex.Escape(paramName) + @"=(?:""(?<value>[^""]+)""|(?<value>\S+))",
		                 RegexOptions.IgnoreCase | RegexOptions.Singleline);
	}

	static string QDecode(Encoding encoding, string text)
	{
		byte[] bytes = new byte[text.Length];
		int byteCount = 0;

		for(int i=0; i<text.Length; i++)
		{
			char c = text[i];

			if(c == '_') // underscores represent spaces
			{
				c = ' ';
			}
			else if(c == '=' && i < text.Length-2) // equal signs are expected to be followed by two hex digits
			{
				int c1 = HexValue(text[i+1]), c2 = HexValue(text[i+2]);
				if(c1 != -1 && c2 != -1)
				{
					bytes[byteCount++] = (byte)((c1 << 4) | c2);
					i += 2;
					continue;
				}
			}

			bytes[byteCount++] = (byte)c;
		}

		return encoding.GetString(bytes, 0, byteCount);
	}

	static readonly Regex headerRe = new Regex(@"^(?<header>[^\s:]+)\s*:\s*(?<value>.*?)\s*$", RegexOptions.Singleline);
	static readonly Regex boundaryRe = MakeParamRegex("boundary");
	static readonly Regex encodedWordRe = new Regex(@"^=\?(?<charset>[^\?]*)\?(?<encoding>[^\?]*)\?(?<text>[^\?]*)\?=$",
	                                                RegexOptions.Singleline);
}
#endregion

}
