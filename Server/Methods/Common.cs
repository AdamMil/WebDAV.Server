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
using System.Collections.Generic;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
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
  /// <summary>Initializes a new <see cref="EntityMetadata"/> representing an existing resource.</summary>
  public EntityMetadata()
  {
    Exists = true;
  }

  /// <summary>Gets or sets whether the entity is considered to be compressible. If true, the entity may be compressed when sent to the
  /// client, if the client supports compression. If false, the entity will not be compressed when sent to the client. If null, whether
  /// or not the entity will be compressed depends on its <see cref="MediaType"/>. The default is null.
  /// </summary>
  public bool? Compressible { get; set; }

  /// <summary>Gets or sets the RFC 7232 entity tag of the entity body. In general, you should use
  /// <see cref="DAVUtility.ComputeEntityTag(System.IO.Stream)"/> to compute this, because all built-in entity tag computations are done
  /// using that method. If you want to use a different method, you will need to take care to calculate the entity tag yourself everywhere
  /// that the system would normally do it for you. This can be set to null if the entity tag cannot be computed.
  /// </summary>
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

  /// <summary>Gets or sets the RFC 7231 <c>media-type</c> of the entity body. This can be set to null if the media type is unknown.</summary>
  public string MediaType { get; set; }

  /// <summary>Returns a copy of this <see cref="EntityMetadata"/> object.</summary>
  public EntityMetadata Clone()
  {
    return new EntityMetadata()
    {
      Compressible = Compressible, EntityTag = EntityTag, Exists = Exists, LastModifiedTime = LastModifiedTime, Length = _length,
      MediaType = MediaType
    };
  }

  /// <summary>Determines whether the entity should be compressed.</summary>
  public bool ShouldCompress()
  {
    return Compressible.HasValue ? Compressible.Value : MediaTypes.ShouldCompress(MediaType);
  }

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

  /// <summary>Adds a new <see cref="ResourceStatus"/> to the collection, given the path to the resource and the status of the resource.</summary>
  public void Add(string serviceRoot, string relativePath, ConditionCode status)
  {
    Add(new ResourceStatus(serviceRoot, relativePath, status));
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

  HashSet<XmlQualifiedName> names;
}
#endregion

#region ResourceStatus
/// <summary>Represents a path to a resource and a <see cref="ConditionCode"/> describing the status of the resource, in the context of
/// some operation.
/// </summary>
public sealed class ResourceStatus
{
  /// <summary>Initializes a new <see cref="ResourceStatus"/>, given the path to the resource and the status of the resource.</summary>
  /// <param name="serviceRoot">The root of the service containing the resource, as either an absolute path or an absolute URI, including
  /// a trailing slash.
  /// </param>
  /// <param name="relativePath">The path to the resource, relative to <paramref name="serviceRoot"/>.</param>
  /// <param name="status">The status of the resource.</param>
  public ResourceStatus(string serviceRoot, string relativePath, ConditionCode status)
  {
    if(serviceRoot == null || relativePath == null || status == null) throw new ArgumentNullException();
    RelativePath = relativePath;
    ServiceRoot  = serviceRoot;
    Status       = status;
  }

  /// <summary>Gets the path to the resource, relative to <see cref="ServiceRoot"/>.</summary>
  public string RelativePath { get; private set; }

  /// <summary>Gets the root of the service containing the resource, as either an absolute path or an absolute URI, including a trailing
  /// slash.
  /// </summary>
  public string ServiceRoot { get; private set; }

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
    if(context == null) throw new ArgumentNullException();

    Context    = context;
    MethodName = context.Request.HttpMethod;

    string value = context.Request.Headers[DAVHeaders.Depth];
    if(value != null)
    {
      if("0".OrdinalEquals(value)) Depth = Depth.Self;
      else if("1".OrdinalEquals(value)) Depth = Depth.SelfAndChildren;
      else if("infinity".OrdinalEquals(value)) Depth = Depth.SelfAndDescendants;
      else throw Exceptions.BadRequest("The Depth header must be 0, 1, or infinity.");
    }

