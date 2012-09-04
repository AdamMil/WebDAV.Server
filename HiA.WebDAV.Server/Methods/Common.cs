using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

#region Depth
/// <summary>Represents the recursion depth requested by the client.</summary>
public enum Depth
{
  /// <summary>The request did not specify a recursion depth, and there is no default defined by the WebDAV specification.</summary>
  Unspecified,
  /// <summary>The request should apply only to the resource named by the request URI.</summary>
  Self,
  /// <summary>The request should apply to the resource named by the request URI and its immediate children.</summary>
  SelfAndChildren,
  /// <summary>The request should apply to the resource named by the request URI and all of its descendants.</summary>
  SelfAndDescendants
}
#endregion

#region EntityMetadata
/// <summary>Contains information about an entity and/or entity body.</summary>
public sealed class EntityMetadata
{
  /// <summary>Gets or sets the RFC 2616 entity tag of the entity body. This can be set to null if the entity tag cannot be computed.</summary>
  public EntityTag EntityTag { get; set; }

  /// <summary>Gets or sets whether the entity actually exists. The default is true. This might be set to false when, for instance,
  /// servicing a <c>PUT</c> request to create a new resource.
  /// </summary>
  public bool Exists { get; set; }

  /// <summary>Gets or sets the <see cref="DateTime"/> indicating when the entity body was last modified. The value should be set to a
  /// time in UTC, but it can be set to null if the last modification time is unknown.
  /// </summary>
  public DateTime? LastModifiedTime { get; set; }

  /// <summary>Gets or sets the length of the entity body. This can be set to null if the length is unknown.</summary>
  public long? Length
  {
    get { return _length; }
    set
    {
      if(value.HasValue && value.Value < 0) throw new ArgumentOutOfRangeException();
      _length = value;
    }
  }

  /// <summary>Gets or sets the RFC 2616 <c>media-type</c> of the entity body. This can be set to null if the media type is unknown.</summary>
  public string MediaType { get; set; }

  long? _length;
}
#endregion

#region FailedResourceCollection
/// <summary>A collection of <see cref="ResourceStatus"/> objects representing resources that could not be successfully processed or that
/// prevented successful processing of the request resource.
/// </summary>
public sealed class FailedResourceCollection : CollectionBase<ResourceStatus>
{
  internal FailedResourceCollection() { }

  /// <summary>Adds a new <see cref="ResourceStatus"/> to the collection, given the path to the resource (relative to
  /// <see cref="WebDAVContext.ServiceRoot"/>) and the status of the resource.
  /// </summary>
  public void Add(string relativePath, ConditionCode status)
  {
    Add(new ResourceStatus(relativePath, status));
  }
}
#endregion

#region PropertyNameSet
/// <summary>A read-only collection of property names referenced by the client.</summary>
public sealed class PropertyNameSet : AccessLimitedCollectionBase<XmlQualifiedName>
{
  internal PropertyNameSet() { }

  /// <inheritdoc/>
  public override bool IsReadOnly
  {
    get { return true; }
  }

  /// <inheritdoc/>
  public new bool Contains(XmlQualifiedName qname)
  {
    return qname != null && names != null && names.Contains(qname);
  }

  internal void Add(XmlQualifiedName qname)
  {
    if(qname == null || qname.IsEmpty) throw new ArgumentException("The name must not be null or empty.");
    if(names == null) names = new HashSet<XmlQualifiedName>();
    if(!names.Add(qname)) throw Exceptions.BadRequest("Duplicate property name " + qname.ToString());
    Items.Add(qname);
  }

  internal void AddRange(IEnumerable<XmlQualifiedName> names)
  {
    foreach(XmlQualifiedName name in names) Add(name);
  }

  internal static readonly PropertyNameSet Empty = new PropertyNameSet();

  HashSet<XmlQualifiedName> names;
}
#endregion

