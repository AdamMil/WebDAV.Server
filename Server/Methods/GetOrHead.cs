/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2016 by Adam Milazzo.

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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using AdamMil.IO;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

#region ByteRange
/// <summary>Represents a range of bytes within an entity response, used to enable partial responses.</summary>
public struct ByteRange : IEquatable<ByteRange>
{
  /// <summary>Initializes a new <see cref="ByteRange"/> based on the given start position and length.</summary>
  public ByteRange(long start, long length) : this(start, length, true) { }

  internal ByteRange(long start, long length, bool validate)
  {
    if(validate && (start < 0 || length <= 0 || (start + length) < 0)) throw new ArgumentOutOfRangeException();
    _start  = start;
    _length = length;
  }

  /// <inheritdoc/>
  public override bool Equals(object obj)
  {
    return obj is ByteRange && Equals((ByteRange)obj);
  }

  /// <summary>Determines whether this <see cref="ByteRange"/> object equals the given <see cref="ByteRange"/> object.</summary>
  public bool Equals(ByteRange range)
  {
    return range.Length == Length && range.Start == Start;
  }

  /// <inheritdoc/>
  public override int GetHashCode()
  {
    return Length.GetHashCode() ^ Start.GetHashCode();
  }

  /// <summary>Determines whether two <see cref="ByteRange"/> values are equal.</summary>
  public static bool operator==(ByteRange a, ByteRange b)
  {
    return a._length == b._length && a._start == b._start;
  }

  /// <summary>Determines whether two <see cref="ByteRange"/> values are unequal.</summary>
  public static bool operator!=(ByteRange a, ByteRange b)
  {
    return a._length != b._length || a._start != b._start;
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return Start.ToStringInvariant() + " - " + End.ToStringInvariant() + " (" + Length.ToStringInvariant() + ")";
  }

  /// <summary>Gets the length the range, in bytes.</summary>
  public long Length
  {
    get { return _length; }
  }

  /// <summary>Gets the start of the range, in bytes.</summary>
  public long Start
  {
    get { return _start; }
  }

  /// <summary>Gets the exclusive end of the range, in bytes.</summary>
  public long End
  {
    get { return Start + Length; }
  }

  long _length, _start;
}
#endregion

