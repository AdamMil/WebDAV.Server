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
    string value = context.Request.Headers[DAVHeaders.Timeout];
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
  }

  /// <summary>A function called to create a new, empty file. The function should return a <see cref="ConditionCode"/> indicating whether
  /// the attempt succeeded or failed, or null for the standard success code.
  /// </summary>
  /// <param name="canonicalPath">A variable that will receive the canonical path to the newly created file, or null to use the path
  /// passed to the <see cref="ProcessStandardRequest(IEnumerable{LockType},bool,string,EntityMetadata,FileCreator)"/> method. This
  /// exists to support resources whose canonical path is not known before they're created.
  /// </param>
  public delegate ConditionCode FileCreator(out string canonicalPath);

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

  /// <summary>Gets or sets arbitrary data to be associated with any lock created by the lock request. If null, no additional information
  /// will be associated with the lock. This property is not used if a lock is merely refreshed and so cannot be used to alter the data
  /// associated with an existing lock.
  /// </summary>
  public XmlElement ServerData { get; set; }

  /// <summary>Processes a standard <c>LOCK</c> request for a new resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name = 'supportedLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, FileCreator createFile)
  {
    if(createFile == null) throw new ArgumentNullException();
    ProcessStandardRequest(supportedLocks, false, null, null, createFile);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name = 'supportedLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, Func<ConditionCode> createFile)
  {
    if(createFile == null) throw new ArgumentNullException();
    ProcessStandardRequest(supportedLocks, GetFileCreator(createFile));
  }

  /// <summary>Processes a standard <c>LOCK</c> request for an existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name = 'supportedLocks' or @name = 'supportsRecursiveLocks']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, (FileCreator)null);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name = 'supportedLocks' or @name = 'supportsRecursiveLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, FileCreator createFile)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, createFile);
  }

  /// <summary>Processes a standard <c>LOCK</c> request for a new or existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name = 'supportedLocks' or @name = 'supportsRecursiveLocks' or @name = 'createFile']" />
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, Func<ConditionCode> createFile)
  {
    ProcessStandardRequest(supportedLocks, supportsRecursiveLocks, null, null, GetFileCreator(createFile));
  }

  /// <summary>Processes a standard <c>LOCK</c> request for an existing resource.</summary>
  /// <include file="documentation.xml" path="/DAV/LockRequest/ProcessStandardRequest/*[@name != 'createFile']" />
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
  public void ProcessStandardRequest(IEnumerable<LockType> supportedLocks, bool supportsRecursiveLocks, string canonicalPath,
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
              if(status != null && !status.IsSuccessful) // if that failed...
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
  /// <remarks>The default implementation checks the lock timeouts requested by the user against <see cref="LockManager.MaximumTimeout"/>
  /// if <see cref="WebDAVContext.LockManager"/> derives from <see cref="LockManager"/>.
  /// </remarks>
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
    XmlDocument xml = Context.LoadRequestXml();
    if(xml == null) // if there's no request body, then the client wants us to refresh a lock
    {
      // RFC 4918 section 9.10.2 requires exactly one lock token to be submitted for refresh
      HashSet<string> lockTokens = GetSubmittedLockTokens();
      if(lockTokens.Count != 1)
      {
        throw Exceptions.BadRequest("Exactly one lock token must be submitted when refreshing a lock." +
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
    else if(Status == null || Status.IsSuccessful)
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
      // add the resource path with a 424 Failed Dependency error
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