#region ResourceStatus
/// <summary>Represents a path to a resource and a <see cref="ConditionCode"/> describing the status of the resource, in the context of
/// some operation.
/// </summary>
public sealed class ResourceStatus
{
  /// <summary>Initializes a new <see cref="ResourceStatus"/>, given the path to the resource (relative to
  /// <see cref="WebDAVContext.ServiceRoot"/>) and the status of the resource.
  /// </summary>
  public ResourceStatus(string relativePath, ConditionCode status)
  {
    if(relativePath == null || status == null) throw new ArgumentNullException();
    RelativePath = relativePath;
    Status       = status;
  }

  /// <summary>Gets the path to the resource, relative to <see cref="WebDAVContext.ServiceRoot"/>.</summary>
  public string RelativePath { get; private set; }

  /// <summary>Gets the status of the resource.</summary>
  public ConditionCode Status { get; private set; }
}
#endregion

#region WebDAVRequest
/// <summary>Represents a WebDAV request.</summary>
public abstract class WebDAVRequest
{
  /// <summary>Initializes a new <see cref="WebDAVRequest"/> based on a new WebDAV request.</summary>
  protected WebDAVRequest(WebDAVContext context)
  {
    Context    = context;
    MethodName = context.Request.HttpMethod;

    string value = context.Request.Headers["Depth"];
    if(value != null)
    {
      if("0".OrdinalEquals(value)) Depth = Depth.Self;
      else if("1".OrdinalEquals(value)) Depth = Depth.SelfAndChildren;
      else if("infinity".OrdinalEquals(value)) Depth = Depth.SelfAndDescendants;
      else throw Exceptions.BadRequest("The Depth header must be 0, 1, or infinity.");
    }

    value = context.Request.Headers[HttpHeaders.IfMatch];
    if(value != null) ifMatch = ParseIfMatch(value);

    value = context.Request.Headers[HttpHeaders.IfNoneMatch];
    if(value != null) ifNoneMatch = ParseIfMatch(value);

    value = context.Request.Headers[HttpHeaders.IfModifiedSince];
    if(value != null) ifModifiedSince = ParseHttpDate(value);

    value = context.Request.Headers[HttpHeaders.IfUnmodifiedSince];
    if(value != null) ifUnmodifiedSince = ParseHttpDate(value);

    value = context.Request.Headers[HttpHeaders.If];
    if(value != null) ifClauses = ParseIf(value);
  }

  /// <summary>Gets the <see cref="WebDAVContext"/> in which the request is being executed.</summary>
  public WebDAVContext Context { get; private set; }

  /// <summary>Gets the recursion depth requested by the client.</summary>
  public Depth Depth { get; protected set; }

  /// <summary>Gets the HTTP method name sent by the client.</summary>
  public string MethodName { get; private set; }

