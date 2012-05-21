using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HiA.IO;

// TODO: add processing examples and documentation
// TODO: implement conditional GETs

namespace HiA.WebDAV.Server
{

#region ByteRange
/// <summary>Represents a range of bytes within an entity response, used to enable partial responses.</summary>
public struct ByteRange
{
  /// <summary>Initializes a new <see cref="ByteRange"/> based on the given start and end positions.</summary>
  public ByteRange(long start, long length)
  {
    if(start < 0 || length < 0 || (start + length) < 0) throw new ArgumentOutOfRangeException();
    Start  = start;
    Length = length;
  }

  internal ByteRange(long start, long length, bool dummy)
  {
    Start  = start;
    Length = length;
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return Start.ToInvariantString() + " + " + Length.ToInvariantString();
  }

  /// <summary>Gets the start of the range, in bytes.</summary>
  public readonly long Start;

  /// <summary>Gets the length the range, in bytes.</summary>
  public readonly long Length;
}
#endregion

#region GetOrHeadRequest
/// <summary>Represents a <c>GET</c> or <c>HEAD</c> request.</summary>
public class GetOrHeadRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="GetOrHeadRequest"/> based on a new WebDAV request.</summary>
  public GetOrHeadRequest(WebDAVContext context) : base(context)
  {
    // try to parse the HTTP Range header if it was specified
    string rangeHeader = context.Request.Headers["Range"];
    if(!string.IsNullOrEmpty(rangeHeader))
    {
      Match m = reRanges.Match(rangeHeader);
      if(m.Success) // if it looks superficially okay...
      {
        // parse the ranges. since we won't know the actual ranges until we know the entity length, we'll abuse the ByteRange structure a
        // bit by storing -1 in the fields that we need to compute, even though it normally doesn't allow negative values
        string[] rangeStrings = m.Groups["ranges"].Value.Split(',', s => s.Trim());
        ByteRange[] ranges = new ByteRange[rangeStrings.Length];
        for(int i=0; i<ranges.Length; i++)
        {
          string rangeString = rangeStrings[i];
          if(rangeString[0] == '-') // if it's a suffix range, it consists of a length taken from the end of the body
          {
            long length;
            if(!long.TryParse(rangeString.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out length)) goto failed;
            ranges[i] = new ByteRange(-1, length, false);
          }
          else // otherwise, it's a normal range taken from the beginning (and possibly extending to the end)
          {
            long start, end = -1;
            int dash = rangeString.IndexOf('-');
            if(!long.TryParse(rangeString.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out start) ||
               dash < rangeString.Length-1 &&
               (!long.TryParse(rangeString.Substring(dash+1), NumberStyles.Integer, CultureInfo.InvariantCulture, out end) ||
                end < start))
            {
              goto failed;
            }
            ranges[i] = new ByteRange(start, end == -1 ? -1 : end, false);
          }
        }

        this.ranges = ranges;
        failed:; // if the Range header is syntatically invalid, RFC 2616 section 14.35.1 requires us to ignore it
      }
    }
  }