#region GetOrHeadRequest
/// <summary>Represents a <c>GET</c> or <c>HEAD</c> request.</summary>
/// <remarks>
/// <para>The <c>GET</c> and <c>HEAD</c> requests are described in sections 4.3.1 and 4.3.2 of RFC 7231 and section 9.4 of RFC 4918.
/// Requests for partial content are described in RFC 7233. To service a <c>GET</c> or <c>HEAD</c> request, you can normally just call the
/// <see cref="WriteStandardResponse{T}(IStandardResource{T})"/> or <see cref="WriteSimpleIndexHtml{T}(IStandardResource{T})"/> method or
/// one of their overrides.
/// </para>
/// <para>If you would like to handle it yourself, you should send an entity body to the client along with valid <c>Content-Length</c>,
/// <c>Content-Type</c>, <c>ETag</c>, and <c>Last-Modified</c> headers. (For a <c>HEAD</c> request, the same status and headers should be
/// sent but the body should be omitted.) The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/> (default)</term>
///   <description>The body of the resource representation is returned to the client. This status code should be used even for a <c>HEAD</c>
///     request (which omits the entity body), since a <c>HEAD</c> request should return the same status as the corresponding <c>GET</c>
///     request.
///   </description>
/// </item>
/// <item>
///   <term>206 <see cref="ConditionCodes.PartialContent">Partial Content</see></term>
///   <description>The server is sending one or more fragments of the entity body in response to a request for partial content. Multiple
///     fragments must be sent as a <c>multipart/byteranges</c> response. (See RFC 7233 for information about partial content.)
///   </description>
/// </item>
/// <item>
///   <term>304 <see cref="ConditionCodes.NotModified">Not Modified</see></term>
///   <description>An <c>If-Modified-Since</c> or <c>If-None-Match</c> precondition was false but other preconditions (if any) were true.</description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The client does not have access to the resource.</description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A precondition other than <c>If-Modified-Since</c> and <c>If-None-Match</c> was false.</description>
/// </item>
/// <item>
///   <term>416 <see cref="ConditionCodes.RequestedRangeNotSatisfiable">Requested Range Not Satisfiable</see></term>
///   <description>The client requested partial content, but none of the requested ranges overlapped the range of available content.</description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="WriteSimpleIndexHtml(IEnumerable{IndexItem})"/></term>
///   <description>You want to change how the index.html-like response is rendered.</description>
/// </item>
/// <item>
///   <term><see cref="WriteStandardResponse(Stream,EntityMetadata)"/></term>
///   <description>You want to change how entity bodies are sent to the client.</description>
/// </item>
/// </list>
/// </remarks>
public class GetOrHeadRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="GetOrHeadRequest"/> based on a new WebDAV request.</summary>
  public GetOrHeadRequest(WebDAVContext context) : base(context)
  {
    // try to parse the HTTP Range header if it was specified
    string rangeHeader = context.Request.Headers[DAVHeaders.Range];
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
            if(!InvariantCultureUtility.TryParse(rangeString, 1, rangeString.Length-1, out length)) goto failed;
            ranges[i] = new ByteRange(-1, length, false);
          }
          else // otherwise, it's a normal range taken from the beginning (and possibly extending to the end)
          {
            long start, end = -1;
            int dash = rangeString.IndexOf('-');
            if(!InvariantCultureUtility.TryParse(rangeString, 0, dash, out start) ||
               dash < rangeString.Length-1 &&
               (!InvariantCultureUtility.TryParse(rangeString, dash+1, rangeString.Length-(dash+1), out end) || end < start))
            {
              goto failed;
            }
            ranges[i] = new ByteRange(start, end == -1 ? -1 : end-start+1, false);
          }
        }

        this.ranges = ranges;
        failed:; // if we don't understand the Range header, RFC 7233 section 3.1 requires us to ignore it
      }
    }

    // if a valid Range header was given, try to parse the HTTP If-Range header too
    if(this.ranges != null)
    {
      rangeHeader = Context.Request.Headers[DAVHeaders.IfRange];
      if(!string.IsNullOrEmpty(rangeHeader))
      {
        rangeHeader = rangeHeader.Trim();
        if(rangeHeader.Length >= 2)
        {
          if(rangeHeader[0] == '"' || rangeHeader[0] == 'W' && rangeHeader[1] == '/') // it looks like an entity tag...
          {
            EntityTag tag = EntityTag.TryParse(rangeHeader); // RFC 7233 3.2 disallows weak entity tags in If-Range
            if(tag == null || tag.IsWeak) throw Exceptions.BadRequest("Invalid entity tag for If-Range header.");
            IfRange = tag;
          }
          else // it doesn't look like an entity tag so it's assumed to be a date
          {
            DateTime time;
            if(DAVUtility.TryParseHttpDate(rangeHeader, out time)) IfRange = time;
          }
        }

        if(IfRange == null && !string.IsNullOrEmpty(rangeHeader)) throw Exceptions.BadRequest("Invalid If-Range header.");
      }
    }

    IsHeadRequest = context.Request.HttpMethod.OrdinalEquals(DAVMethods.Head);
  }

  /// <summary>Gets the value parsed from the HTTP <c>If-Range</c> header. This is either null (if the header was unspecified or invalid)
  /// or an <see cref="EntityTag"/> or a <see cref="DateTime"/> (which is interpreted as a modification date).
  /// </summary>
  /// <remarks>This property is automatically used by <see cref="WriteStandardResponse(Stream,EntityMetadata)"/>. You only need to use it
  /// if you process the request manually. If null or if the entity has not been modified (as determined by the entity tag or modification
  /// date exactly matching the current values), the request should be processed normally. Otherwise, if the entity has been modified (i.e.
  /// if the entity tag or the modification date doesn't match exactly), the request should be processed as though no <c>Range</c> header
  /// was submitted. That is, the <see cref="GetByteRanges"/> method should not be used (or should be considered to have returned null) and
  /// the entire entity body should be returned to the client.
  /// </remarks>
  public object IfRange { get; private set; }

  /// <summary>Gets whether the client has submitted a <c>HEAD</c> request rather than a <c>GET</c> request.</summary>
  public bool IsHeadRequest { get; private set; }

  /// <summary>Returns an array of satisfiable byte ranges from the HTTP Range header, in the order that they would be found in the entity
  /// body, or null if no valid Range header was supplied by the client.
  /// </summary>
  /// <remarks>If the returned array has a length of zero, the range set was unsatisfiable, which requires a special kind of response.
  /// If any case when an array is returned, you should reply in accordance with RFC 7233 section 4.
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
      if(start < entityLength && (length != 0 || start != -1)) // if the range is satisfiable (as per RFC 7233 section 2.1)...
      {
        if(start == -1) // if it's a suffix range...
        {
          start = Math.Max(0, entityLength - length); // compute the start
        }
        else if(length == -1) // if it's a range extending to the end of the entity
        {
          length = entityLength - start; // compute the length
        }
        else // otherwise it's a normal range
        {
          length = Math.Min(entityLength-start, length);
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
      for(int r=1, w=0, count=satisfiable; r<count; w++,r++)
      {
        long end = array[w].Start + array[w].Length;
        if(end >= array[r].Start - 125) // if the ranges abut or overlap or if the gap is smaller than the approximate per-range overhead..
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
    /// <summary>Initializes a new <see cref="IndexItem"/> representing a file (non-collection member) with the given path segment.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[not(@name='pathSegment')]" />
    /// <remarks>The name of the item will be initialized by URL-decoding the path segment and removing any trailing slash.</remarks>
    public IndexItem(string pathSegment) : this(pathSegment, DAVUtility.RemoveTrailingSlash(DAVUtility.UriPathDecode(pathSegment))) { }

    /// <summary>Initializes a new <see cref="IndexItem"/> with the given path segment.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[not(@name='pathSegment') or not(@name='isDirectory')]" />
    /// <remarks>The name of the item will be initialized by URL-decoding the path segment and removing any trailing slash.</remarks>
    public IndexItem(string pathSegment, bool isDirectory) : this(pathSegment)
    {
      IsDirectory = isDirectory;
    }

    /// <summary>Initializes a new <see cref="IndexItem"/> representing a file (non-collection member) with the given name and path
    /// segment.
    /// </summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param[@name='name' or @name='pathSegment']" />
    public IndexItem(string pathSegment, string name)
    {
      if(string.IsNullOrEmpty(pathSegment) || string.IsNullOrEmpty(name))
      {
        throw new ArgumentException("The name and path must not be empty.");
      }
      Name        = name;
      PathSegment = pathSegment;
      Size        = -1;
    }

    /// <summary>Initializes a new <see cref="IndexItem"/>.</summary>
    /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/ItemIndex/Cons/param" />
    public IndexItem(string pathSegment, string name, bool isDirectory) : this(pathSegment, name)
    {
      IsDirectory = isDirectory;
    }

    /// <summary>Gets or sets the time when the item's content was last changed, or null if the time is unknown. The default is null.</summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>Gets the name of the item, which may be different from the <see cref="PathSegment"/> used to construct the item's URL.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the canonical (minimally escaped) path segment appended to the request URL to form the URL of the item's resource.
    /// See <see cref="DAVUtility.CanonicalSegmentEncode"/> for details on minimally escaped path segments.
    /// </summary>
    public string PathSegment { get; private set; }

    /// <summary>Gets or sets the size of the item's content, or -1 if the size is unknown or inapplicable. The default is -1.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets a string describing the type of the item. If null, a default type will be shown for directories but not
    /// files. The default is null.
    /// </summary>
    public string Type { get; set; }

    /// <summary>Gets or sets whether the item represents a directory (collection member) intsead of a file (non-collection member).</summary>
    public bool IsDirectory { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
      return Name;
    }
  }
  #endregion

  /// <summary>Writes a simple index.html-like response listing the children of an <see cref="IStandardResource{T}"/>.</summary>
  public void WriteSimpleIndexHtml<T>(IStandardResource<T> requestResource) where T : IStandardResource<T>
  {
    if(requestResource == null) throw new ArgumentNullException();
    IEnumerable<T> children = requestResource.GetChildren(Context);
    var childCollection = children as ICollection<T>; // get the number of children if we can
    List<IndexItem> items = childCollection == null ? new List<IndexItem>() : new List<IndexItem>(childCollection.Count);
    if(children != null)
    {
      foreach(IStandardResource<T> child in children)
      {
        EntityMetadata metadata = child.GetEntityMetadata(false);
        IndexItem item = new IndexItem(child.GetMemberName(Context), child.IsCollection);
        item.LastModificationTime = metadata.LastModifiedTime;
        if(metadata.Length.HasValue) item.Size = metadata.Length.Value;
        if(!item.IsDirectory)
        {
          string extension = Path.GetExtension(item.Name);
          if(!string.IsNullOrEmpty(extension)) item.Type = extension.Substring(1); // use the extension w/o a leading period as file type
        }
        items.Add(item);
      }
    }
    WriteSimpleIndexHtml(items);
  }

  /// <summary>Writes a simple index.html-like response based on a set of items assumed to be children of a collection at request URL.</summary>
  /// <param name="children">An array of items to display in the index.</param>
  /// <remarks>This method writes a simple HTML table that supports sorting based on query string parameters. The <c>sort</c> parameter can
  /// be "name", "type", "size", or "date" to sort by name, item type, size, or last modification date respectively. The <c>desc</c>
  /// parameter can be "0" or "1" to sort by ascending or descending order, or it can be omitted to sort by the column's default order.
  /// </remarks>
  public virtual void WriteSimpleIndexHtml(IEnumerable<IndexItem> children)
  {
    if(children == null) throw new ArgumentNullException();

    // make sure the basePath has a trailing slash so we can add items to it (and also to simplify the location of the parent directory)
    string basePath = DAVUtility.WithTrailingSlash(Context.RequestPath);

    // begin writing the response
    StringBuilder sb = new StringBuilder(4096);
    string htmlPath = HttpUtility.HtmlEncode(Context.ServiceRoot + basePath);

    // create an HTML table
    sb.Append("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">\n");
    sb.Append("<html>\n<head>\n<base href=\"").Append(htmlPath).Append("\" />\n<title>").Append(htmlPath).Append(" contents</title>\n");
    sb.Append("<style type=\"text/css\">TD,TH { padding:2px 3px; font-family:monospace; text-align:left; }</style>\n</head>\n");
    sb.Append("<body>\n<h3>").Append(htmlPath).Append("</h3>\n<table>\n<tr>");

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
      children = children.Order((a, b) =>
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
    foreach(IndexItem item in children)
    {
      if(item == null) throw new ArgumentException("An item was null.");
      string encodedName = HttpUtility.HtmlEncode(item.Name), encodedType = HttpUtility.HtmlEncode(item.Type);
      string encodedUrl  = HttpUtility.HtmlEncode(DAVUtility.UriPathPartialEncode(item.PathSegment));
      if(item.IsDirectory) encodedUrl = DAVUtility.WithTrailingSlash(encodedUrl);
      sb.Append("<tr><td><a href=\"").Append(encodedUrl).Append("\">").Append(encodedName).Append("</a></td><td>")
        .Append(encodedType ?? (item.IsDirectory ? "&lt;DIR&gt;" : null)).Append("</td><td>")
        .Append(item.Size < 0 ? null : GetSizeString(item.Size)).Append("</td><td>")
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

  /// <summary>Processes a standard <c>GET</c> or <c>HEAD</c> request for an <see cref="IStandardResource{T}"/>. The resource's data stream
  /// from <see cref="IStandardResource.OpenStream"/> will be sent to the client if it's not null. Otherwise, if the resource is a
  /// directory, an HTML index page will be sent. Otherwise, an empty body will be sent.
  /// </summary>
  public void WriteStandardResponse<T>(IStandardResource<T> requestResource) where T : IStandardResource<T>
  {
    if(requestResource == null) throw new ArgumentNullException();
    using(Stream stream = requestResource.OpenStream(Context)) // collections can theoretically have data streams...
    {
      if(stream == null && requestResource.IsCollection) WriteSimpleIndexHtml(requestResource); // but if they don't, write an index
      else WriteStandardResponse(stream ?? new MemoryStream()); // otherwise, write the stream (or an empty stream if none)
    }
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[not(@name='mediaType') and not(@name='metadata')]" />
  /// <remarks>This method is intended to be used with dynamically generated output. The
  /// <see cref="WriteStandardResponse(Stream,EntityMetadata)"/> override is preferred if you have metadata about the entity body.
  /// </remarks>
  public void WriteStandardResponse(Stream entityBody)
  {
    WriteStandardResponse(entityBody, (EntityMetadata)null);
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[not(@name='metadata')]" />
  /// <remarks>This method is intended to be used with dynamically generated output. The
  /// <see cref="WriteStandardResponse(Stream,EntityMetadata)"/> override is preferred if you have additional metadata about the entity
  /// body.
  /// </remarks>
  public void WriteStandardResponse(Stream entityBody, string mediaType)
  {
    WriteStandardResponse(entityBody, new EntityMetadata() { MediaType = mediaType });
  }

  /// <include file="documentation.xml" path="/DAV/GetOrHeadRequest/WriteStandardResponse/*[not(@name='mediaType')]" />
  public virtual void WriteStandardResponse(Stream entityBody, EntityMetadata metadata)
  {
    if(entityBody == null) throw new ArgumentNullException();
    if(!entityBody.CanRead) throw new ArgumentException("The entity body cannot be read.");

    // notify the client that we accept byte ranges in the Range header (see RFC 7233 section 2.3)
    Context.Response.Headers[DAVHeaders.AcceptRanges] = "bytes";

    metadata = metadata == null ? new EntityMetadata() : metadata.Clone();
    if(!metadata.Length.HasValue && entityBody.CanSeek) metadata.Length = entityBody.Length;

    ByteRange[] ranges = metadata.Length.HasValue ? GetByteRanges(metadata.Length.Value) : null;
    if(ranges != null && ranges.Length == 0) // if a Ranges header was specified but unsatisfiable...
    {
      // RFC 7233 section 4.4 says that a 416 response should include a Content-Range header giving the entity length (if known)
      Context.Request.Headers[DAVHeaders.ContentRange] = new ContentRange(metadata.Length.Value).ToHeaderString();
      Status = ConditionCodes.RequestedRangeNotSatisfiable; // then it's an error
    }
    else
    {
      // if no entity tag was provided and the stream is seekable, compute the entity tag
      if(metadata.EntityTag == null)
      {
        if(!string.IsNullOrEmpty(Context.Response.Headers[DAVHeaders.ETag])) // if an entity tag was set in the headers...
        {
          metadata.EntityTag = new EntityTag(Context.Response.Headers[DAVHeaders.ETag]); // use that
        }
        else if(entityBody.CanSeek) // otherwise, if we can make a scan through the entity body...
        {
          metadata.EntityTag = DAVUtility.ComputeEntityTag(entityBody, true); // compute an entity tag from the body
          entityBody.Position = 0;
        }
      }

      // if no last modified time was provided, try to take it from the header if set
      if(!metadata.LastModifiedTime.HasValue && !string.IsNullOrEmpty(Context.Response.Headers[DAVHeaders.LastModified]))
      {
        DateTime time;
        if(DAVUtility.TryParseHttpDate(Context.Response.Headers[DAVHeaders.LastModified], out time)) metadata.LastModifiedTime = time;
      }

      ConditionCode precondition = CheckPreconditions(metadata);
      if(precondition != null && precondition.IsError)
      {
        Status = precondition;
      }
      else
      {
        // set the ETag and Last-Modified headers if they weren't already set
        if(metadata.EntityTag != null && string.IsNullOrEmpty(Context.Response.Headers[DAVHeaders.ETag]))
        {
          Context.Response.Headers[DAVHeaders.ETag] = metadata.EntityTag.ToHeaderString();
        }
        if(metadata.LastModifiedTime.HasValue)
        {
          DateTime lastModifiedTime = DAVUtility.GetHttpDate(metadata.LastModifiedTime.Value);
          if(string.IsNullOrEmpty(Context.Response.Headers[DAVHeaders.LastModified]))
          {
            Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(lastModifiedTime);
          }
        }

        // if the If-Range header was sent but doesn't match the current entity body...
        if(IfRange != null)
        {
          EntityTag tag = IfRange as EntityTag;
          if(tag != null ? !tag.StronglyEquals(metadata.EntityTag) // RFC 7233 section 3.2 requires an exact (strong) entity tag match
                         : !metadata.LastModifiedTime.HasValue ||
                           DAVUtility.GetHttpDate(metadata.LastModifiedTime.Value) != (DateTime)IfRange) // or an exact date match
          {
            ranges = null; // then send the entire entity to the client rather than only the requested range
          }
        }

        ContentEncoding contentEncoding = Context.ChooseResponseEncoding(metadata.ShouldCompress());
        Context.Response.SetContentEncodingHeader(contentEncoding); // make sure the Content-Encoding gets set even if we don't send a body
        bool sentBody = false;
        if(ranges != null && ranges.Length != 1) // if multiple ranges were specified, then we should send a multipart/byteranges response
        {
          sentBody = WriteMultipartResponse(entityBody, metadata, ranges, precondition, contentEncoding);
        }
        else // otherwise, send the whole entity or a single range from it as a regular response
        {
          sentBody = WriteSinglePartResponse(entityBody, metadata, ranges, precondition, contentEncoding);
        }

        // if we didn't send a body and won't be sending one later (i.e. if this is a HEAD request or a 304 Not Modified response) and we
        // didn't set a Content-Length header, then set an empty header in order to prevent IIS/ASP.NET from adding Content-Length: 0. the
        // empty header won't actually be sent to the client.
        if(!sentBody && Status == null && Context.Response.Headers[DAVHeaders.ContentLength] == null)
        {
          Context.Response.Headers[DAVHeaders.ContentLength] = "";
        }
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks><note type="inherit">The default implementation does not write any response if <see cref="WebDAVRequest.Status"/> is null.
  /// Otherwise, it writes a response based on the status.
  /// </note></remarks>
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

  bool WriteMultipartResponse(Stream entityBody, EntityMetadata metadata, ByteRange[] ranges, ConditionCode precondition,
                              ContentEncoding contentEncoding)
  {
    Context.Response.SetStatus(ConditionCodes.PartialContent);
    if(precondition != null || IsHeadRequest)
    {
      Status = precondition;
      Context.Response.SetContentType("multipart/byteranges");
    }

    // if we need to send the Content-Length header and/or the body itself...
    if(precondition == null && (!IsHeadRequest || contentEncoding == ContentEncoding.Identity))
    {
      // compute the MIME Content-Range headers all at once so we can compute the content length without creating the strings twice
      string[] rangeHeaders = new string[ranges.Length];
      for(int i=0; i<rangeHeaders.Length; i++)
      {
        rangeHeaders[i] = ranges[i].Start.ToStringInvariant() + "-" + (ranges[i].Start + ranges[i].Length - 1).ToStringInvariant() +
                              "/" + metadata.Length.Value.ToStringInvariant() + "\r\n\r\n";
      }

      // if the response is not compressed and this isn't a 304 Not Modified response (see expl. below), send the Content-Length header
      if(contentEncoding == ContentEncoding.Identity)
      {
        const int BoundaryLength = 69; // the length of the boundary string returned by DAVUtility.CreateMimeBoundary()
        int headerLength = BoundaryLength + 6 + DAVHeaders.ContentRange.Length + 8; // "\r\n--BOUNDARY\r\n...Content-Range: bytes "
        if(!string.IsNullOrEmpty(metadata.MediaType)) headerLength += DAVHeaders.ContentType.Length + metadata.MediaType.Length + 4;
        long contentLength = headerLength*ranges.Length-2 + BoundaryLength + 8; // the boundaries and footer ("\r\n--BOUNDARY--\r\n")
        for(int i=0; i<ranges.Length; i++) contentLength += rangeHeaders[i].Length + ranges[i].Length;
        Context.Response.Headers[DAVHeaders.ContentLength] = contentLength.ToStringInvariant();
      }

      if(!IsHeadRequest) // if we need to send the body...
      {
        string boundary = DAVUtility.CreateMimeBoundary(), header = "\r\n--" + boundary + "\r\n";
        if(!string.IsNullOrEmpty(metadata.MediaType))
        {
          header += DAVHeaders.ContentType + ": " + metadata.MediaType + "\r\n";
        }
        header += DAVHeaders.ContentRange + ": bytes ";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        Context.Response.SetContentType("multipart/byteranges; boundary=" + boundary);
        using(Stream outputStream = Context.OpenResponseBody(contentEncoding, true))
        {
          // if we're not encoding the stream, pass Response.OutputStream to WriteStreamRange so it can use the WriteFile method
          Stream stream = contentEncoding == ContentEncoding.Identity ? Context.Response.OutputStream : outputStream;

          long offset = 0;
          for(int i=0; i<ranges.Length; i++)
          {
            ByteRange range = ranges[i];

            // write the MIME part for this range
            int skip = i == 0 ? 2 : 0; // skip the initial CRLF pair for the first chunk
            stream.Write(headerBytes, skip, headerBytes.Length-skip);
            WriteAsciiString(stream, rangeHeaders[i]);
            WriteStreamRange(stream, entityBody, range.Start - offset, range.Length);
            if(!entityBody.CanSeek) offset = range.Start + range.Length;
          }
          WriteAsciiString(stream, "\r\n--" + boundary + "--\r\n");
        }

        return true;
      }
    }

    return false;
  }

  bool WriteSinglePartResponse(Stream entityBody, EntityMetadata metadata, ByteRange[] ranges, ConditionCode precondition,
                               ContentEncoding contentEncoding)
  {
    long rangeStart = 0, entityLength = metadata.Length ?? -1, rangeLength = entityLength;
    if(ranges != null && ranges.Length == 1)
    {
      rangeStart  = ranges[0].Start;
      rangeLength = ranges[0].Length;
    }

    Context.Response.SetContentType(metadata.MediaType); // set ContentType even if mediaType is null to avoid the text/html default

    // if the precondition is 304 Not Modified, we should return that instead as per the rules for invoking CheckPreconditions (i.e.
    // we apply the status if we would normally return 200 OK). this applies even for 206 responses because the Range header is
    // supposed to be ignored at the time we're checking preconditions as per RFC 7233 section 3.1. so, ignoring the Range header,
    // the reply /would have been/ 200 OK, so we send the 304 Not Modified status for both full and partial gets
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      // set the Content-Length header if we know the length. we don't do this for 304 Not Modified responses. while we are allowed
      // to send the content length for 304 responses, several widely used clients fail to handle it correctly, so we won't
      if(rangeLength != -1 && contentEncoding == ContentEncoding.Identity)
      {
        Context.Response.Headers[DAVHeaders.ContentLength] = rangeLength.ToStringInvariant();
      }

      if(rangeStart == 0 && rangeLength == entityLength) // if we're sending the whole file, then we want to use a 200 OK response
      {
        Context.Response.SetStatus(ConditionCodes.OK);
      }
      else // otherwise, we're sending only a portion
      {
        Context.Response.SetStatus(ConditionCodes.PartialContent); // so use a 206 Partial Content response
        // let the client know which part of the file was sent
        Context.Response.Headers[DAVHeaders.ContentRange] = new ContentRange(rangeStart, rangeLength, entityLength).ToHeaderString();
      }

      if(!IsHeadRequest) // we want to write an entity body unless it's a HEAD request...
      {
        using(Stream outputStream = Context.OpenResponseBody(contentEncoding, true))
        {
          // if we're not encoding the stream, pass Response.OutputStream to WriteStreamRange so it can use the WriteFile method
          Stream stream = contentEncoding == ContentEncoding.Identity ? Context.Response.OutputStream : outputStream;
          WriteStreamRange(stream, entityBody, rangeStart, rangeLength);
        }
        return true;
      }
    }

    return false;
  }

  /// <summary>Copies a range of bytes from one stream to another. Note that this method interprets the <paramref name="offset"/> parameter
  /// differently depending on whether the stream is seekable.
  /// </summary>
  /// <param name="destStream">The stream into which <paramref name="sourceStream"/> will be written.</param>
  /// <param name="sourceStream">The stream whose contents will be written to <paramref name="destStream"/>.</param>
  /// <param name="offset">If the stream is seekable, this represents the absolute position within the stream from which data will be
  /// copied. If the stream is not seekable, this represents the number of bytes to skip before beginning to copy data. If the stream is
  /// positioned at the beginning, the result is the same either way.
  /// </param>
  /// <param name="length">The number of bytes to copy from the stream, or -1 to copy the stream to the end.</param>
  /// <remarks>If you can easily supply a <see cref="FileStream"/> you should, since this method has especially efficient handling of
  /// <see cref="FileStream"/> objects when writing directly to the response <see cref="HttpResponse.OutputStream"/>.
  /// </remarks>
  void WriteStreamRange(Stream destStream, Stream sourceStream, long offset, long length)
  {
    if(destStream == null || sourceStream == null) throw new ArgumentNullException();
    if(offset < 0 || length < -1) throw new ArgumentOutOfRangeException();

    if(sourceStream.CanSeek)
    {
      // use the more efficient WriteFile method if we're writing directly to the response stream
      FileStream fileStream = sourceStream as FileStream;
      if(fileStream != null && destStream == Context.Response.OutputStream)
      {
        Context.Response.WriteFile(fileStream.SafeFileHandle.DangerousGetHandle(), offset, length);
        return;
      }
      else
      {
        sourceStream.Position = offset;
        offset = 0;
      }
    }

    if(offset != 0) sourceStream.Skip(offset);

    sourceStream.Process(4096, true, (buffer, bytesInBuffer) =>
    {
      int bytesToWrite = length == -1 ? bytesInBuffer : (int)Math.Min(bytesInBuffer, length);
      destStream.Write(buffer, 0, bytesToWrite);
      if(length != -1) length -= bytesToWrite;
      return length != 0;
    });
  }

  readonly ByteRange[] ranges;

  static string GetSizeString(long size)
  {
    const long KB = 1024, MB = KB*1024, GB = MB*1024, TB = GB*1024, PB = TB*1024, EB = PB*1024;
    return size < KB ? size.ToStringInvariant() + " B" :
           size < MB ? (size / (double)KB).ToString("f2", CultureInfo.InvariantCulture) + " KiB" :
           size < GB ? (size / (double)MB).ToString("f2", CultureInfo.InvariantCulture) + " MiB" :
           size < TB ? (size / (double)GB).ToString("f2", CultureInfo.InvariantCulture) + " GiB" :
           size < PB ? (size / (double)TB).ToString("f2", CultureInfo.InvariantCulture) + " TiB" :
           size < EB ? (size / (double)PB).ToString("f2", CultureInfo.InvariantCulture) + " PiB" :
                       (size / (double)EB).ToString("f2", CultureInfo.InvariantCulture) + " EiB";
  }

  static void WriteAsciiString(Stream stream, string str)
  {
    stream.Write(Encoding.ASCII.GetBytes(str));
  }

  static readonly Regex reRanges =
    new Regex(@"^\s*bytes\s*=\s*(?<ranges>(?:\d+\s*-\s*\d*|-\s*\d+)(?:\s*,\s*(?:\d+\s*-\s*\d*|-\s*\d+))*)\s*$", RegexOptions.Compiled);
}
#endregion

} // namespace AdamMil.WebDAV.Server