  /// <summary>Gets whether the client has submitted any condition headers (e.g. <c>If-Match</c>, <c>If-Modified-Since</c>, etc). If true,
  /// you must call the <see cref="CheckPreconditions"/> method to see if the preconditions are satisfied before executing the method. If
  /// false, you needn't call the <see cref="CheckPreconditions"/> method, as it is guaranteed to return null.
  /// </summary>
  public bool ShouldCheckPreconditions
  {
    get { return ifClauses != null || ifMatch != null || ifNoneMatch != null || ifModifiedSince.HasValue || ifUnmodifiedSince.HasValue; }
  }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> representing the overall result of the request. If the status is null, the
  /// request is assumed to have been successful and an appropriate response will be used. In general, setting a status value will
  /// prevent a default entity body from being generated.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <summary>Determines whether a request should be processed given the precondition (i.e. <c>If-Match</c>, <c>If-None-Match</c>,
  /// <c>If-Modified-Since</c>, and <c>If-Unmodified-Since</c>) headers submitted by the client.
  /// </summary>
  /// <param name="entityTag">The current entity tag of the resource requested by the client. If no entity exists, this should be null.</param>
  /// <param name="lastModifiedTime">The time that the resource requested by the client was last modified. If no entity exists or the last
  /// modification time is unknown, this should be null.
  /// </param>
  /// <param name="entityExists">True if the entity requested by the client exists and false if not.</param>
  /// <returns>Returns null if the request should proceed normally. Otherwise, the status code that should be returned to the client is
  /// returned. This might be an error code (e.g. 412 Precondition Failed) or a redirection code (e.g. 304 Not Modified).
  /// </returns>
  /// <remarks>If the request would normally result in a response other than 2xx or 412 Precondition Failed, then that response must be
  /// given instead. That is to say, this method should only be called if the request would otherwise result in a 2xx response.
  /// In addition, if the request is a <c>GET</c> request and this method returns 304 Not Modified, the 304 response should be ignored if
  /// the <c>GET</c> request would otherwise have been responded to with anything but 200 OK. (For instance, if a particular <c>GET</c>
  /// request would normally be responded to with 206 Partial Content and this method returns 304 Not Modified, the request should be
  /// processed normally and return 206 Partial Content. However if this method returns any other status or if the <c>GET</c> request would
  /// normally be responded to with 200 OK, the status returned from this method should be returned to the client instead.)
  /// <note type="caution">This method does not check the <c>If-Range</c> header. In responding to a <c>GET</c> request, you should check
  /// the <c>If-Range</c> header or use </note>
  /// </remarks>
  public ConditionCode CheckPreconditions(EntityTag entityTag, DateTime? lastModifiedTime, bool entityExists)
  {
    // RFC 2616 leaves the behavior undefined when If-* headers are mixed together, so we will implement it by requiring the request to
    // pass all conditions and trying conditions that cause errors before those that cause redirections

    // as per RFC 2616 sections 14.24 and 14.26, if the entity exists, then the precondition fails if 1) If-None-Match = *, 2) If-Match
    // doesn't match the entity tag, or 3) If-None-Match does match the entity tag and the method is not GET or HEAD. otherwise, if the
    // entity doesn't exist, the precondition fails if If-Match was provided. if the entity exists and If-None-Match matches it and the
    // method is GET or HEAD, 304 Not Modified should be returned instead. that is handled later
    if(entityExists ? ifMatch != null && entityTag != null && !Matches(ifMatch, entityTag) ||
                      Matches(ifNoneMatch, entityTag) && !IsGetOrHead()
                    : ifMatch != null)
    {
      return ConditionCodes.PreconditionFailed;
    }

    DateTime utcTime = default(DateTime);
    if((ifModifiedSince.HasValue || ifUnmodifiedSince.HasValue) && lastModifiedTime.HasValue)
    {
      utcTime = DAVUtility.GetHttpDate(lastModifiedTime.Value); // convert it to a date value suitable for comparing with HTTP dates
      // as per RFC 2616 section 14.28, the precondition fails if the last modified date is after the If-Unmodified-Since date
      if(ifUnmodifiedSince.HasValue && utcTime > ifUnmodifiedSince) return ConditionCodes.PreconditionFailed;
    }

    // RFC 2616 section 14.26 says that if the entity exists and matches the If-None-Match header, and the request is GET or HEAD, then
    // 304 Not Modified should be returned
    if(entityExists && Matches(ifNoneMatch, entityTag) && IsGetOrHead())
    {
      // section 14.26 also says that if we respond with 304 Not Modified, then we should also the matching ETag value
      if(entityTag != null && string.IsNullOrEmpty(Context.Response.Headers[HttpHeaders.ETag]))
      {
        Context.Response.Headers[HttpHeaders.ETag] = entityTag.ToHeaderString();
      }
      return ConditionCodes.NotModified;
    }

    // as per RFC 2616 section 14.25, a 304 Not Modified response should be returned if 1) a GET request would normally be
    // responded to with a 200 OK status (something we can't check here), and 2) the last modified time is not greater than
    // If-Modified-Since
    if(ifModifiedSince.HasValue && lastModifiedTime.HasValue && utcTime <= ifModifiedSince) return ConditionCodes.NotModified;

    return null;
  }