  /// <summary>Gets whether the client has submitted a <c>HEAD</c> request rather than a <c>GET</c> request.</summary>
  public bool IsHeadRequest { get; private set; }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> representing the overall result of the request. If the status is null, the
  /// request is assumed to have been successful, but no response is written. (The response is assumed to have been written by the resource
  /// when it processed the request.)
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <summary>Returns an array of satisfiable byte ranges from the HTTP Range header, the order that they would be found in the entity
  /// body, or null if no valid Range header was supplied by the client.
  /// </summary>
  /// <remarks>If the returned array has a length of zero, the range set was unsatisfiable, which requires a special kind of response.
  /// If any case when an array is returned, you should reply in accordance with RFC 2616 sections 14.35, 14.16, and 19.2.
  /// </remarks>
  public ByteRange[] GetByteRanges(long entityLength)
  {
    if(entityLength < 0) throw new ArgumentOutOfRangeException();
    if(ranges == null) return null; // if no valid Range header was specified, return null

    // create a list of the ranges that are satisfiable given the entity length
    ByteRange[] array = new ByteRange[ranges.Length];
    int satisfiable = 0;
    for(int i=0; i<ranges.Length; i++)
    {
      long start = ranges[i].Start, length = ranges[i].Length;
      if(start < entityLength && (length != 0 || start != -1)) // if the range is satisfiable (as per RFC 2616 section 14.35.1)...
      {
        if(start == -1) // if it's a suffix range...
        {
          start = Math.Max(0, entityLength - length); // compute the start
        }
        else if(length == -1) // if it's a range extending to the end of the entity
        {
          length = entityLength - start; // compute the length
        }
        else // otherwise it's a normal range and the 'length' variable is actually the end of the range (exclusive)
        {
          length = Math.Min(entityLength, length) - start + 1;
        }
        array[satisfiable++] = new ByteRange(start, length);
      }
    }

    // if there are multiple ranges, sort and merge them if possible
    if(satisfiable > 1)
    {
      Array.Sort(array, 0, satisfiable, RangeComparer.Instance);
      // it's possible for overlapping ranges to be submitted. for instance, 0-700 and 500-1000. this should be interpreted as the single
      // range 0-1000. walk through the array with two pointers, 'w' (write) which points at the place we would write a newly merged range
      // and 'r' (read) which points to one we would merge array[w] with, where w < r. normally they are one apart (r-w == 1), but if we
      // merge any ranges, then a gap appears, and we have to shift items over
      for(int r=1, w=0; r<satisfiable; w++,r++)
      {
        long end = array[w].Start + array[w].Length;
        if(end >= array[r].Start) // if the ranges abut or overlap...
        {
          array[w] = new ByteRange(array[w].Start, Math.Max(end, array[r].Start + array[r].Length) - array[w].Start); // merge them
          w--; // try merging this same range again
          satisfiable--; // and note that there's one fewer range
        }
        else if(r-w != 1) // otherwise, if any ranges have been merged, shift the remaining items over as we go
        {
          array[w+1] = array[r]; // array[w] points to a valid range, which we don't want to clobber, so shift the range into array[w+1]
        }
      }
    }

    return array.Trim(satisfiable);
  }

  #region IndexItem
  /// <summary>Represents an item displayed in an index.html-like response (typically used by <see cref="WriteSimpleIndexHtml"/>).</summary>
  public sealed class IndexItem
  {
    /// <summary>Initializes a new <see cref="IndexItem"/> representing a file (non-collection member) with the given name.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[@name = 'name']" />
    public IndexItem(string name)
    {
      if(string.IsNullOrEmpty(name)) throw new ArgumentException("The name must not be empty.");
      Name = name;
      Size = -1;
    }

    /// <summary>Initializes a new <see cref="IndexItem"/>.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[@name = 'name' or @name = 'isDirectory']" />
    public IndexItem(string name, bool isDirectory) : this(name)
    {
      IsDirectory = isDirectory;
    }

    /// <summary>Initializes a new <see cref="IndexItem"/> representing a file (non-collection member) with the given name, modification
    /// date, and size.
    /// </summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[@name != 'isDirectory']" />
    public IndexItem(string name, DateTime? lastModificationTime, long size) : this(name, lastModificationTime, size, false) { }

    /// <summary>Initializes a new <see cref="IndexItem"/>.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param" />
    public IndexItem(string name, DateTime? lastModificationTime, long size, bool isDirectory) : this(name, isDirectory)
    {
      if(size < -1) throw new ArgumentOutOfRangeException();
      LastModificationTime = lastModificationTime;
      Size                 = size;
    }

    /// <summary>Initializes a new <see cref="IndexItem"/> from a <see cref="FileSystemInfo"/> object representing a file or directory.</summary>
    public IndexItem(FileSystemInfo fsInfo)
    {
      if(fsInfo == null) throw new ArgumentNullException();
      Name                 = fsInfo.Name;
      LastModificationTime = fsInfo.LastWriteTime;
      IsDirectory          = (fsInfo.Attributes & FileAttributes.Directory) != 0;
      if(IsDirectory)
      {
        Size = -1;
      }
      else
      {
        FileInfo fileInfo = (FileInfo)fsInfo;
        Size = fileInfo.Length;
        Type = fileInfo.Extension;
        if(!string.IsNullOrEmpty(Type)) Type = Type.Substring(1); // remove the leading period from the extension
      }
    }

    /// <summary>Gets or sets the time when the item's content was last changed, or null if the time is unknown.</summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>Gets the name of the item, which is also the path segment appended to the request URL to form the URL of the item's
    /// resource.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the size of the item's content, or -1 if the size is unknown or inapplicable.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets a string describing the type of the item. If null, a default type will be shown for directories but not
    /// files.
    /// </summary>
    public string Type { get; set; }

