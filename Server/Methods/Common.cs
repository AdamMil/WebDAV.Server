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
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

  /// <summary>Gets or sets the RFC 2616 entity tag of the entity body. In general, you should use
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

  /// <summary>Gets or sets the RFC 2616 <c>media-type</c> of the entity body. This can be set to null if the media type is unknown.</summary>
  public string MediaType { get; set; }

  /// <summary>Returns a copy of this <see cref="EntityMetadata"/> object.</summary>
  public EntityMetadata Clone()
  {
    return new EntityMetadata()
    {
      EntityTag = EntityTag, Exists = Exists, LastModifiedTime = LastModifiedTime, Length = _length, MediaType = MediaType
    };
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

  /// <summary>Adds a new <see cref="ResourceStatus"/> to the collection, given the absolute path to the resource and the status of the
  /// resource.
  /// </summary>
  public void Add(string absolutePath, ConditionCode status)
  {
    Add(new ResourceStatus(absolutePath, status));
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
  /// <summary>Initializes a new <see cref="ResourceStatus"/>, given the absolute path to the resource and the status of the resource.</summary>
  public ResourceStatus(string absolutePath, ConditionCode status)
  {
    if(absolutePath == null || status == null) throw new ArgumentNullException();
    AbsolutePath = absolutePath;
    Status       = status;
  }

  /// <summary>Gets the absolute path to the resource.</summary>
  public string AbsolutePath { get; private set; }

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

  /// <summary>Gets whether the client has submitted any precondition headers (e.g. <c>If</c>, <c>If-Match</c>, <c>If-Modified-Since</c>,
  /// etc). If true, or if processing the request might conflict with any resource locks, you must call the
  /// <see cref="CheckPreconditions"/> method to ensure that the preconditions are satisfied before proceeding with the request. It is
  /// okay (and inexpensive) to call <see cref="CheckPreconditions"/> even if this is false, so in general you do not need to check this
  /// property.
  /// </summary>
  /// <remarks>The main usage of this property is to avoid computing expensive <see cref="EntityMetadata"/> (especially the
  /// <see cref="EntityTag"/>) in cases when it won't be needed. In particular, if this property is false, then the
  /// <see cref="EntityMetadata"/> object passed to <see cref="CheckPreconditions"/> will not require an <see cref="EntityTag"/>.
  /// </remarks>
  public bool ClientSubmittedPreconditions
  {
    get { return ifClauses != null || ifMatch != null || ifNoneMatch != null || ifModifiedSince.HasValue || ifUnmodifiedSince.HasValue; }
  }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> representing the overall result of the request. If the status is null, the
  /// request is assumed to have been successful and an appropriate response will be used. In general, setting a status value will
  /// prevent a default entity body from being generated.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <summary>Determines whether a request should be processed given the precondition (i.e. <c>If</c>, <c>If-Match</c>,
  /// <c>If-None-Match</c>, <c>If-Modified-Since</c>, and <c>If-Unmodified-Since</c>) headers submitted by the client. This method also
  /// checks resource locks by calling <see cref="CheckSubmittedLockTokens()"/>.
  /// </summary>
  /// <param name="requestMetadata">The <see cref="EntityMetadata"/> for the request resource. If null, the metadata will be retrieved by
  /// calling <see cref="IWebDAVResource.GetEntityMetadata"/> on the <see cref="WebDAVContext.RequestResource"/> if it is available.
  /// </param>
  /// <returns>Returns null if the request should proceed normally. Otherwise, the status code that should be returned to the client is
  /// returned. This might be an error code (e.g. 412 Precondition Failed) or a redirection code (e.g. 304 Not Modified).
  /// </returns>
  /// <remarks>If the request would normally result in a response other than 2xx or 412 Precondition Failed, then that response must be
  /// given instead. That is to say, this method should only be called if the request would otherwise result in a 2xx response.
  /// In addition, if the request is a <c>GET</c> request and this method returns 304 Not Modified, the 304 response should be ignored if
  /// the <c>GET</c> request would otherwise have been responded to with anything but 200 OK. (For instance, if a particular <c>GET</c>
  /// request would normally be responded to with 206 Partial Content and this method returns 304 Not Modified, the request should be
  /// processed normally and return 206 Partial Content. However if this method returns any other status or if the <c>GET</c> request would
  /// normally be responded to with 200 OK, the status returned from this method should be returned to the client instead of processing the
  /// request.)
  /// <note type="caution">This method does not check the <c>If-Range</c> header, because it works differently from the other <c>If-</c>
  /// headers. When responding to a <c>GET</c> request, you should check the <c>If-Range</c> header using
  /// <see cref="GetOrHeadRequest.IfRange"/> or use <see cref="GetOrHeadRequest.WriteStandardResponse(System.IO.Stream,EntityMetadata)"/>,
  /// which checks it for you.)
  /// </note>
  /// </remarks>
  public ConditionCode CheckPreconditions(EntityMetadata requestMetadata)
  {
    // RFC 2616 leaves the behavior undefined when If-* headers are mixed together, so we will implement it by requiring the request to
    // pass all conditions and trying conditions that cause errors before those that cause redirections

    if(ifMatch != null || ifNoneMatch != null)
    {
      // load the request metadata with the entity tag if we need it
      if(requestMetadata == null || requestMetadata.EntityTag == null)
      {
        if(Context.RequestResource != null) requestMetadata = Context.RequestResource.GetEntityMetadata(true);
        if(requestMetadata == null) requestMetadata = new EntityMetadata() { Exists = false };
      }

      // as per RFC 2616 sections 14.24 and 14.26, if the entity exists, then the precondition fails if 1) If-None-Match = *, 2) If-Match
      // doesn't match the entity tag, or 3) If-None-Match does match the entity tag and the method is not GET or HEAD. otherwise, if the
      // entity doesn't exist, the precondition fails if If-Match was provided. if the entity exists and If-None-Match matches it and the
      // method is GET or HEAD, 304 Not Modified should be returned instead. that is handled later
      if(requestMetadata.Exists ? ifMatch != null && requestMetadata.EntityTag != null && !Matches(ifMatch, requestMetadata.EntityTag) ||
                                  Matches(ifNoneMatch, requestMetadata.EntityTag) && !IsGetOrHead()
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
        // as per RFC 2616 section 14.28, the precondition fails if the last modified date is after the If-Unmodified-Since date
        if(ifUnmodifiedSince.HasValue && utcTime > ifUnmodifiedSince) return ConditionCodes.PreconditionFailed;
      }
    }

    // RFC 4918 section 10.4.3 specifies the processing for the If header
    if(ifClauses != null)
    {
      bool anyListSucceeded = false;
      foreach(TaggedIfLists clause in ifClauses)
      {
        EntityMetadata metadata = null;
        if(clause.ResourceTag == null || clause.ResourceTag.OrdinalEquals(Context.ServiceRoot + Context.RequestPath) ||
           clause.ResourceTag[0] != '/' && clause.ResourceTagUri.Equals(Context.Request.Url))
        {
          bool checksEntityTag = clause.ChecksEntityTag();
          if((requestMetadata == null || checksEntityTag && requestMetadata.EntityTag == null) && Context.RequestResource != null)
          {
            requestMetadata = Context.RequestResource.GetEntityMetadata(checksEntityTag);
          }

          metadata = requestMetadata;
        }
        else
        {
          IWebDAVResource resource = WebDAVModule.ResolveUri(Context, clause.ResourceTagUri);
          if(resource != null) metadata = resource.GetEntityMetadata(clause.ChecksEntityTag());
        }

        foreach(IfList list in clause.Lists)
        {
          bool allConditionsSucceeded = true;
          foreach(IfCondition condition in list.Conditions)
          {
            bool match = false;
            if(metadata != null)
            {
              if(condition.EntityTag != null)
              {
                match = metadata != null && condition.EntityTag.Equals(metadata.EntityTag);
              }
              else if(Context.LockManager != null)
              {
                string absolutePath = clause.ResourceTag == null ? null :
                                      clause.ResourceTag[0] == '/' ? clause.ResourceTag : clause.ResourceTagUri.AbsolutePath;
                match = Context.LockManager.GetLock(condition.LockToken, absolutePath) != null;
              }
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

      if(!anyListSucceeded) return ConditionCodes.PreconditionFailed;
    }

    // now check that the required lock tokens have been submitted in the If header
    ConditionCode lockStatus = CheckSubmittedLockTokens();
    if(lockStatus != null) return lockStatus;

    if(ifNoneMatch != null)
    {
      // RFC 2616 section 14.26 says that if the entity exists and matches the If-None-Match header, and the request is GET or HEAD, then
      // 304 Not Modified should be returned
      if(requestMetadata.Exists && Matches(ifNoneMatch, requestMetadata.EntityTag) && IsGetOrHead())
      {
        // section 14.26 also says that if we respond with 304 Not Modified, then we should also the matching ETag value
        if(requestMetadata.EntityTag != null && string.IsNullOrEmpty(Context.Response.Headers[HttpHeaders.ETag]))
        {
          Context.Response.Headers[HttpHeaders.ETag] = requestMetadata.EntityTag.ToHeaderString();
        }
        return ConditionCodes.NotModified;
      }
    }

    // as per RFC 2616 section 14.25, a 304 Not Modified response should be returned if 1) a GET request would normally be
    // responded to with a 200 OK status (something we can't check here), and 2) the last modified time is not greater than
    // If-Modified-Since
    if(ifModifiedSince.HasValue && requestMetadata.LastModifiedTime.HasValue && utcTime <= ifModifiedSince)
    {
      return ConditionCodes.NotModified;
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
    }
    return lockTokens;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>The default implementation always returns null.
  /// <note type="inherit">Derived classes should typically implement this method by calling
  /// <see cref="CheckSubmittedLockTokens(LockType,bool,bool)"/> or one of its overrides.
  /// </note>
  /// </remarks>
  protected virtual ConditionCode CheckSubmittedLockTokens()
  {
    return null;
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensCore/node()" />
  protected ConditionCode CheckSubmittedLockTokens(LockType lockType, bool checkParent, bool checkDescendants)
  {
    return CheckSubmittedLockTokens(lockType, checkParent, checkDescendants, null, null);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensCore/node()" />
  /// <param name="absolutePath">The absolute path to the resource whose locks will be examined. The resource must be within the request
  /// service (i.e. controlled by <see cref="WebDAVContext.Service"/>). If null, the request resource will be used.
  /// </param>
  protected ConditionCode CheckSubmittedLockTokens(LockType lockType, bool checkParent, bool checkDescendants, string absolutePath)
  {
    return CheckSubmittedLockTokens(lockType, checkParent, checkDescendants, absolutePath, null);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensCore/node()" />
  /// <param name="absolutePath">The absolute path to the resource whose locks will be examined. If null, the request resource will be
  /// used and <paramref name="service"/> must also be null.
  /// </param>
  /// <param name="service">The <see cref="IWebDAVService"/> containing the resource whose locks will be examined. If null, the request
  /// service will be used. If not null, <paramref name="absolutePath"/> cannot be null either.
  /// </param>
  protected ConditionCode CheckSubmittedLockTokens(LockType lockType, bool checkParent, bool checkDescendants,
                                                   string absolutePath, IWebDAVService service)
  {
    if(lockType == null) throw new ArgumentNullException();

    if(absolutePath == null)
    {
      if(service != null) throw new ArgumentException("If absolutePath is null, service must also be null.");
      absolutePath = Context.ServiceRoot + (Context.RequestResource != null ? Context.RequestResource.CanonicalPath : Context.RequestPath);
    }

    if(Context.LockManager != null)
    {
      HashSet<string> submittedTokens = GetSubmittedLockTokens();
      Predicate<ActiveLock> filter = L => lockType.ConflictsWith(L.Type);
      bool isBuiltInType = lockType.GetType() == typeof(LockType); // can we make assumptions about the LockType implementation?

      if(checkParent) // if we have to check the parent collection for a lock...
      {
        string parentPath = DAVUtility.GetParentPath(absolutePath);
        if(parentPath != null) // if the resource has a parent...
        {
          IList<ActiveLock> locks = Context.LockManager.GetLocks(parentPath, true, false, filter);
          if(locks.Count != 0) // and the parent is locked...
          {
            ActiveLock lockObject = GetSubmittedLock(locks, submittedTokens); // see if a relevant token was submitted
            if(lockObject == null) return new LockTokenSubmittedConditionCode(parentPath); // none was submitted, so that's an error
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

      // now check locks directly on the resource
      IList<ActiveLock> directLocks = Context.LockManager.GetLocks(absolutePath, !checkParent, false, filter);
      if(directLocks.Count != 0)
      {
        ActiveLock lockObject = GetSubmittedLock(directLocks, submittedTokens); // see if a relevant token was submitted
        if(lockObject == null) return new LockTokenSubmittedConditionCode(absolutePath); // none was submitted, so that's an error
        if(lockObject.Recursive && checkDescendants)
        {
          if(isBuiltInType) return null; // see above for a description of this code
          else filter = Combine(filter, L => !lockObject.Type.ConflictsWith(L.Type));
        }
      }

      // now look at locks that are descendants of the resource
      if(checkDescendants)
      {
        IList<ActiveLock> locks = Context.LockManager.GetLocks(absolutePath, false, true, filter);
        List<ActiveLock> descendantLocks;
        if(directLocks.Count == 0)
        {
          descendantLocks = GetList(locks);
        }
        else
        {
          descendantLocks = new List<ActiveLock>(locks.Count);
          foreach(ActiveLock lockObject in locks)
          {
            if(lockObject.Path.Length > absolutePath.Length) descendantLocks.Add(lockObject);
          }
        }

        if(descendantLocks.Count != 0)
        {
          descendantLocks.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
          for(int i=0; i<descendantLocks.Count; )
          {
            ConditionCode status = CheckDescendantLocks(descendantLocks, ref i, submittedTokens, isBuiltInType ? null : filter);
            if(status != null) return status;
          }
        }
      }
    }

    return null;
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
      if(resourceTag != null && resourceTag.Length == 0 || lists.Length == 0) throw new ArgumentException();
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
  }
  #endregion

  bool IsGetOrHead()
  {
    return Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Get) || Context.Request.HttpMethod.OrdinalEquals(HttpMethods.Head);
  }

  TaggedIfLists[] ifClauses;
  EntityTag[] ifMatch, ifNoneMatch;
  DateTime? ifModifiedSince, ifUnmodifiedSince;

  /// <summary>Checks the lock at the given index along with some or all of its descendants, and advances the index to the next unchecked
  /// lock.
  /// </summary>
  static ConditionCode CheckDescendantLocks(List<ActiveLock> locks, ref int index, HashSet<string> submittedTokens,
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

    if(baseLock == null) return new LockTokenSubmittedConditionCode(basePath); // if the base lock wasn't submitted, return an error

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
      ConditionCode status = CheckDescendantLocks(locks, ref index, submittedTokens, filter);
      if(status != null) return status;
    }

    return null;
  }

  /// <summary>Combines two <see cref="Predicate{T}"/> of <see cref="ActiveLock"/> into a single predicate representing their conjunction.</summary>
  static Predicate<ActiveLock> Combine(Predicate<ActiveLock> a, Predicate<ActiveLock> b)
  {
    return L => a(L) && b(L);
  }

  static List<ActiveLock> GetList(IList<ActiveLock> ilist)
  {
    return ilist as List<ActiveLock> ?? new List<ActiveLock>(ilist);
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
  static bool Matches(EntityTag[] matches, EntityTag entityTag)
  {
    if(matches == MatchAny) return true;
    if(matches != null && entityTag != null)
    {
      foreach(EntityTag match in matches)
      {
        // RFC 2616 section 14.24 requires the strong match and 14.26 requires it for all requests except GET and HEAD. so we'll just do
        // it all the time even though we could choose to use a weak match for GET/HEAD requests when checking If-None-Match
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
        tag = ParseResourceTag(value, ref index);
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
          bool negated = value.StartsWith("Not ", StringComparison.Ordinal);
          if(negated)
          {
            index = SkipWhitespace(value, index+4);
            if(index == value.Length) return null;
          }

          if(value[index] == '<')
          {
            string lockToken = ParseResourceTag(value, ref index);
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

      Uri tagUri = null;
      if(tag != null && !DAVUtility.TryParseSimpleRef(tag, out tagUri)) return null;
      taggedLists.Add(new TaggedIfLists(tag, tagUri, lists.ToArray()));
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