  /// <summary>Returns a <see cref="HashSet{T}"/> containing the lock tokens submitted along with the request. The collection will always
  /// be empty if <see cref="ShouldCheckPreconditions"/> is false.
  /// </summary>
  public HashSet<string> GetSubmittedLockTokens()
  {
    HashSet<string> lockTokens = new HashSet<string>();
    if(ifClauses != null)
    {
      foreach(TaggedIfLists taggedList in ifClauses)
      {
        foreach(IfList list in taggedList.Lists)
        {
          foreach(IfCondition condition in list.Conditions)
          {
            if(condition.LockToken != null) lockTokens.Add(condition.LockToken);
          }
        }
      }
    }
    return lockTokens;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  /// <remarks>The default implementation does nothing.</remarks>
  protected internal virtual void ParseRequest() { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal abstract void WriteResponse();

  #region IfCondition
  sealed class IfCondition
  {
    internal IfCondition(EntityTag entityTag, bool negated)
    {
      if(entityTag == null) throw new ArgumentNullException();
      EntityTag = entityTag;
      Negated   = negated;
    }

    internal IfCondition(string lockToken, bool negated)
    {
      if(string.IsNullOrEmpty(lockToken)) throw new ArgumentException();
      Negated = negated;
    }

    public EntityTag EntityTag { get; private set; }
    public string LockToken { get; private set; }
    public bool Negated { get; private set; }
  }
  #endregion

  #region IfList
  sealed class IfList
  {
    internal IfList(IfCondition[] conditions)
    {
      if(conditions == null || conditions.Length == 0) throw new ArgumentException();
      Conditions = conditions;
    }

    public IfCondition[] Conditions { get; private set; }
  }
  #endregion

  #region TaggedIfLists
  sealed class TaggedIfLists
  {
    internal TaggedIfLists(string resourceTag, IfList[] lists)
    {
      if(lists == null) throw new ArgumentNullException();
      if(resourceTag != null && resourceTag.Length == 0 || lists.Length == 0) throw new ArgumentException();
      ResourceTag = resourceTag;
      Lists       = lists;
    }

    public string ResourceTag { get; private set; }
    public IfList[] Lists { get; private set; }
  }
  #endregion

  bool IsGetOrHead()
  {
    return Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Get) || Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Head);
  }

  TaggedIfLists[] ifClauses;
  EntityTag[] ifMatch, ifNoneMatch;
  DateTime? ifModifiedSince, ifUnmodifiedSince;

  /// <summary>Determines whether the given entity tag matches the tags specified by the match array from the <c>If-Match</c> or
  /// <c>If-None-Match</c> headers.
  /// </summary>
  static bool Matches(EntityTag[] matches, EntityTag entityTag)
  {
    if(matches == MatchAny) return true;
    if(matches != null && entityTag != null)
    {
      foreach(EntityTag match in matches)
      {
        // RFC 2616 section 14.24 requires the strong match and 14.26 requires it for all requests except GET and HEAD. so we'll just do
        // it all the time even though we could technically use a weak match for GET/HEAD requests when checking If-None-Match
        if(entityTag.StronglyEquals(match)) return true;
      }
    }
    return false;
  }

  static EntityTag ParseEntityTag(string value, ref int index, int end)
  {
    int i = index;
    bool isWeak = false;
    char c = value[i++];
    if(c == 'W') // if the entity tag starts with W, that represents a weak tag
    {
      if(end - i < 2 || value[i++] != '/') return null; // W must be followed by a slash
      c = value[i++]; // grab the first one, which should be a quotation mark
      isWeak = true;
    }
    if(c != '"' || i == end) return null; // a quoted string must follow, so expect more characters

    // find the end of the entity tag and add it to the list
    int start = i;
    while(true)
    {
      c = value[i++];
      if(c == '\\') i++;
      else if(c == '"') break;
      else if(i == end) return null;
    }

    index = i;
    try { return new EntityTag(DAVUtility.UnquoteDecode(value, start, i-start-1), isWeak); }
    catch(FormatException) { return null; }
  }