    /// <summary>Gets or sets whether the item represents a directory (collection member) intsead of a file (non-collection member).</summary>
    public bool IsDirectory { get; set; }
  }
  #endregion

  /// <summary>Writes a simple index.html-like response based on a set of items assumed to be children of a collection at request URL.</summary>
  /// <param name="children">An array of items to display in the index.</param>
  /// <remarks>This method writes a simple HTML table that supports sorting based on query string parameters. The <c>sort</c> parameter can
  /// be "name", "type", "size", or "date" to sort by name, item type, size, or last modification date respectively. The <c>desc</c>
  /// parameter can be "0" or "1" to sort by ascending or descending order, or it can be omitted to sort by the column's default order.
  /// </remarks>
  public void WriteSimpleIndexHtml(IEnumerable<IndexItem> children)
  {
    if(children == null) throw new ArgumentNullException();

    string basePath = Context.RequestPath;
    // make sure the basePath has a trailing slash so we can add items to it (and also to simplify the location of the parent directory)
    if(basePath.Length != 0 && basePath[basePath.Length-1] != '/') basePath += "/";

    // begin writing the response
    StringBuilder sb = new StringBuilder(4096);
    string htmlPath = HttpUtility.HtmlEncode(Context.ServiceRoot + basePath);

    // create an HTML table
    sb.Append("<html>\n<head><title>").Append(htmlPath)
      .Append(" contents</title>\n<style>TD,TH { padding:2px 3px; }</style>\n</head>\n<body>\n<h2>").Append(htmlPath)
      .Append("</h2>\n<table>\n<tr>");

    // add headers with links for sorting the table
    string[] headers = new string[] { "name", "Name", "type", "Type", "size", "Size", "date", "Modification Date" };
    string sort = Context.Request.QueryString["sort"]; // the current sort type from the query string
    bool reverse = false;
    for(int i=0; i<headers.Length; i+=2)
    {
      sb.Append("<th><a href=\"?sort=").Append(headers[i]);
      if(string.Equals(sort, headers[i], StringComparison.Ordinal))
      {
        string desc = Context.Request.QueryString["desc"];
        reverse = !string.IsNullOrEmpty(desc) ? string.Equals(desc, "1", StringComparison.Ordinal)
                                              : string.Equals(sort, "date", StringComparison.Ordinal);
        sb.Append(reverse ? "&desc=0" : "&desc=1");
      }
      sb.Append("\">").Append(headers[i+1]).Append("</a></th>");
    }
    sb.Append("</tr>\n");

    // add a link to the parent collection
    if(!string.IsNullOrEmpty(basePath))
    {
      int slash = basePath.LastIndexOf('/', basePath.Length-2);
      sb.Append("<tr><td><a href=\"")
        .Append(HttpUtility.HtmlEncode(Context.ServiceRoot + (slash == -1 ? null : basePath.Substring(0, slash+1))))
        .Append("\">&lt;parent directory&gt;</a></td><td>&lt;DIR&gt;</td><td></td><td></td></tr>\n");
    }

    // sort the children to put directories first and ensure they're in sorted order
    IndexItem[] childArray = System.Linq.Enumerable.ToArray(children);
    {
      Comparison<IndexItem> secondComparison; // the comparison to use after files and directories have been separated
      Comparison<IndexItem> thirdComparison = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
      if(string.Equals(sort, "type", StringComparison.Ordinal))
      {
        secondComparison = (a, b) => string.Compare(a.Type, b.Type, StringComparison.CurrentCultureIgnoreCase);
      }
      else if(string.Equals(sort, "date"))
      {
        secondComparison = (a, b) => Comparer<DateTime?>.Default.Compare(a.LastModificationTime, b.LastModificationTime);
      }
      else if(string.Equals(sort, "size"))
      {
        secondComparison = (a, b) => a.Size.CompareTo(b.Size);
      }
      else
      {
        secondComparison = thirdComparison;
        thirdComparison  = null;
      }
      Array.Sort(childArray, (a, b) =>
      {
        if(a == b) return 0;
        if(a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
        int cmp = secondComparison(a, b);
        if(cmp != 0) return reverse ? -Math.Sign(cmp) : cmp;
        return thirdComparison == null ? 0 : thirdComparison(a, b);
      });
    }

    // write links to the items
    basePath = HttpUtility.HtmlEncode(Context.ServiceRoot + basePath);
    foreach(IndexItem item in childArray)
    {
      if(item == null) throw new ArgumentException("An item was null.");
      string encodedName = HttpUtility.HtmlEncode(item.Name), encodedType = HttpUtility.HtmlEncode(item.Type);
      sb.Append("<tr><td><a href=\"").Append(basePath).Append(encodedName).Append("\">").Append(encodedName).Append("</a></td><td>")
        .Append(encodedType ?? (item.IsDirectory ? "&lt;DIR&gt;" : null)).Append("</td><td>")
        .Append(item.Size == -1 ? null : GetSizeString(item.Size)).Append("</td><td>")
        .Append(!item.LastModificationTime.HasValue ?
                  null : item.LastModificationTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        .Append("</td></tr>\n");
    }

    // finish the response
    sb.Append("</table>\n</body>\n</html>\n");

    // and write it out with WriteStandardResponse
    Context.Response.ContentEncoding = Encoding.UTF8;
    WriteStandardResponse(new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())), "text/html");
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[@name != 'entityLength' and @name != 'contentType']" />
  public void WriteStandardResponse(Stream entityBody)
  {
    WriteStandardResponse(entityBody, -1, null);
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[@name != 'contentType']" />
  public void WriteStandardResponse(Stream entityBody, long entityLength)
  {
    WriteStandardResponse(entityBody, entityLength, null);
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[@name != 'entityLength']" />
  public void WriteStandardResponse(Stream entityBody, string contentType)
  {
    WriteStandardResponse(entityBody, -1, contentType);
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*" />
  public void WriteStandardResponse(Stream entityBody, long entityLength, string contentType)
  {
    if(entityBody == null) throw new ArgumentNullException();
    if(entityLength < -1) throw new ArgumentOutOfRangeException();
    if(entityLength == -1 && entityBody.CanSeek) entityLength = entityBody.Length; // try to discover the entity length if we can

    // notify the client that we accept byte ranges in the Range header (see RFC 2616 section 14.5)
    Context.Response.AppendHeader("Accept-Ranges", "bytes");

    ByteRange[] ranges = entityLength == -1 ? null : GetByteRanges(entityLength);
    if(ranges != null && ranges.Length == 0) // if a Ranges header was specified but unsatisfiable...
    {
      Status = ConditionCodes.RequestedRangeNotSatisfiable;
    }
    else if(ranges != null && ranges.Length != 1) // if multiple ranges were specified, then we should send a multipart/byteranges response
    {
      Context.Response.StatusCode = (int)HttpStatusCode.PartialContent;

      if(IsHeadRequest) // if it's a HEAD request, then we can't actually send a body
      {
        Context.Response.ContentType = "multipart/byteranges";
      }
      else
      {
        string boundary = DAVUtility.CreateMimeBoundary(), header = "\r\n--" + boundary + "\r\n";
        if(!string.IsNullOrEmpty(contentType)) header += "Content-Type: " + DAVUtility.HeaderEncode(contentType) + "\r\n";
        header += "Content-range: bytes ";

        Context.Response.ContentType = "multipart/byteranges; boundary=" + boundary;
        Context.Response.BufferOutput = false;
        long offset = 0;
        for(int i=0; i<ranges.Length; i++)
        {
          ByteRange range = ranges[i];

          // write the MIME part for this range
          Context.Response.Write(header);
          Context.Response.Write(range.Start.ToInvariantString() + "-" + (range.Start + range.Length - 1).ToInvariantString() + "/" +
                                 entityLength.ToInvariantString() + "\r\n\r\n");
          WriteStreamRange(entityBody, range.Start - offset, range.Length);
          if(!entityBody.CanSeek) offset = range.Start + range.Length;
        }
        Context.Response.Write("\r\n--" + boundary + "--\r\n");
      }
    }
    else
    {
      long rangeStart = 0, rangeLength = entityLength;
      if(ranges != null && ranges.Length == 1)
      {
        rangeStart  = ranges[0].Start;
        rangeLength = ranges[0].Length;
      }

      if(!string.IsNullOrEmpty(contentType)) Context.Response.ContentType = contentType;
      if(rangeLength != -1) Context.Response.AppendHeader("Content-Length", rangeLength.ToInvariantString());
      if(rangeStart == 0 && rangeLength == entityLength) // if we're sending the whole file...
      {
        Context.Response.StatusCode = (int)HttpStatusCode.OK; // then use a 200 OK response
      }
      else // otherwise, we're sending only a portion
      {
        Context.Response.StatusCode = (int)HttpStatusCode.PartialContent; // so use a 206 Partial Content response
        // let the client know which part of the file was sent
        string rangeValue = "bytes " + rangeStart.ToInvariantString() + "-" + (rangeStart + rangeLength - 1).ToInvariantString() +
                            "/" + entityLength.ToInvariantString();
        Context.Response.AppendHeader("Content-Range", rangeValue);
      }

      if(!IsHeadRequest)
      {
        Context.Response.BufferOutput = false;
        WriteStreamRange(entityBody, rangeStart, rangeLength); // write the entity body unless it's a HEAD request
      }
    }
  }

  /// <summary>Copies a range of bytes from a stream to the HTTP response stream. Note that the behavior of this method differs depending
  /// on whether the stream is seekable.
  /// </summary>
  /// <param name="stream">The stream whose contents will be written.</param>
  /// <param name="offset">If the stream is seekable, this represents the absolute position within the stream from which data will be
  /// copied. If the stream is not seekable, this represents the number of bytes to skip before beginning to copy data. If the stream is
  /// positioned at the beginning, the result is the same either way.
  /// </param>
  /// <param name="length">The number of bytes to copy from the stream, or -1 to copy the stream to the end.</param>
  /// <remarks>If you can easily supply a <see cref="FileStream"/> you should, since this method has especially efficient handling of
  /// <see cref="FileStream"/> objects.
  /// </remarks>
  public void WriteStreamRange(Stream stream, long offset, long length)
  {
    if(stream == null) throw new ArgumentNullException();
    if(offset < 0 || length < -1) throw new ArgumentOutOfRangeException();

    if(stream.CanSeek)
    {
      FileStream fileStream = stream as FileStream;
      if(fileStream != null)
      {
        Context.Response.WriteFile(fileStream.SafeFileHandle.DangerousGetHandle(), offset, length);
        return;
      }
      else
      {
        stream.Position = offset;
        offset = 0;
      }
    }

    if(offset != 0) stream.Skip(offset);

    stream.Process(4096, true, (buffer, bytesInBuffer) =>
    {
      int bytesToWrite = (int)Math.Min(bytesInBuffer, length);
      Context.Response.BinaryWrite(bytesToWrite == buffer.Length ? buffer : buffer.Trim(bytesToWrite));
      if(length != -1) length -= bytesToWrite;
      return length != 0;
    });
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>The default implementation does not write any response if <see cref="Status"/> is null. Otherwise, it writes a response
  /// based on the status.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(Status != null) Context.WriteStatusResponse(Status);
  }

  #region RangeComparer
  sealed class RangeComparer : IComparer<ByteRange>
  {
    private RangeComparer() { }

    public int Compare(ByteRange a, ByteRange b)
    {
      return a.Start.CompareTo(b.Start);
    }

    public static readonly RangeComparer Instance = new RangeComparer();
  }
  #endregion

  readonly ByteRange[] ranges;

  static string GetSizeString(long size)
  {
    const long KB = 1024, MB = KB*1024, GB = MB*1024, TB = GB*1024, PB = TB*1024, EB = PB*1024;
    return size < KB ? size.ToInvariantString() :
           size < MB ? (size / (double)KB).ToString("f2", CultureInfo.InvariantCulture) + " KiB" :
           size < GB ? (size / (double)MB).ToString("f2", CultureInfo.InvariantCulture) + " MiB" :
           size < TB ? (size / (double)GB).ToString("f2", CultureInfo.InvariantCulture) + " GiB" :
           size < PB ? (size / (double)TB).ToString("f2", CultureInfo.InvariantCulture) + " TiB" :
           size < EB ? (size / (double)PB).ToString("f2", CultureInfo.InvariantCulture) + " PiB" :
                       (size / (double)EB).ToString("f2", CultureInfo.InvariantCulture) + " EiB";
  }

  static readonly Regex reRanges =
    new Regex(@"^\s*bytes\s*=\s*(?<ranges>(?:\d+\s*-\s*\d*|-\s*\d+)(?:\s*,\s*(?:\d+\s*-\s*\d*|-\s*\d+))*)\s*$", RegexOptions.Compiled);
}
#endregion

} // namespace HiA.WebDAV.Server