    value = context.Request.Headers[DAVHeaders.IfMatch];
    if(value != null) ifMatch = ParseIfMatch(value, DAVHeaders.IfMatch);

    value = context.Request.Headers[DAVHeaders.IfNoneMatch];
    if(value != null) ifNoneMatch = ParseIfMatch(value, DAVHeaders.IfNoneMatch);

    value = context.Request.Headers[DAVHeaders.IfModifiedSince];
    if(value != null) ifModifiedSince = ParseHttpDateHeader(value, DAVHeaders.IfModifiedSince);

    value = context.Request.Headers[DAVHeaders.IfUnmodifiedSince];
    if(value != null) ifUnmodifiedSince = ParseHttpDateHeader(value, DAVHeaders.IfUnmodifiedSince);

    value = context.Request.Headers[DAVHeaders.If];
    if(value != null) ifClauses = ParseIfHeader(value, DAVHeaders.If);
  }

  /// <summary>Gets the <see cref="WebDAVContext"/> in which the request is being executed.</summary>
  public WebDAVContext Context { get; private set; }

  /// <summary>Gets the recursion depth requested by the client.</summary>
  public Depth Depth { get; protected set; }

  /// <summary>Gets the HTTP method name sent by the client.</summary>
  public string MethodName { get; private set; }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> indicating the overall result of the request. If the status is null, the
  /// request is assumed to have been successful and an appropriate response will be used. In general, setting a status value will
  /// prevent a default entity body from being generated.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/*[@name != 'canonicalPath']" />
  public ConditionCode CheckPreconditions(EntityMetadata requestMetadata)
  {
    return CheckPreconditions(requestMetadata, null);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  public ConditionCode CheckPreconditions(EntityMetadata requestMetadata, string canonicalPath)
  {
    // RFC 7232 section 6 defines the evaluation order when multiple conditions are mixed together, although it doesn't specify how the
    // standard HTTP conditions should be combined with conditions from extensions such as WebDAV. the general idea, though, is to try
    // conditions that cause 412 Precondition Failed responses before those that cause 304 Not Modified responses, which are before those
    // that cause 206 Partial Content (handled in GetOrHeadRequest). so although we may not use the exact same steps as RFC 7232, the
    // result should be the same
    if(ifMatch != null || ifNoneMatch != null)
    {
      // load the request metadata with the entity tag if we need it
      if(requestMetadata == null || requestMetadata.EntityTag == null)
      {
        if(Context.RequestResource != null) requestMetadata = Context.RequestResource.GetEntityMetadata(true);
        if(requestMetadata == null) requestMetadata = new EntityMetadata() { Exists = false };
      }

      // as per RFC 7232 sections 3.1, 3.2, and 6, if the entity exists, then the precondition fails if 1) If-None-Match = *, 2) If-Match
      // doesn't strongly match the entity tag, or 3) If-None-Match (weakly) matches the entity tag and the method is not GET or HEAD.
      // otherwise, if the entity doesn't exist, the precondition fails if If-Match was provided. if the entity exists and If-None-Match
      // (weakly) matches it and the method is GET or HEAD, 304 Not Modified should be returned instead. that is handled later
      if(requestMetadata.Exists ?
            ifMatch != null && requestMetadata.EntityTag != null && !Matches(ifMatch, requestMetadata.EntityTag, true) ||
            Matches(ifNoneMatch, requestMetadata.EntityTag, false) && !IsGetOrHead()
          : ifMatch != null)
      {
        return ConditionCodes.PreconditionFailed;
      }
    }

    DateTime utcTime = default(DateTime);
    if(ifModifiedSince.HasValue || ifUnmodifiedSince.HasValue)
    {
      if(requestMetadata == null)
      {
        if(Context.RequestResource != null) requestMetadata = Context.RequestResource.GetEntityMetadata(false);
        if(requestMetadata == null) requestMetadata = new EntityMetadata() { Exists = false };
      }

      if(requestMetadata.LastModifiedTime.HasValue)
      {
        // convert the modification time to a value suitable for comparing with HTTP dates
        utcTime = DAVUtility.GetHttpDate(requestMetadata.LastModifiedTime.Value);
        // as per RFC 7232 section 3.4, the precondition fails if the last modified date is after the If-Unmodified-Since date
        if(ifUnmodifiedSince.HasValue && utcTime > ifUnmodifiedSince) return ConditionCodes.PreconditionFailed;
      }
    }

    if(ifClauses != null && !CheckIfHeader(ref requestMetadata, canonicalPath)) return ConditionCodes.PreconditionFailed;

    // now check that the required lock tokens have been submitted in the If header
    ConditionCode lockStatus = CheckSubmittedLockTokens(canonicalPath);
    if(lockStatus != null) return lockStatus;

    if(ifNoneMatch != null)
    {
      // RFC 7232 section 3.2 says that if the entity exists and matches the If-None-Match header, and the request is GET or HEAD, then
      // 304 Not Modified should be returned. it also says that we must use a weak comparison
      if(requestMetadata.Exists && Matches(ifNoneMatch, requestMetadata.EntityTag, false) && IsGetOrHead())
      {
        // section 3.2 also says that if we respond with 304 Not Modified, then we should also the matching ETag value
        if(requestMetadata.EntityTag != null && string.IsNullOrEmpty(Context.Response.Headers[DAVHeaders.ETag]))
        {
          Context.Response.Headers[DAVHeaders.ETag] = requestMetadata.EntityTag.ToHeaderString();
        }
        return ConditionCodes.NotModified;
      }
    }
    // as per RFC 7232 section 3.2, a 304 Not Modified response should be returned if 1) a GET request would normally be
    // responded to with a 200 OK status (something we can't check here), and 2) the last modified time is not greater than
    // If-Modified-Since. according to section 3.3, we only check If-Modified-Since if If-None-Match was not submitted
    else if(ifModifiedSince.HasValue && requestMetadata.LastModifiedTime.HasValue && utcTime <= ifModifiedSince)
    {
      // according to RFC 7232 section 6, If-Modified-Since only applies to GET/HEAD requests. but it seems useful to apply it to other
      // types of requests as well, just as If-None-Match is applied
      return IsGetOrHead() ? ConditionCodes.NotModified : ConditionCodes.PreconditionFailed;
    }

    return null;
  }

  /// <summary>Returns a <see cref="HashSet{T}"/> containing the lock tokens submitted along with the request.</summary>
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

      // filter the lock tokens, for instance to remove those that are not owned by the current user
      List<string> deadTokens = null;
      foreach(string token in lockTokens)
      {
        if(!FilterSubmittedLockToken(token))
        {
          if(deadTokens == null) deadTokens = new List<string>();
          deadTokens.AddRange(token);
        }
      }
      if(deadTokens != null) lockTokens.ExceptWith(deadTokens);
    }
    return lockTokens;
  }

  /// <summary>Gets whether the client has submitted any precondition headers that may require a match against an <see cref="EntityTag"/>.</summary>
  /// <remarks>The main usage of this method is to avoid computing the <see cref="EntityMetadata.EntityTag"/> in cases when it won't be
  /// needed. If this property is false, then the <see cref="EntityMetadata"/> object passed to
  /// <see cref="CheckPreconditions(EntityMetadata)"/> will not require an <see cref="EntityTag"/>. Otherwise, it probably will.
  /// </remarks>
  public bool PreconditionsMayNeedEntityTag()
  {
    if(ifMatch != null || ifNoneMatch != null) return true;
    if(ifClauses != null)
    {
      foreach(TaggedIfLists lists in ifClauses)
      {
        if(lists.ChecksEntityTag()) return true;
      }
    }
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>The default implementation always returns null.
  /// <note type="inherit">Derived classes should typically implement this method by calling
  /// <see cref="CheckSubmittedLockTokens(LockType,string,bool,bool)"/> or one of its overrides.
  /// </note>
  /// </remarks>
  protected virtual ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return null;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensCore/node()" />
  /// <param name="canonicalPath">The canonical, relative path to the resource whose locks will be examined. The resource must be within
  /// the request service (i.e. controlled by <see cref="WebDAVContext.Service"/>). If null, the request resource will be used.
  /// </param>
  protected ConditionCode CheckSubmittedLockTokens(LockType lockType, string canonicalPath, bool checkParent, bool checkDescendants)
  {
    return CheckSubmittedLockTokens(lockType, canonicalPath, checkParent, checkDescendants, null, null);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensCore/node()" />
  /// <param name="canonicalPath">The canonical, relative path to the resource whose locks will be examined. If null, the request resource
  /// will be used.
  /// </param>
  /// <param name="serviceRoot">The service root for the service containing the resource named by <paramref name="canonicalPath"/>. If the
  /// <see cref="Uri.Scheme"/> or <see cref="Uri.Authority"/> of the resource URI is different from the authority of the request resource,
  /// this must be an absolute URI (e.g. <c>http://othersite/otherRoot/</c>). If null, the <see cref="WebDAVContext.ServiceRoot"/> for the
  /// request service will be used.
  /// </param>
  /// <param name="lockManager">The <see cref="ILockManager"/> for the service containing the resource named by
  /// <paramref name="canonicalPath"/>. If null, the lock manager for the request service will be used.
  /// </param>
  protected ConditionCode CheckSubmittedLockTokens(LockType lockType, string canonicalPath, bool checkParent, bool checkDescendants,
                                                   string serviceRoot, ILockManager lockManager)
  {
    if(lockType == null) throw new ArgumentNullException();

    if(lockManager == null) lockManager = Context.LockManager;
    if(lockManager != null)
    {
      if(canonicalPath == null) canonicalPath = Context.GetCanonicalPath();
      serviceRoot = serviceRoot == null ? Context.ServiceRoot : DAVUtility.WithTrailingSlash(serviceRoot);

      HashSet<string> submittedTokens = GetSubmittedLockTokens();
      Predicate<ActiveLock> filter = L => lockType.ConflictsWith(L.Type);
      bool isBuiltInType = lockType.GetType() == typeof(LockType); // can we make assumptions about the LockType implementation?

      if(checkParent) // if we have to check the parent collection for a lock...
      {
        string parentPath = DAVUtility.GetParentPath(canonicalPath);
        if(parentPath != null) // if the resource has a parent...
        {
          IList<ActiveLock> locks = lockManager.GetLocks(parentPath, LockSelection.SelfAndRecursiveAncestors, filter);
          if(locks.Count != 0) // and the parent is locked (directly or indirectly)...
          {
            ActiveLock lockObject = GetSubmittedLock(locks, submittedTokens); // see if a relevant token was submitted
            if(lockObject == null) return new LockTokenSubmittedConditionCode(serviceRoot, parentPath); // error: none submitted
            if(lockObject.Recursive)
            {
              // if it's recursive and we know the implementation, we can avoid checking anything else because the built-in LockType class
              // will only match other locks of exactly the same type, so submitting the parent lock covers all possible descendant locks.
              // otherwise, we still need to look at descendant locks, but we can skip the ones that aren't covered by (i.e. don't conflict
              // with) the recursive parent lock
              if(isBuiltInType) return null;
              else filter = Combine(filter, L => !lockObject.Type.ConflictsWith(L.Type));
            }
          }
        }
      }

      // now check locks directly on the resource, and indirect locks too if we didn't already check them in the checkParent path above
      IList<ActiveLock> directLocks =
        lockManager.GetLocks(canonicalPath, checkParent ? LockSelection.Self : LockSelection.SelfAndRecursiveAncestors, filter);
      if(directLocks.Count != 0)
      {
        ActiveLock lockObject = GetSubmittedLock(directLocks, submittedTokens); // see if a relevant token was submitted
        if(lockObject == null) return new LockTokenSubmittedConditionCode(serviceRoot, canonicalPath); // error: none submitted
        if(lockObject.Recursive && checkDescendants)
        {
          if(isBuiltInType) return null; // see above for a description of this code
          else filter = Combine(filter, L => !lockObject.Type.ConflictsWith(L.Type));
        }
      }

      // now look at locks that are descendants of the resource
      if(checkDescendants)
      {
        List<ActiveLock> descendantLocks = GetList(lockManager.GetLocks(canonicalPath, LockSelection.Descendants, filter));
        if(descendantLocks.Count != 0)
        {
          descendantLocks.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
          for(int i=0; i<descendantLocks.Count; )
          {
            ConditionCode status = CheckDescendantLocks(descendantLocks, ref i, submittedTokens, serviceRoot,
                                                        isBuiltInType ? null : filter);
            if(status != null) return status;
          }
        }
      }
    }

    return null;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/FilterSubmittedLockToken/node()" />
  /// <remarks>The default implementation filters out lock tokens that are not owned by the current user. (If the current user is
  /// anonymous, locks created by other anonymous users will not be filtered out, since there is no way to distinguish the two.)
  /// </remarks>
  protected virtual bool FilterSubmittedLockToken(string lockToken)
  {
    ActiveLock lockObject = Context.LockManager.GetLock(lockToken, null);
    return lockObject != null && Context.CurrentUserId.OrdinalEquals(lockObject.OwnerId);
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
      LockToken = lockToken;
      Negated   = negated;
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
    internal TaggedIfLists(string resourceTag, Uri resourceTagUri, IfList[] lists)
    {
      if(lists == null) throw new ArgumentNullException();
      if(StringUtility.IsEmpty(resourceTag) || lists.Length == 0) throw new ArgumentException();
      ResourceTag    = resourceTag;
      ResourceTagUri = resourceTagUri;
      Lists          = lists;
    }

    public string ResourceTag { get; private set; }
    public Uri ResourceTagUri { get; private set; }
    public IfList[] Lists { get; private set; }

    public bool ChecksEntityTag()
    {
      foreach(IfList ifList in Lists)
      {
        foreach(IfCondition condition in ifList.Conditions)
        {
          if(condition.EntityTag != null) return true;
        }
      }
      return false;
    }

    /// <summary>Determines whether the <see cref="ResourceTag"/> exactly matches a relative path in the context of a request. If this
    /// method returns false, the tag might still match the given path through an alias.
    /// </summary>
    public bool ExactTagMatch(WebDAVContext context, string relativePath)
    {
      if(context == null || relativePath == null) throw new ArgumentNullException();
      if(ResourceTag != null)
      {
        if(ResourceTagUri.IsAbsoluteUri) // if the resource tag was an absolute URI, construct an absolute URI from the request and compare
        {
          string absolutePath = DAVUtility.RemoveTrailingSlash(context.ServiceRoot + relativePath);
          return string.Equals(DAVUtility.RemoveTrailingSlash(ResourceTagUri.AbsolutePath), absolutePath, StringComparison.Ordinal) &&
                 ResourceTagUri.Authority.OrdinalEquals(context.Request.Url.Authority);
        }
        // otherwise, if the resource tag was an absolute path, see if it matches the service root up to the trailing slash
        else if(string.Compare(ResourceTag, 0, context.ServiceRoot, 0, context.ServiceRoot.Length-1, StringComparison.Ordinal) == 0)
        {
          if(relativePath.Length != 0) // the path doesn't merely reference the service root...
          {
            // match the slash that separates the two and then match the relative path
            return ResourceTag.Length > context.ServiceRoot.Length && ResourceTag[context.ServiceRoot.Length-1] == '/' &&
                   string.Compare(ResourceTag, context.ServiceRoot.Length, relativePath, 0, int.MaxValue, StringComparison.Ordinal) == 0;
          }
          else // if the relative path references the service root...
          {
            return ResourceTag.Length < context.ServiceRoot.Length || // we already matched up to the trailing slash, so check the slash
                   ResourceTag.Length == context.ServiceRoot.Length && ResourceTag[context.ServiceRoot.Length-1] == '/';
          }
        }
      }

      return false;
    }
  }
  #endregion

  /// <summary>Checks the lock at the given index along with some or all of its descendants, and advances the index to the next unchecked
  /// lock. This method assumes that the locks have been sorted by path.
  /// </summary>
  ConditionCode CheckDescendantLocks(List<ActiveLock> locks, ref int index, HashSet<string> submittedTokens, string serviceRoot,
                                     Predicate<ActiveLock> filter)
  {
    // find the first lock that passes the filter
    int start = index;
    if(filter != null)
    {
      while(start < locks.Count && !filter(locks[start])) start++;
      if(start == locks.Count) // if none pass, then we're done
      {
        index = locks.Count;
        return null;
      }
    }

    // find all locks with the same (base) path
    int end = start+1, baseCount = 1;
    ActiveLock baseLock = locks[start];
    string basePath = baseLock.Path;
    while(end < locks.Count && basePath.OrdinalEquals(locks[end].Path))
    {
      if(filter == null || filter(locks[end])) baseCount++;
      end++;
    }
    index = end;

    if(baseCount == 1) // if there's only one of them, check it directly
    {
      if(!submittedTokens.Contains(baseLock.Token)) baseLock = null;
    }
    else // otherwise, there are multiple locks with the same path, and we only require one of them to match
    {
      ActiveLock[] baseLocks = new ActiveLock[baseCount];
      baseLocks[0] = baseLock;
      for(int i=start+1,j=1; i<end; i++)
      {
        if(filter == null || filter(locks[i])) baseLocks[j++] = locks[i];
      }
      baseLock = GetSubmittedLock(baseLocks, submittedTokens);
    }

    if(baseLock == null) return new LockTokenSubmittedConditionCode(serviceRoot, basePath); // error: base lock not submitted

    basePath = DAVUtility.WithTrailingSlash(basePath);
    if(baseLock.Recursive) // if the submitted lock was recursive...
    {
      if(filter == null) // if there was no filter, we can simply skip over all descendants
      {
        while(end < locks.Count && locks[end].Path.StartsWith(basePath, StringComparison.Ordinal)) end++;
        index = end;
        return null;
      }
      else // otherwise, we may not be able to skip all descendants, but we can make the filter more restrictive
      {
        filter = Combine(filter, L => !baseLock.Type.ConflictsWith(L.Type));
      }
    }

    // in any case, now look at the descendants
    while(index < locks.Count && locks[index].Path.StartsWith(basePath, StringComparison.Ordinal))
    {
      ConditionCode status = CheckDescendantLocks(locks, ref index, submittedTokens, serviceRoot, filter);
      if(status != null) return status;
    }

    return null;
  }

  /// <summary>Checks the preconditions for the WebDAV <c>If</c> header, assuming that the header was specified.</summary>
  bool CheckIfHeader(ref EntityMetadata requestMetadata, string canonicalPath)
  {
    // RFC 4918 section 10.4.3 specifies the processing for the If header
    if(canonicalPath == null) canonicalPath = Context.GetCanonicalPath();
    bool anyListSucceeded = false;
    foreach(TaggedIfLists clause in ifClauses)
    {
      EntityMetadata metadata = null;
      ILockManager lockManager = null;
      string canonicalClausePath = null;

      // if the clause definitely matches the request URI...
      if(clause.ResourceTag == null || clause.ExactTagMatch(Context, Context.RequestPath) ||
         !canonicalPath.OrdinalEquals(Context.RequestPath) && clause.ExactTagMatch(Context, canonicalPath))
      {
        bool checksEntityTag = clause.ChecksEntityTag();
        if((requestMetadata == null || checksEntityTag && requestMetadata.EntityTag == null) && Context.RequestResource != null)
        {
          requestMetadata = Context.RequestResource.GetEntityMetadata(checksEntityTag);
        }

        canonicalClausePath = canonicalPath;
        metadata            = requestMetadata;
        lockManager         = Context.LockManager;
      }
      else // otherwise, the clause doesn't obviously match the request URI, so do a full resolution step
      {
        UriResolution info = WebDAVModule.ResolveUri(Context, clause.ResourceTagUri, false);
        if(info.Resource != null)
        {
          canonicalClausePath = info.Resource.CanonicalPath;
          metadata            = info.Resource.GetEntityMetadata(clause.ChecksEntityTag());
          lockManager         = info.LockManager;
        }
      }

      foreach(IfList list in clause.Lists)
      {
        bool allConditionsSucceeded = true;
        foreach(IfCondition condition in list.Conditions)
        {
          bool match = false;
          if(condition.EntityTag != null)
          {
            match = metadata != null && condition.EntityTag.Equals(metadata.EntityTag);
          }
          else if(condition.LockToken != null)
          {
            match = lockManager != null && canonicalClausePath != null && lockManager.GetLock(condition.LockToken, canonicalClausePath) != null;
          }

          if(!match ^ condition.Negated)
          {
            allConditionsSucceeded = false;
            break;
          }
        }

        if(allConditionsSucceeded)
        {
          anyListSucceeded = true;
          break;
        }
      }

      if(anyListSucceeded) break;
    }

    return anyListSucceeded;
  }

  bool IsGetOrHead()
  {
    return Context.Request.HttpMethod.OrdinalEquals(DAVMethods.Get) || Context.Request.HttpMethod.OrdinalEquals(DAVMethods.Head);
  }

  TaggedIfLists[] ifClauses;
  EntityTag[] ifMatch, ifNoneMatch;
  DateTime? ifModifiedSince, ifUnmodifiedSince;

  static WebDAVException BadHeader(string headerName, int index, string error)
  {
    throw Exceptions.BadRequest("Invalid " + headerName + " header. Error near index " + index.ToStringInvariant() + ": " + error);
  }

  /// <summary>Combines two <see cref="Predicate{T}"/> of <see cref="ActiveLock"/> into a single predicate representing their conjunction.</summary>
  static Predicate<ActiveLock> Combine(Predicate<ActiveLock> a, Predicate<ActiveLock> b)
  {
    return L => a(L) && b(L);
  }

  static List<ActiveLock> GetList(IList<ActiveLock> list)
  {
    return list as List<ActiveLock> ?? new List<ActiveLock>(list);
  }

  static ActiveLock GetSubmittedLock(IEnumerable<ActiveLock> locks, HashSet<string> submittedTokens)
  {
    ActiveLock matchingLock = null;
    foreach(ActiveLock lockObject in locks)
    {
      if(submittedTokens.Contains(lockObject.Token))
      {
        matchingLock = lockObject;
        if(matchingLock.Recursive) break; // we prefer recursive locks over non-recursive locks, so keep searching if it's not recursive
      }
    }
    return matchingLock;
  }

  /// <summary>Determines whether the given entity tag matches the tags specified by the match array from the <c>If-Match</c> or
  /// <c>If-None-Match</c> headers.
  /// </summary>
  static bool Matches(EntityTag[] matches, EntityTag entityTag, bool useStrongComparison)
  {
    if(matches == MatchAny) return true;
    if(matches != null && entityTag != null)
    {
      foreach(EntityTag match in matches)
      {
        if(useStrongComparison ? entityTag.StronglyEquals(match) : entityTag.WeaklyEquals(match)) return true;
      }
    }
    return false;
  }

  /// <summary>Parses the value of a WebDAV <c>If</c> header, as defined in RFC 4918 section 10.4.</summary>
  static TaggedIfLists[] ParseIfHeader(string value, string headerName)
  {
    List<TaggedIfLists> taggedLists = new List<TaggedIfLists>();
    List<IfList> lists = new List<IfList>();
    List<IfCondition> conditions = new List<IfCondition>();
    int index = 0;

    while(true)
    {
      index = SkipWhitespace(value, index);
      if(index == value.Length) break;

      Uri tagUri = null;
      string tag = null;
      if(value[index] == '<')
      {
        tag = ParseResourceTag(value, ref index);
        if(tag == null) throw BadHeader(headerName, index, "Invalid resource tag.");
        if(!DAVUtility.TryParseSimpleRef(tag, out tagUri)) throw BadHeader(headerName, index, "Expected absolute URI or absolute path.");
      }

      lists.Clear();
      index = SkipWhitespace(value, index);
      if(index == value.Length || value[index] != '(') throw BadHeader(headerName, index, "Expected ')'.");
      do
      {
        index = SkipWhitespace(value, index+1);
        if(index == value.Length) throw BadHeader(headerName, index, "Expected conditions.");
        conditions.Clear();

        while(true)
        {
          bool negated = string.Compare(value, index, "Not ", 0, 4, StringComparison.Ordinal) == 0;
          if(negated)
          {
            index = SkipWhitespace(value, index+4);
            if(index == value.Length) throw BadHeader(headerName, index, "Expected condition.");
          }

          if(value[index] == '<')
          {
            string lockToken = ParseResourceTag(value, ref index);
            if(lockToken == null) throw BadHeader(headerName, index, "Invalid lock token.");
            conditions.Add(new IfCondition(lockToken, negated));
          }
          else if(value[index] == '[')
          {
            if(++index == value.Length) throw BadHeader(headerName, index, "Expected entity tag.");
            EntityTag entityTag = EntityTag.TryParse(value, ref index, value.Length);
            if(entityTag == null) throw BadHeader(headerName, index, "Invalid entity tag.");
            conditions.Add(new IfCondition(entityTag, negated));
            if(index == value.Length || value[index++] != ']') throw BadHeader(headerName, index, "Expected ']'.");
          }
          else if(value[index] == ')')
          {
            index++;
            break;
          }
          else
          {
            throw BadHeader(headerName, index, "Unexpected character '" + value[index].ToString() + "'.");
          }

          index = SkipWhitespace(value, index);
        }

        if(conditions.Count == 0) throw BadHeader(headerName, index, "Expected conditions.");

        lists.Add(new IfList(conditions.ToArray()));
        index = SkipWhitespace(value, index);
      } while(index < value.Length && value[index] == '(');

      taggedLists.Add(new TaggedIfLists(tag, tagUri, lists.ToArray()));
    }

    return taggedLists.Count == 0 ? null : taggedLists.ToArray();
  }

  /// <summary>Parses the value of an HTTP <c>If-Match</c> or <c>If-None-Match</c> header, which contains a comma-separated list of
  /// entity tags.
  /// </summary>
  static EntityTag[] ParseIfMatch(string value, string headerName)
  {
    int start, length;
    value.Trim(out start, out length);
    // if there was no header value, return null. otherwise, if the value is *, return an empty array
    if(length == 0) return null;
    if(string.Compare(value, start, "*", 0, length, StringComparison.Ordinal) == 0) return MatchAny;

    // unfortunately we can't simply split on commas because the commas could occur within an entity tag, so we actually have to parse them
    // to find where they start and end
    List<EntityTag> tags = new List<EntityTag>();
    for(int i = start, end = start + length; i < end; ) // for each entity tag
    {
      EntityTag tag = EntityTag.TryParse(value, ref i, end);
      if(tag == null) throw BadHeader(headerName, i, "Invalid entity tag.");
      tags.Add(tag);

      // now skip over a comma if there is one
      while(i < end && char.IsWhiteSpace(value[i])) i++;
      if(i < end)
      {
        if(value[i++] != ',') throw BadHeader(headerName, i, "Expected ','.");
        while(i < end && char.IsWhiteSpace(value[i])) i++;
      }
    }
    return tags.ToArray();
  }

  static DateTime ParseHttpDateHeader(string value, string headerName)
  {
    DateTime date;
    if(!DAVUtility.TryParseHttpDate(value, out date))
    {
      throw Exceptions.BadRequest("Invalid " + headerName + " header. Expected an HTTP datetime value.");
    }
    return date;
  }

  static string ParseResourceTag(string value, ref int index)
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

} // namespace AdamMil.WebDAV.Server