  /// <summary>Parses the value of a WebDAV <c>If</c> header, as defined in RFC 4918 section 10.4.</summary>
  static TaggedIfLists[] ParseIf(string value)
  {
    List<TaggedIfLists> taggedLists = new List<TaggedIfLists>();
    List<IfList> lists = new List<IfList>();
    List<IfCondition> conditions = new List<IfCondition>();
    int index = 0;

    while(true)
    {
      index = SkipWhitespace(value, index);
      if(index == value.Length) break;

      string tag = null;
      if(value[index] == '<')
      {
        tag = ParseTag(value, ref index);
        if(tag == null) return null;
      }

      lists.Clear();
      index = SkipWhitespace(value, index);
      if(index == value.Length || value[index] != '(') return null;
      do
      {
        index = SkipWhitespace(value, index+1);
        if(index == value.Length) return null;
        conditions.Clear();

        while(true)
        {
          bool negated = value.StartsWith("Not ");
          if(negated)
          {
            index = SkipWhitespace(value, index+4);
            if(index == value.Length) return null;
          }

          if(value[index] == '<')
          {
            string lockToken = ParseTag(value, ref index);
            if(lockToken == null) return null;
            conditions.Add(new IfCondition(lockToken, negated));
          }
          else if(value[index] == '[')
          {
            if(++index == value.Length) return null;
            EntityTag entityTag = ParseEntityTag(value, ref index, value.Length);
            if(entityTag == null) return null;
            conditions.Add(new IfCondition(entityTag, negated));
            if(index == value.Length || value[index++] != ']') return null;
          }
          else if(value[index] == ')')
          {
            index++;
            break;
          }
          else
          {
            return null;
          }

          index = SkipWhitespace(value, index);
        }

        if(conditions.Count == 0) return null;

        lists.Add(new IfList(conditions.ToArray()));
        index = SkipWhitespace(value, index);
      } while(index < value.Length && value[index] == '(');

      taggedLists.Add(new TaggedIfLists(tag, lists.ToArray()));
    }

    return taggedLists.Count == 0 ? null : taggedLists.ToArray();
  }

  /// <summary>Parses the value of an HTTP <c>If-Match</c> or <c>If-None-Match</c> header, which contains a comma-separated list of
  /// entity tags.
  /// </summary>
  static EntityTag[] ParseIfMatch(string value)
  {
    int start, length;
    StringUtility.Trim(value, out start, out length);
    // if there was no header value, return null. otherwise, if the value is *, return an empty array
    if(length == 0) return null;
    if(string.Compare(value, start, "*", 0, length, StringComparison.Ordinal) == 0) return MatchAny;

    // unfortunately we can't simply split on commas because the commas could occur within an entity tag, so we actually have to parse them
    // to find where they start and end
    List<EntityTag> tags = new List<EntityTag>();
    for(int i = start, end = start + length; i < end; ) // for each entity tag
    {
      EntityTag tag = ParseEntityTag(value, ref i, end);
      if(tag == null) return null;

      // now skip over a comma if there is one
      while(i < end && char.IsWhiteSpace(value[i])) i++;
      if(i < end)
      {
        if(value[i++] != ',') return null;
        while(i < end && char.IsWhiteSpace(value[i])) i++;
      }
    }
    return tags.ToArray();
  }

  static DateTime? ParseHttpDate(string value)
  {
    DateTime date;
    return DAVUtility.TryParseHttpDate(value, out date) ? (DateTime?)date : null;
  }

  static string ParseTag(string value, ref int index)
  {
    int end = ++index;
    while(end < value.Length && value[end] != '>') end++;
    if(end == value.Length || end == index) return null;
    string tag = value.Substring(index, end-index);
    index = end + 1;
    return tag;
  }

  static int SkipWhitespace(string value, int index)
  {
    while(index < value.Length && char.IsWhiteSpace(value[index])) index++;
    return index;
  }

  static readonly Regex matchRe = new Regex(@"^\s*(?:(?:W/)?""(?:\\.|[^""])*""(?:\s*,\s*(?:W/)?""(?:\\.|[^""\\])*"")*\s*)?$",
                                            RegexOptions.Compiled | RegexOptions.Singleline);
  static readonly EntityTag[] MatchAny = new EntityTag[0];
}
#endregion

#region SimpleRequest
/// <summary>Represents a simple WebDAV request that doesn't need any special parameters.</summary>
public abstract class SimpleRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="SimpleRequest"/> based on a new WebDAV request.</summary>
  protected SimpleRequest(WebDAVContext context) : base(context) { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>The default implementation sets the response status based on <see cref="WebDAVRequest.Status"/>, using
  /// <see cref="ConditionCodes.NoContent"/> if the status is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
  }
}
#endregion

} // namespace HiA.WebDAV.Server
