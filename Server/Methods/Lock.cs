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
using System.Linq;
using System.Net;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;

// TODO: RFC 4918 section 9.10.1 has a proposed erratum changing it to say that only the request-URI is locked in a LOCK request. if that
// change is adopted, we won't be able to use the canonical resource URL, and will need to update lock-related code in various places.
// check back later to see the status of the proposal

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>LOCK</c> request.</summary>
/// <remarks>
/// <para>The <c>LOCK</c> request is described in section 9.10 of RFC 4918. To service a <c>LOCK</c> request, you can normally just call
/// the <see cref="O:AdamMil.WebDAV.Server.LockRequest.ProcessStandardRequest">ProcessStandardRequest</see> method.
/// </para>
/// <para>If you would like to handle it yourself, you should call <see cref="ILockManager.RefreshLock"/> using the lock token returned
/// from <see cref="WebDAVRequest.GetSubmittedLockTokens"/> if <see cref="IsRefresh"/> is true, and call <see cref="ILockManager.AddLock"/>
/// to create a new lock if <see cref="IsRefresh"/> is false. When creating a new lock on a nonexistent resource, a new, empty,
/// non-collection resource should be created at the request URL and locked. In any case, if the request was successful the new or
/// refreshed lock should be set in the <see cref="NewLock"/> property so it can be reported to the client. If any resources prevent the
/// lock from being created (e.g. due to conflicting locks), you should add them to <see cref="FailedResources"/>, unless the only conflict
/// was with the request resource itself, in which case you should simply report the error in <see cref="WebDAVRequest.Status"/>. The list
/// of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/> (default)</term>
///   <description>A lock was successfully created or refreshed. The response body must contain a <c>DAV:prop</c> element that contains a
///     <c>DAV:lockdiscovery</c> element describing the lock. Such a response will be generated automatically if you set the
///     <see cref="NewLock"/> property. This is the default status code returned when <see cref="WebDAVRequest.Status"/> is null.
///   </description>
/// </item>
/// <item>
///   <term>207 <see cref="ConditionCodes.MultiStatus">Multi-Status</see></term>
///   <description>This status code should be used along with a <c>DAV:multistatus</c> XML body when multiple resources prevented the lock
///     from being created, or when a single resource that was not the request resource prevented it from being created. Such a response
///     will automatically be generated if items are added to <see cref="FailedResources"/>. The error codes listed in this table may be used
///     for the resources in a 207 Multi-Status response, except 409 Conflict and 412 Precondition Failed. If the request resource itself
///     didn't prevent the lock from being created, the response should include the request resource with a
///     424 <see cref="ConditionCodes.FailedDependency">Failed Dependency</see> status.
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to lock the resource, or the server refuses to lock the resource for some other reason.</description>
/// </item>
/// <item>
///   <term>405 <see cref="ConditionCodes.MethodNotAllowed">Method Not Allowed</see></term>
///   <description>The request resource does not support being locked. If you return this status code, then you must not include the
///     <c>LOCK</c> or <c>UNLOCK</c> method in responses to <c>OPTIONS</c> requests (i.e. <see cref="OptionsRequest.SupportsLocking"/>
///     must be false).
///   </description>
/// </item>
/// <item>
///   <term>409 <see cref="ConditionCodes.Conflict"/></term>
///   <description>The request was submitted to an unmapped URL but a new resource could not be created because the parent collection does
///     not exist. This collection must not be created automatically.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>The client attempted to refresh a lock but the request URI didn't fall within the scope of the lock, or a conditional
///     request was not executed because the condition wasn't true.
///   </description>
/// </item>
/// <item>
///   <term>423 <see cref="ConditionCodes.Locked"/></term>
///   <description>The resource was already locked with a conflicting lock. This may be because the locks themselves conflicted, or because
///     the user already has a lock on the resource and isn't allowed to create another. The response body should include the
///     <c>DAV:no-conflicting-lock</c> precondition code if applicable.
///   </description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="GetAppropriateTimeout"/></term>
///   <description>You want to change how the lock timeout is chosen.</description>
/// </item>
/// <item>
///   <term><see cref="ParseRequestXml"/></term>
///   <description>You want to change how the request XML body is parsed or validated.</description>
/// </item>
/// <item>
///   <term><see cref="ProcessStandardRequest(IEnumerable{LockType},bool,string,EntityMetadata,FileCreator)"/></term>
///   <description>You want to change the standard request processing.</description>
/// </item>
/// </list>
/// </remarks>
public class LockRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="LockRequest"/> based on a new WebDAV request.</summary>
  public LockRequest(WebDAVContext context) : base(context)
  {
    // section 9.10.3 of RFC 4918 says a depth of infinity is the default, and a depth of 1 is disallowed
    string value = context.Request.Headers[DAVHeaders.Depth];
    if(string.IsNullOrEmpty(value) || "infinity".OrdinalEquals(value)) Depth = Depth.SelfAndDescendants;
    else if("0".OrdinalEquals(value)) Depth = Depth.Self;
    else if("1".OrdinalEquals(value)) Depth = Depth.SelfAndChildren;
    else throw Exceptions.BadRequest("The Depth header must be 0 or infinity for LOCK requests, or unspecified.");

    // parse the Timeout header if specified. (see RFC 4918 section 10.7)
    value = context.Request.Headers[DAVHeaders.Timeout];
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
          if(InvariantCultureUtility.TryParse(timeoutStr, 7, timeoutStr.Length-7, out timeoutSeconds))
          {
            requestedTimeouts.Add(timeoutSeconds);
          }
        }
      }
      RequestedTimeouts = new ReadOnlyListWrapper<uint>(requestedTimeouts);
    }

    FailedResources = new FailedResourceCollection();
  }

  /// <summary>A function called to create a new, empty file. The function should return a <see cref="ConditionCode"/> indicating whether
  /// the attempt succeeded or failed, or null for the standard success code.
  /// </summary>
  /// <param name="canonicalPath">A variable that will receive the canonical path to the newly created file, or null to use the path
  /// passed to the <see cref="O:AdamMil.WebDAV.Server.LockRequest.ProcessStandardRequest">ProcessStandardRequest</see> method. This
  /// exists to support resources whose canonical path is not known before they're created.
  /// </param>
  public delegate ConditionCode FileCreator(out string canonicalPath);

  /// <summary>Gets the recursion depth requested by the client in the <c>Depth</c> header.</summary>
  public Depth Depth { get; protected set; }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing resources that prevented the
  /// request resource from being locked.
  /// </summary>
  /// <remarks>A <see cref="ResourceStatus"/> object representing the request resource itself can also be added, but if that is the only
  /// resource with an error, it is better to represent the error by setting <see cref="WebDAVRequest.Status"/> and leaving the collection
  /// empty. If the request resource itself didn't prevent the lock from being created but other resources did, you should also add the
  /// request resource with a 424 <see cref="ConditionCodes.FailedDependency">Failed Dependency</see> status.
  /// </remarks>
  public FailedResourceCollection FailedResources { get; private set; }

  /// <summary>Gets whether the client wants to refresh an existing lock, as opposed to creating a new lock.</summary>
  public bool IsRefresh
  {
    get { return LockType == null; }
  }

  /// <summary>Gets the <see cref="LockType"/> requested by the client. If null, the client wants to refresh an existing lock.</summary>
  /// <remarks>This property is not valid until <see cref="ParseRequest"/> has been called.</remarks>
  public LockType LockType { get; protected set; }

  /// <summary>Gets or sets an <see cref="ActiveLock"/> object representing the lock created or refreshed by the lock request.</summary>
  /// <remarks>This property should be set when a lock was successfully created or refreshed.</remarks>
  public ActiveLock NewLock { get; set; }

  /// <summary>Gets arbitrary information about and supplied by the client requesting the lock. If null, no owner information was
  /// submitted with the lock request.
  /// </summary>
  /// <remarks>This property is not valid until <see cref="ParseRequest"/> has been called.</remarks>
  public XmlElement OwnerData { get; protected set; }

  /// <summary>Gets a collection of lock timeout values requested by the client, from most to least preferred.</summary>
  /// <remarks>The expected behavior is that the server should use the first timeout value it finds acceptable, but the server is not
  /// required to use any of them. If empty, no timeout value was suggested by the client, and the server should use a default timeout
  /// value (usually infinite).
  /// </remarks>
  public ReadOnlyListWrapper<uint> RequestedTimeouts { get; private set; }

  /// <summary>Gets or sets arbitrary data to be associated with any lock created by the lock request. If null, no additional information
  /// will be associated with the lock.
  /// </summary>
  /// <remarks>This property is not used if a lock is merely refreshed and so cannot be used to alter the data
  /// associated with an existing lock. This property must be set before calling a method like
  /// <see cref="O:AdamMil.WebDAV.Server.LockRequest.ProcessStandardRequest">ProcessStandardRequest</see>.
  /// </remarks>
  public XmlElement ServerData { get; set; }

  /// <summary>Processes a standard <c>LOCK</c> request for a new resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/param[not(@name) or @name = 'supportedLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, FileCreator createFile)
  {
    if(createFile == null) throw new ArgumentNullException();
    ProcessStandardRequest(supportedLocks, false, null, null, createFile);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/param[not(@name) or @name = 'supportedLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, Func<ConditionCode> createFile)
  {
    if(createFile == null) throw new ArgumentNullException();
    ProcessStandardRequest(supportedLocks, GetFileCreator(createFile));
  }

  /// <summary>Processes a standard <c>LOCK</c> request for an existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/param[not(@name) or @name = 'supportedLocks' or @name = 'supportsRecursiveLocks']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, (FileCreator)null);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/param[not(@name) or @name = 'supportedLocks' or @name = 'supportsRecursiveLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, FileCreator createFile)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, createFile);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/param[not(@name) or @name = 'supportedLocks' or @name = 'supportsRecursiveLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, Func<ConditionCode> createFile)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, GetFileCreator(createFile));
  }

  /// <summary>Processes a standard <c>LOCK</c> request for an existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[not(@name='createFile')]" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, string canonicalPath,
                                     EntityMetadata metadata)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, canonicalPath, metadata, (FileCreator)null);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/node()" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, string canonicalPath,
                                     EntityMetadata metadata, Func<ConditionCode> createFile)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, canonicalPath, metadata, GetFileCreator(createFile));
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/node()" />
  public virtual void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, string canonicalPath,
                                             EntityMetadata metadata, FileCreator createFile)
  {
    if(Context.RequestResource == null && createFile == null && !IsRefresh)
    {
      throw new ArgumentException("A file creation function must be provided if there is no request resource and this isn't a refresh.");
    }

    if(Context.LockManager == null) // if there's no lock manager, the resource can't be locked
    {
      Status = new ConditionCode(HttpStatusCode.MethodNotAllowed, "This resource does not support locking.");
    }
    else if(supportedLocks != null && LockType != null && !supportedLocks.Any(L => LockType.Equals(L))) // is the lock type unsupported?
    {
      Status = new ConditionCode(HttpStatusCode.BadRequest, LockType.ToString() + " locks are not supported by this resource.");
    }
    else // otherwise, we can try to lock it
    {
      if(canonicalPath == null) canonicalPath = Context.GetCanonicalPath();
      ConditionCode precondition = CheckPreconditions(metadata, canonicalPath);
      if(precondition != null) // if the preconditions weren't satisfied...
      {
        Status = precondition;
      }
      else // the preconditions were satisfied...
      {
        bool recursive = supportsRecursiveLocks && Depth == Depth.SelfAndDescendants;
        uint? requestedTimeout = GetAppropriateTimeout();
        if(IsRefresh) // if the client wants to refresh a lock...
        {
          ActiveLock lockObject = Context.LockManager.GetLock(GetSubmittedLockTokens().First(), canonicalPath);
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
          try
          {
            NewLock = Context.LockManager.AddLock(canonicalPath, LockType, GetLockSelection(recursive), requestedTimeout,
                                                  Context.CurrentUserId, OwnerData, ServerData);
            if(Context.RequestResource == null) // if the URL was unmapped...
            {
              ConditionCode status = DAVUtility.TryExecute(() => // try to create a new file
              {
                string newCanonicalPath;
                ConditionCode s = createFile(out newCanonicalPath); // let the function give us the new canonical URL for the lock manager
                if(newCanonicalPath != null) canonicalPath = newCanonicalPath;
                return s;
              });
              if(!DAVUtility.IsSuccess(status)) // if that failed...
              {
                Context.LockManager.RemoveLock(NewLock); // remove the lock that we added
                NewLock = null;
                Status = status; // and return the error
                return;
              }
              Status = ConditionCodes.Created; // if we created a new resource, use 201 Created rather than the default of 200 OK
            }
          }
          catch(LockConflictException)
          {
            ProcessConflictingLocks(canonicalPath, recursive, true);
          }
          catch(LockLimitReachedException ex)
          {
            // 503 Service Unavailable seems like the best choice among the available HTTP status codes for reporting the lock limit.
            // the only other one that's similar is 507 Insufficient Storage, but that's more about disk space and requires that the
            // client not retry automatically, whereas we don't want to prevent automatic retry
            Status = ex.ConditionCode ?? new ConditionCode(HttpStatusCode.ServiceUnavailable, "A lock limit has been reached.");
          }
        }
      }
    }
  }

  /// <summary>Returns the most appropriate lock timeout requested by the client, or null if the client did not request a lock timeout.</summary>
  /// <remarks><note type="inherit">The default implementation checks the lock timeouts requested by the user against
  /// <see cref="LockManager.MaximumTimeout"/> if <see cref="WebDAVContext.LockManager"/> derives from <see cref="LockManager"/>.
  /// </note></remarks>
  protected virtual uint? GetAppropriateTimeout()
  {
    if(RequestedTimeouts.Count == 0)
    {
      return null;
    }
    else
    {
      // if there are multiple requested timeouts, try to return the first one that complies with the limits set in the lock manager, if
      // we know what the limits are. we know some of the limits, at least, if it's a built-in LockManager class
      if(RequestedTimeouts.Count > 1)
      {
        LockManager builtInManager = Context.LockManager as LockManager;
        if(builtInManager != null && builtInManager.MaximumTimeout != 0)
        {
          for(int i=0; i<RequestedTimeouts.Count; i++)
          {
            uint timeout = RequestedTimeouts[i];
            if(timeout != 0 && timeout <= builtInManager.MaximumTimeout) return timeout;
          }
        }
      }
      return RequestedTimeouts[0];
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    if(Depth != Depth.SelfAndDescendants && Depth != Depth.Self)
    {
      Status = new ConditionCode(HttpStatusCode.BadRequest, "The Depth header must be 0 or infinity for LOCK requests, or unspecified.");
      return;
    }
    ParseRequestXml(Context.LoadRequestXml());
  }

  /// <summary>Called by <see cref="ParseRequest"/> to parse and validate the XML request body. The <see cref="XmlDocument"/> will be null
  /// if the client did not submit a body.
  /// </summary>
  /// <remarks>If the request body is invalid, this method should set <see cref="WebDAVRequest.Status"/> to an appropriate error code.</remarks>
  protected virtual void ParseRequestXml(XmlDocument xml)
  {
    if(xml == null) // if there's no request body, then the client wants us to refresh a lock
    {
      // RFC 4918 section 9.10.2 requires exactly one lock token to be submitted for refresh
      HashSet<string> lockTokens = GetSubmittedLockTokens();
      if(lockTokens.Count != 1)
      {
        Status = new ConditionCode(HttpStatusCode.BadRequest, "Exactly one lock token must be submitted when refreshing a lock." +
                                   (lockTokens.Count == 0 ? " If you intended to create a lock, the request body was missing." : null));
      }
    }
    else
    {
      xml.DocumentElement.AssertName(DAVNames.lockinfo);
      XmlQualifiedName type = null;
      bool? exclusive = null;
      foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements())
      {
        if(child.HasChildNodes)
        {
          if(child.HasName(DAVNames.lockscope))
          {
            if(child.FirstChild.HasName(DAVNames.exclusive)) exclusive = true;
            else if(child.FirstChild.HasName(DAVNames.shared)) exclusive = false;
          }
          else if(child.HasName(DAVNames.locktype))
          {
            type = child.FirstChild.GetQualifiedName();
          }
          else if(child.HasName(DAVNames.owner))
          {
            OwnerData = child.Extract();
          }
        }
      }

      if(type != null && exclusive.HasValue) LockType = new LockType(type, exclusive.Value);
      else Status = new ConditionCode(HttpStatusCode.BadRequest, "Expected a valid DAV:lockinfo element.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks><note type="inherit">This implementation writes a multi-status response if <see cref="FailedResources"/> is not empty,
  /// outputs a <c>DAV:lockdiscovery</c> element if <see cref="NewLock"/> is not null, and writes a response based on
  /// <see cref="WebDAVRequest.Status"/> otherwise.
  /// </note></remarks>
  protected internal override void WriteResponse()
  {
    if(FailedResources.Count != 0)
    {
      Context.WriteFailedMembers(FailedResources);
    }
    else if(NewLock != null)
    {
      // the Lock-Token header should be supplied when a new lock is created, but not when a lock is refreshed (RFC 4918 section 9.10)
      if(!IsRefresh) Context.Response.Headers[DAVHeaders.LockToken] = "<" + NewLock.Token + ">";

      using(XmlWriter writer = Context.OpenXmlResponse(Status ?? ConditionCodes.OK))
      {
        string davPrefix = Context.UseExplorerHacks() ? "D" : null; // Windows Explorer can't handle responses without prefixes
        writer.WriteStartElement(davPrefix, DAVNames.prop.Name, DAVNames.prop.Namespace);
        if(davPrefix == null) writer.WriteAttributeString("xmlns", DAVNames.DAV); // add the xmlns attribute for the DAV: namespace
        else writer.WriteAttributeString("xmlns", davPrefix, null, DAVNames.DAV);
        writer.WriteStartElement(DAVNames.lockdiscovery);
        ((IElementValue)NewLock).WriteValue(writer, Context);
        writer.WriteEndElement(); // lockdiscovery
        writer.WriteEndElement(); // prop
      }
    }
    else if(DAVUtility.IsSuccess(Status))
    {
      throw new ContractViolationException("A successful LOCK request must set NewLock to the new or refreshed lock.");
    }
    else
    {
      Context.WriteStatusResponse(Status);
    }
  }

  LockSelection GetLockSelection(bool recursive)
  {
    LockSelection selection = recursive ? LockSelection.RecursiveUpAndDown : LockSelection.SelfAndRecursiveAncestors;
    if(Context.RequestResource == null) selection |= LockSelection.Parent;
    return selection;
  }

  bool ProcessConflictingLocks(string lockPath, bool recursive, bool forceError)
  {
    HashSet<string> paths =
      Context.LockManager.GetConflictingLocks(lockPath, LockType, GetLockSelection(recursive), Context.CurrentUserId)
        .Select(L => L.Path).ToSet();
    foreach(string path in paths) FailedResources.Add(Context.ServiceRoot, path, ConditionCodes.Locked);

    if(FailedResources.Count == 0) // if there weren't any known conflicting locks...
    {
      if(forceError) Status = ConditionCodes.Locked; // then issue a generic Locked error if we're to force an error status
      return forceError;
    }
    else if(!paths.Contains(DAVUtility.RemoveTrailingSlash(lockPath))) // if the request path didn't conflict but other paths did...
    {
      // add the request resource with a 424 Failed Dependency error
      FailedResources.Add(Context.ServiceRoot, Context.RequestPath, ConditionCodes.FailedDependency);
    }
    else if(FailedResources.Count == 1) // if there was only one conflict, which related to the request path...
    {
      FailedResources.Clear(); // use the HTTP 423 Locked status rather than a 207 Multi-Status response
      Status = ConditionCodes.Locked;
    }

    // if multiple resources failed, we're going to write a 207 Multi-Status response. we'll report that in Status even though we'll
    // ignore Status later in order to communicate that an unusual condition occurred, given that a null status normally means success
    if(FailedResources.Count > 1) Status = ConditionCodes.MultiStatus;

    return true;
  }

  static FileCreator GetFileCreator(Func<ConditionCode> createFile)
  {
    return createFile == null ? null : (FileCreator)delegate(out string canonicalPath) { canonicalPath = null; return createFile(); };
  }
}

} // namespace AdamMil.WebDAV.Server
