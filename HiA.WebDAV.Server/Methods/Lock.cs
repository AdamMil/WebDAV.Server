using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Xml;

// TODO: add processing examples and documentation
// TODO: only allow setting locks supported by the resource

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>LOCK</c> request.</summary>
/// <remarks>The <c>LOCK</c> request is described in section 9.10 of RFC 4918.</remarks>
public class LockRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="LockRequest"/> based on a new WebDAV request.</summary>
  public LockRequest(WebDAVContext context) : base(context)
  {
    // section 9.10.3 of RFC 4918 says a depth of infinity is the default, and a depth of 1 is disallowed
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants;
    else if(Depth == Depth.SelfAndChildren) throw Exceptions.BadRequest("The Depth header cannot be 1 for LOCK requests.");

    FailedResources = new FailedResourceCollection();

    // parse the Timeout header if specified
    string value = context.Request.Headers[HttpHeaders.Timeout];
    if(string.IsNullOrEmpty(value))
    {
      RequestedTimeouts = new ReadOnlyListWrapper<uint>(new uint[0]);
    }
    else
    {
      List<uint> requestedTimeouts = new List<uint>();
      foreach(string timeoutStr in value.Split(',', s => s.Trim(), StringSplitOptions.RemoveEmptyEntries))
      {
        if(timeoutStr.OrdinalEquals("Infinite"))
        {
          requestedTimeouts.Add(0);
        }
        else if(timeoutStr.StartsWith("Second-", StringComparison.Ordinal))
        {
          uint timeoutSeconds;
          if(uint.TryParse(timeoutStr.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out timeoutSeconds))
          {
            requestedTimeouts.Add(timeoutSeconds);
          }
        }
      }
      RequestedTimeouts = new ReadOnlyListWrapper<uint>(requestedTimeouts);
    }
  }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing resources that prevented the
  /// request resource from being locked. A <see cref="ResourceStatus"/> object representing the request resource itself can also be added,
  /// but if that is the only resource with an error, it is better to represent the error by setting <see cref="WebDAVRequest.Status"/> and
  /// leaving the collection empty.
  /// </summary>
  public FailedResourceCollection FailedResources { get; private set; }

  /// <summary>Gets whether the client wants to refresh an existing lock, as opposed to creating a new lock.</summary>
  public bool IsRefresh
  {
    get { return LockType == null; }
  }

  /// <summary>Gets the <see cref="LockType"/> requested by the client. If null, the client wants to refresh an existing lock.</summary>
  /// <remarks>This property is not valid until <see cref="ParseRequest"/> has been called.</remarks>
  public LockType LockType { get; protected set; }

  /// <summary>Gets or sets an <see cref="ActiveLock"/> object representing the lock created or refreshed by the lock request. This should
  /// be set when a lock was successfully created or refreshed.
  /// </summary>
  public ActiveLock NewLock { get; set; }

  /// <summary>Gets arbitrary information about and supplied by the client requesting the lock. If null, no owner information was
  /// submitted with the lock request.
  /// </summary>
  /// <remarks>This property is not valid until <see cref="ParseRequest"/> has been called.</remarks>
  public XmlElement OwnerData { get; protected set; }

  /// <summary>Gets a collection of lock timeout values requested by the client, from most to least preferred. The expected behavior is
  /// that the server should use the first timeout value it finds acceptable, but the server is not required to use any of them.
  /// If empty, no timeout value was suggested by the client, and the server should use a default timeout value (usually infinite).
  /// </summary>
  public ReadOnlyListWrapper<uint> RequestedTimeouts { get; private set; }

  /// <summary>Processes a standard <c>LOCK</c> request for an existing resource.</summary>
  /// <param name="supportsRecursiveLocks">True if the resource supports recursive locks and false if not. Typically, true should be passed
  /// for collection resources and false for non-collection resources.
  /// </param>
  public void ProcessStandardRequest(bool supportsRecursiveLocks)
  {
    ProcessStandardRequest(null, null, supportsRecursiveLocks);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <param name="absolutePath">The absolute, canonical path of the resource to lock. If null, the path to the
  /// <see cref="WebDAVContext.RequestResource"/> will be used, if it's available. If null and the request resource is not available, an
  /// exception will be thrown.
  /// </param>
  /// <param name="metadata">The <see cref="EntityMetadata"/> of the resource. If null, the metadata will be retrieved by calling
  /// <see cref="IWebDAVResource.GetEntityMetadata"/> on the <see cref="WebDAVContext.RequestResource"/> if it's needed and the request
  /// resource is available.
  /// </param>
  /// <param name="supportsRecursiveLocks">True if the resource supports recursive locks and false if not. Typically, true should be passed
  /// for collection resources and false for non-collection resources.
  /// </param>
  public void ProcessStandardRequest(string absolutePath, EntityMetadata metadata, bool supportsRecursiveLocks)
  {
    if(Context.LockManager == null) // if there's no lock manager, the resource can't be locked
    {
      Status = new ConditionCode((int)HttpStatusCode.Forbidden, "This resource does not support locking.");
    }
    else // otherwise, we can try to lock it
    {
      if(absolutePath == null)
      {
        if(Context.RequestResource == null) throw new ArgumentException("A lock path must be provided if there is no request resource.");
        absolutePath = Context.ServiceRoot + Context.RequestResource.CanonicalPath;
      }

      bool recursive = supportsRecursiveLocks && Depth == Depth.SelfAndDescendants;
      ConditionCode precondition = CheckPreconditions(metadata);
      if(precondition != null) // if the preconditions weren't satisfied...
      {
        if(precondition.StatusCode != (int)HttpStatusCode.NotModified)
        {
          Status = precondition; // then immediately return the precondition status
        }
        else // otherwise, see if there are any conflicting locks. if so, return the error. if not, return the precondition status
        {
          ProcessConflictingLocks(absolutePath, recursive, false);
          if(Status == null || Status.IsSuccessful) Status = precondition;
        }
      }
      else // the preconditions were satisfied...
      {
        uint? requestedTimeout = RequestedTimeouts.Count == 0 ? (uint?)null : RequestedTimeouts[0];
        if(IsRefresh) // if the client wants to refresh a lock...
        {
          ActiveLock lockObject = Context.LockManager.GetLock(GetSubmittedLockTokens().First(), absolutePath);
          if(lockObject == null) // if there's no matching lock, issue an error
          {
            Status = ConditionCodes.LockTokenMatchesRequestUri412;
          }
          else // otherwise, the lock matches, so refresh it
          {
            Context.LockManager.RefreshLock(lockObject, requestedTimeout);
            NewLock = lockObject;
          }
        }
        else // the client wants to create a new lock...
        {
          try { NewLock = Context.LockManager.AddLock(absolutePath, LockType, recursive, requestedTimeout, OwnerData); }
          catch(LockConflictException) { ProcessConflictingLocks(absolutePath, recursive, true); }
        }
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    XmlDocument xml = Context.LoadRequestXml();
    if(xml == null) // if there's no request body, then the client wants us to refresh a lock
    {
      // RFC 4918 section 9.10.2 requires exactly one lock token to be submitted for refresh
      HashSet<string> lockTokens = GetSubmittedLockTokens();
      if(lockTokens.Count != 1) throw Exceptions.BadRequest("Exactly one lock token must be submitted when refreshing a lock.");
    }
    else
    {
      xml.DocumentElement.AssertName(Names.lockinfo);
      XmlQualifiedName type = null;
      bool? exclusive = null;
      foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements())
      {
        if(child.HasChildNodes)
        {
          if(child.HasName(Names.lockscope))
          {
            if(child.FirstChild.HasName(Names.exclusive)) exclusive = true;
            else if(child.FirstChild.HasName(Names.shared)) exclusive = false;
          }
          else if(child.HasName(Names.locktype))
          {
            type = child.FirstChild.GetQualifiedName();
          }
          else if(child.HasName(Names.owner))
          {
            OwnerData = child.Extract();
          }
        }
      }

      if(type == null || !exclusive.HasValue) throw Exceptions.BadRequest("Expected a valid DAV:lockinfo element.");
      LockType = new LockType(type, exclusive.Value);
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedResources"/> is not empty, outputs a
  /// <c>DAV:lockdiscovery</c> element if <see cref="NewLock"/> is not null, and writes a response based on
  /// <see cref="WebDAVRequest.Status"/> otherwise.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedResources.Count != 0)
    {
      Context.WriteFailedMembers(FailedResources);
    }
    else if(NewLock != null)
    {
      // the Lock-Token header should be supplied when a new lock is created, but not when a lock is refreshed (RFC 4918 section 9.10)
      if(!IsRefresh) Context.Response.Headers[HttpHeaders.LockToken] = "<" + NewLock.LockToken + ">";

      using(XmlWriter writer = Context.OpenResponseXml(Status ?? ConditionCodes.OK))
      {
        writer.WriteStartElement(Names.prop);
        writer.WriteAttributeString("xmlns", Names.DAV);
        writer.WriteStartElement(Names.lockdiscovery);
        ((IElementValue)NewLock).WriteValue(writer, Context);
        writer.WriteEndElement(); // lockdiscovery
        writer.WriteEndElement(); // prop
      }
    }
    else if(Status == null || Status.IsSuccessful)
    {
      throw new ContractViolationException("A successful LOCK request must set NewLock to the new or refreshed lock.");
    }
    else
    {
      Context.WriteStatusResponse(Status);
    }
  }

  void ProcessConflictingLocks(string lockPath, bool recursive, bool forceError)
  {
    HashSet<string> paths = Context.LockManager.GetConflictingLocks(lockPath, LockType, recursive).Select(L => L.LockPath).ToSet();
    foreach(string path in paths) FailedResources.Add(path, ConditionCodes.Locked);

    if(FailedResources.Count == 0) // if there weren't any known conflicting locks...
    {
      if(forceError) Status = ConditionCodes.Locked; // then issue a generic Locked error if we're to force an error status
    }
    else if(!paths.Contains(DAVUtility.RemoveTrailingSlash(lockPath))) // if the request path didn't conflict but other paths did...
    {
      // add the resource path with a 424 Failed Dependency error
      FailedResources.Add(Context.ServiceRoot + Context.RequestPath, ConditionCodes.FailedDependency);
    }
    else if(FailedResources.Count == 1) // if there was only one conflict, which related to the request path...
    {
      FailedResources.Clear(); // use the HTTP 423 Locked status rather than a 207 Multi-Status response
      Status = ConditionCodes.Locked;
    }
  }
}

} // namespace HiA.WebDAV.Server
