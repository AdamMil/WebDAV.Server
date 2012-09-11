using System;
using System.Collections.Generic;
using System.Xml;

// TODO: implement some DOS protection (e.g. providing options to limit the number of shared locks, etc.)
// TODO: add some helper methods somewhere to encapsulate common lock-related actions like getting the locks on a resource (e.g. the
// request.Context.LockManager.GetLocks(request.Context.ServiceRoot + CanonicalPath, true, false, null) bit)
// TODO: make LockManager use a ReaderWriterLockSlim

namespace HiA.WebDAV.Server
{

#region ActiveLock
/// <summary>Describes an active lock on a resource. This object is used with the <c>DAV:lockdiscovery</c> property.</summary>
public class ActiveLock : IElementValue
{
  /// <summary>Initializes a new <see cref="ActiveLock"/> object.</summary>
  /// <param name="absolutePath">The absolute, canonical path to the resource to which this <see cref="ActiveLock"/> object pertains..</param>
  /// <param name="absoluteLockPath">The absolute, canonical path to the resource to which the lock is directly applied.</param>
  /// <param name="lockToken">A URI string uniquely identifying the lock. The string should be unique among all locks on all resources
  /// on all servers in the world. (A good practice is to use the urn:uuid URI scheme defined in RFC 4122.)
  /// </param>
  /// <param name="lockType">A <see cref="LockType"/> object representing the type and scope of the lock.</param>
  /// <param name="recursive">Whether the lock is recursive.</param>
  /// <param name="creationTime">The time when the lock was originally created. If the time has a <see cref="DateTimeKind"/> of
  /// <see cref="DateTimeKind.Local"/>, it will be converted to UTC. Otherwise it is assumed to be in UTC already.
  /// </param>
  /// <param name="timeoutSeconds">The number of seconds after <paramref name="creationTime"/> before the lock expires, or 0 if the lock
  /// does not expire.
  /// </param>
  /// <param name="ownerData">Arbitrary data about the owner of the lock submitted by the client.</param>
  /// <exception cref="ArgumentException">Thrown if <paramref name="lockToken"/> is empty, or if <paramref name="absolutePath"/> doesn't
  /// equal <paramref name="absoluteLockPath"/> but <paramref name="recursive"/> is false, or if <paramref name="ownerData"/> is not a
  /// <c>DAV:owner</c> element.
  /// </exception>
  public ActiveLock(string absolutePath, string absoluteLockPath, string lockToken, LockType lockType, bool recursive,
                    DateTime creationTime, uint timeoutSeconds, XmlElement ownerData)
  {
    if(absolutePath == null || absoluteLockPath == null || lockToken == null || lockType == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(lockToken)) throw new ArgumentException("A lock token is required.");

    absolutePath     = DAVUtility.RemoveTrailingSlash(absolutePath);
    absoluteLockPath = DAVUtility.RemoveTrailingSlash(absoluteLockPath);
    LockManager.ValidateAbsolutePath(absolutePath);
    LockManager.ValidateAbsolutePath(absoluteLockPath);

    Direct = absolutePath.OrdinalEquals(absoluteLockPath);
    if(!Direct && !recursive)
    {
      throw new ArgumentException("A non-recursive cannot be inherited. (The path and lock path must be identical.)");
    }

    LockPath  = absoluteLockPath;
    LockToken = lockToken;
    LockType  = lockType;
    Path      = absolutePath;
    Recursive = recursive;

    if(ownerData != null)
    {
      if(!ownerData.HasName(Names.owner)) throw new ArgumentException("The owner data must be a DAV:owner element.");
      _owner = ownerData.Extract();
    }

    CreationTime = creationTime.Kind == DateTimeKind.Local ?
      creationTime.ToUniversalTime() : DateTime.SpecifyKind(creationTime, DateTimeKind.Utc);

    Timeout = timeoutSeconds;
    if(timeoutSeconds != 0) ExpirationTime = CreationTime.AddSeconds(timeoutSeconds);
  }

  /// <summary>Gets the time when the lock was originally created, in UTC.</summary>
  public DateTime CreationTime { get; private set; }

  /// <summary>If true, the lock is directly applied to the resource named by the <see cref="Path"/>. If false, the resource inherits the
  /// lock from a parent collection at <see cref="LockPath"/>.
  /// </summary>
  public bool Direct { get; private set; }

  /// <summary>Gets the time when the lock is scheduled to time out, in UTC, or null if the lock should not time out.</summary>
  public DateTime? ExpirationTime { get; private set; }

  /// <summary>Gets the absolute path to the resource to which the lock is directly applied. The path is canonical except that it does not
  /// have a trailing slash. It is safe to pass this path to <see cref="ILockManager"/> methods.
  /// </summary>
  public string LockPath { get; private set; }

  /// <summary>Gets a URI string that uniquely identifies this lock.</summary>
  public string LockToken { get; private set; }

  /// <summary>Gets the type and scope of the lock.</summary>
  public LockType LockType { get; private set; }

  /// <summary>Gets the absolute path to the resource to which this <see cref="ActiveLock"/> object pertains. The path is canonical except
  /// that it does not have a trailing slash. It is safe to pass this path to <see cref="ILockManager"/> methods.
  /// </summary>
  public string Path { get; private set; }

  /// <summary>Gets whether the lock is recursive.</summary>
  public bool Recursive { get; private set; }

  /// <summary>Gets the number of seconds most recently used to compute the <see cref="ExpirationTime"/>, or zero if the lock should not
  /// expire.
  /// </summary>
  public uint Timeout { get; private set; }

  /// <summary>Returns arbitrary information about and supplied by the client requesting the lock. If null, no owner information was
  /// submitted with the lock request.
  /// </summary>
  public XmlElement GetOwnerData()
  {
    return _owner == null ? null : (XmlElement)_owner.Clone();
  }

  /// <summary>Gets whether the given absolute path is within the scope of the lock.</summary>
  public bool IsInScope(string absolutePath)
  {
    if(absolutePath == null) throw new ArgumentNullException();
    absolutePath = DAVUtility.RemoveTrailingSlash(absolutePath);
    LockManager.ValidateAbsolutePath(absolutePath);
    return LockPath.OrdinalEquals(absolutePath) ||
           Recursive && absolutePath.StartsWith(LockPath, StringComparison.Ordinal) && absolutePath[LockPath.Length] == '/';
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return "Lock " + LockToken + " of type " + LockType.ToString() + " on " + LockPath + (Recursive ? " (recursive)" : null);
  }

  /// <summary>Refreshes the lock timeout by computing a new <see cref="ExpirationTime"/> <paramref name="timeoutSeconds"/> seconds in the
  /// future, or setting <see cref="ExpirationTime"/> to null if <paramref name="timeoutSeconds"/> is zero.
  /// </summary>
  protected internal void Refresh(uint timeoutSeconds)
  {
    lock(this)
    {
      ExpirationTime = timeoutSeconds == 0 ? (DateTime?)null : DateTime.UtcNow.AddSeconds(timeoutSeconds);
      Timeout        = timeoutSeconds;
    }
  }

  #region IElementValue Members
  IEnumerable<string> IElementValue.GetNamespaces()
  {
    return ((IElementValue)LockType).GetNamespaces();
  }

  void IElementValue.WriteValue(XmlWriter writer, WebDAVContext context)
  {
    writer.WriteStartElement(Names.activelock);
    writer.WriteStartElement(Names.lockscope);
    writer.WriteEmptyElement(LockType.Exclusive ? Names.exclusive : Names.shared);
    writer.WriteEndElement(); // lockscope
    writer.WriteStartElement(Names.locktype);
    writer.WriteEmptyElement(LockType.Type);
    writer.WriteEndElement(); // locktype
    writer.WriteElementString(Names.depth, Recursive ? "infinity" : "0");
    if(_owner != null) writer.WriteNode(_owner.CreateNavigator(), false);
    if(ExpirationTime.HasValue)
    {
      double dsecs = (ExpirationTime.Value - DateTime.UtcNow).TotalSeconds;
      uint secs = dsecs < 0 ? 0 : dsecs > uint.MaxValue ? uint.MaxValue : (uint)dsecs;
      writer.WriteElementString(Names.timeout, "Second-" + secs.ToInvariantString());
    }
    else
    {
      writer.WriteElementString(Names.timeout, "Infinite");
    }
    writer.WriteStartElement(Names.locktoken);
    writer.WriteElementString(Names.href, LockToken);
    writer.WriteEndElement(); // locktoken
    writer.WriteStartElement(Names.lockroot);
    writer.WriteElementString(Names.href, LockPath);
    writer.WriteEndElement(); // lockroot
  }
  #endregion

  XmlElement _owner;
}
#endregion

#region LockRemoval
/// <summary>Determines how locks are removed when calling <see cref="ILockManager.RemoveLocks"/>.</summary>
public enum LockRemoval
{
  /// <summary>The locks directly applied to the given lock path are removed, but no locks on descendant resources are removed.</summary>
  Nonrecursive,
  /// <summary>The locks applied to the given lock path are removed along with all descendant locks.</summary>
  Recursive,
  /// <summary>The locks applied to the given lock path are removed only if there are no descendant locks.</summary>
  RequireEmpty
}
#endregion

#region LockType
/// <summary>Represents a type and scope of a lock. This object is used with the <c>DAV:supportedlock</c> property.</summary>
public class LockType : IElementValue
{
  /// <summary>Initializes a new <see cref="LockType"/> given the type and scope of the lock.</summary>
  /// <param name="type">The type of the lock. This determines the operations protected by the lock.</param>
  /// <param name="exclusive">The scope of the lock, i.e. whether it's shared or exclusive.</param>
  /// <remarks>The only lock type defined in the WebDAV standard is <c>DAV:write</c>, which protects resources against being changed. This
  /// library provides built-in support for write locks. You can define additional lock types if you take care to implement their
  /// semantics. If you wish to create a standard write lock, you should generally use <see cref="ExclusiveWrite"/> or
  /// <see cref="SharedWrite"/> rather than invoking this constructor.
  /// </remarks>
  public LockType(XmlQualifiedName type, bool exclusive)
  {
    if(type == null) throw new ArgumentNullException();
    if(type.IsEmpty) throw new ArgumentException();
    Type      = type;
    Exclusive = exclusive;
  }

  /// <summary>Gets whether the lock is exclusive. An exclusive lock conflicts with all other locks of the same type, while a shared lock
  /// only conflicts with exclusive locks.
  /// </summary>
  public bool Exclusive { get; private set; }

  /// <summary>Gets the type of the lock, which determines the operations protected by the lock.</summary>
  /// <remarks>The only lock type defined in the WebDAV standard is <c>DAV:write</c>, which protects resources against being changed. You
  /// can define additional lock types if you take care to implement their semantics.
  /// </remarks>
  public XmlQualifiedName Type { get; private set; }

  /// <summary>Determines if this lock type conflicts with the given lock type.</summary>
  /// <remarks>In the default implementation, two locks conflict if they have the same type and at least one is exclusive. You should
  /// generally not need to override this method unless you wish to create overlapping lock types. For instance, you might wish to have a
  /// read lock and a write lock, where having a write lock prevents anyone from taking a read lock and vice versa. In that case, you must
  /// use your derived class for both the read and write lock types. In particular, you must be careful to avoid using the built-in
  /// <see cref="ExclusiveWrite"/> or <see cref="SharedWrite"/> values, which are of type <see cref="LockType"/> and would not have your
  /// override.
  /// </remarks>
  public virtual bool ConflictsWith(LockType type)
  {
    if(type == null) throw new ArgumentNullException();
    return (Exclusive || type.Exclusive) && Type.Equals(type.Type);
  }

  /// <inheritdoc/>
  public override bool Equals(object obj)
  {
    return Equals(obj as LockType);
  }

  /// <summary>Determines whether this <see cref="LockType"/> matches the given <see cref="LockType"/>.</summary>
  public bool Equals(LockType other)
  {
    return other != null && Exclusive == other.Exclusive && Type == other.Type;
  }

  /// <inheritdoc/>
  public override int GetHashCode()
  {
    int hash = Type.GetHashCode();
    if(Exclusive) hash ^= 1;
    return hash;
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return Type.ToString() + (Exclusive ? " (exclusive)" : " (shared)");
  }

  /// <summary>A standard exclusive <c>DAV:write</c> lock, as defined in RFC 4918 section 7.</summary>
  public static readonly LockType ExclusiveWrite = new LockType(Names.write, true);

  /// <summary>A standard shared <c>DAV:write</c> lock, as defined in RFC 4918 section 7.</summary>
  public static readonly LockType SharedWrite = new LockType(Names.write, false);

  /// <summary>An <see cref="IEnumerable{T}"/> of <see cref="LockType"/> containing <see cref="ExclusiveWrite"/> and
  /// <see cref="SharedWrite"/>, useful for specifying the value of the <c>DAV:supportedlock</c> property.
  /// </summary>
  public static readonly IEnumerable<LockType> WriteLocks =
    new ReadOnlyListWrapper<LockType>(new LockType[] { ExclusiveWrite, SharedWrite });

  #region IElementValue Members
  IEnumerable<string> IElementValue.GetNamespaces()
  {
    return new string[] { Type.Namespace };
  }

  void IElementValue.WriteValue(XmlWriter writer, WebDAVContext context)
  {
    writer.WriteStartElement(Names.lockentry);
    writer.WriteStartElement(Names.lockscope);
    writer.WriteEmptyElement(Exclusive ? Names.exclusive : Names.shared);
    writer.WriteEndElement(); // lockscope
    writer.WriteStartElement(Names.locktype);
    writer.WriteEmptyElement(Type);
    writer.WriteEndElement(); // locktype
    writer.WriteEndElement(); // lockentry
  }
  #endregion
}
#endregion

#region ILockManager
/// <summary>Represents a container for the resource locks within a WebDAV service.</summary>
public interface ILockManager : IDisposable
{
  /// <include file="documentation.xml" path="/DAV/ILockManager/AddLock/node()" />
  ActiveLock AddLock(string absolutePath, LockType type, bool recursive, uint? timeoutSeconds, XmlElement ownerData);
  /// <include file="documentation.xml" path="/DAV/ILockManager/GetConflictingLocks/node()" />
  IList<ActiveLock> GetConflictingLocks(string absolutePath, LockType type, bool recursive);
  /// <include file="documentation.xml" path="/DAV/ILockManager/GetLock/node()" />
  ActiveLock GetLock(string lockToken, string absolutePath);
  /// <include file="documentation.xml" path="/DAV/ILockManager/GetLocks/node()" />
  IList<ActiveLock> GetLocks(string absolutePath, bool includeInheritedLocks, bool includeDescendantLocks, Predicate<ActiveLock> filter);
  /// <include file="documentation.xml" path="/DAV/ILockManager/RefreshLock/node()" />
  bool RefreshLock(ActiveLock activeLock, uint? timeoutSeconds);
  /// <include file="documentation.xml" path="/DAV/ILockManager/RemoveLock/node()" />
  bool RemoveLock(ActiveLock activeLock);
  /// <include file="documentation.xml" path="/DAV/ILockManager/RemoveLocks/node()" />
  bool RemoveLocks(string absolutePath, LockRemoval removal);
}
#endregion

#region LockManager
/// <summary>Provides a base class for implementing lock managers. The <see cref="LockManager"/> class maintains an in-memory
/// representation of the locks for a WebDAV service. Derived classes are responsible for saving and loading the locks to and from
/// persistent storage.
/// </summary>
public abstract class LockManager : ILockManager
{
  /// <summary>Initializes a new, empty <see cref="LockManager"/>.</summary>
  protected LockManager() { }

  /// <summary>Initializes a new <see cref="LockManager"/> containing the given locks.</summary>
  protected LockManager(IEnumerable<ActiveLock> initialLocks)
  {
    if(initialLocks == null) throw new ArgumentNullException();

    foreach(ActiveLock lockObject in initialLocks)
    {
      locksByToken.Add(lockObject.LockToken, lockObject);
      locksByUrl.Add(lockObject.LockPath, lockObject);
    }
  }

  /// <summary>Finalizes the <see cref="LockManager"/> by calling <see cref="Dispose(bool)"/>.</summary>
  ~LockManager()
  {
    Dispose(true);
    disposed = true;
  }

  /// <summary>Gets or sets the default lock timeout, in seconds, used when the client does not specify a lock timeout. Zero indicates
  /// that the lock should not time out. The default value is zero.
  /// </summary>
  public uint DefaultTimeout { get; set; }

  /// <include file="documentation.xml" path="/DAV/ILockManager/AddLock/node()" />
  public ActiveLock AddLock(string absolutePath, LockType type, bool recursive, uint? timeoutSeconds, XmlElement ownerData)
  {
    ValidateAbsolutePath(absolutePath);
    if(type == null) throw new ArgumentNullException();
    AssertNotDisposed();
    lock(this)
    {
      foreach(ActiveLock activeLock in GetLocks(absolutePath, true, recursive, null))
      {
        if(activeLock.LockType.ConflictsWith(type)) throw new LockConflictException(activeLock);
      }

      absolutePath = DAVUtility.RemoveTrailingSlash(absolutePath);
      ActiveLock newLock = new ActiveLock(absolutePath, absolutePath, MakeLockToken(), type, recursive, DateTime.UtcNow,
                                          timeoutSeconds ?? DefaultTimeout, ownerData);
      locksByToken.Add(newLock.LockToken, newLock);
      locksByUrl.Add(absolutePath, newLock);
      OnLockAdded(newLock);
      return newLock;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    GC.SuppressFinalize(this);
    Dispose(false);
    disposed = true;
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/GetConflictingLocks/node()" />
  public IList<ActiveLock> GetConflictingLocks(string absolutePath, LockType type, bool recursive)
  {
    ValidateAbsolutePath(absolutePath);
    if(type == null) throw new ArgumentNullException();
    AssertNotDisposed();
    List<ActiveLock> conflictingLocks = new List<ActiveLock>();
    lock(this)
    {
      foreach(ActiveLock activeLock in GetLocks(absolutePath, true, recursive, null))
      {
        if(activeLock.LockType.ConflictsWith(type)) conflictingLocks.Add(activeLock);
      }
    }
    return conflictingLocks;
  }

  /// <summary>Returns the lock having the given lock token, or null if no such lock exists in the lock manager.</summary>
  public ActiveLock GetLock(string lockToken)
  {
    return GetLock(lockToken, null);
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/GetLock/node()" />
  public ActiveLock GetLock(string lockToken, string absolutePath)
  {
    AssertNotDisposed();
    ActiveLock activeLock;
    lock(this) activeLock = GetLockByToken(lockToken);
    return activeLock == null || absolutePath == null || activeLock.IsInScope(absolutePath) ? activeLock : null;
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/GetLocks/node()" />
  public IList<ActiveLock> GetLocks(string absolutePath, bool includeInheritedLocks, bool includeDescendantLocks,
                                    Predicate<ActiveLock> filter)
  {
    ValidateAbsolutePath(absolutePath);
    AssertNotDisposed();
    List<ActiveLock> activeLocks = new List<ActiveLock>();
    string path = DAVUtility.RemoveTrailingSlash(absolutePath);
    lock(this)
    {
      List<ActiveLock> deadLocks = null;
      DateTime now = DateTime.UtcNow;
      bool isAncestor = false;
      while(true)
      {
        List<ActiveLock> locks;
        if(locksByUrl.TryGetValue(path, out locks))
        {
          foreach(ActiveLock lockObject in locks)
          {
            if(lockObject.Timeout == 0 || lockObject.ExpirationTime.Value > now)
            {
              if((!isAncestor || lockObject.Recursive) && (filter == null || filter(lockObject))) activeLocks.Add(lockObject);
            }
            else
            {
              if(deadLocks == null) deadLocks = new List<ActiveLock>();
              deadLocks.Add(lockObject);
            }
          }
        }

        if(!includeInheritedLocks) break;
        int lastSlash = path.LastIndexOf('/');
        if(lastSlash == 0) break;
        path = path.Substring(0, lastSlash);
        isAncestor = true;
      }

      if(includeDescendantLocks)
      {
        path = DAVUtility.WithTrailingSlash(absolutePath);
        foreach(KeyValuePair<string,List<ActiveLock>> pair in locksByUrl)
        {
          if(pair.Key.StartsWith(path, StringComparison.Ordinal)) AddLocks(pair.Value, activeLocks, ref deadLocks, now, filter);
        }
      }

      RemoveDeadLocks(deadLocks);
    }

    return activeLocks;
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/RefreshLock/node()" />
  public bool RefreshLock(ActiveLock activeLock, uint? timeoutSeconds)
  {
    if(activeLock == null) throw new ArgumentNullException();
    AssertNotDisposed();

    lock(this)
    {
      if(activeLock == GetLockByToken(activeLock.LockToken))
      {
        timeoutSeconds = GetRefreshTimeout(activeLock, timeoutSeconds);
        if(timeoutSeconds.HasValue)
        {
          activeLock.Refresh(timeoutSeconds.Value);
          OnLockUpdated(activeLock);
          return true;
        }
      }
    }

    return false;
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/RemoveLock/node()" />
  public bool RemoveLock(ActiveLock activeLock)
  {
    if(activeLock == null) throw new ArgumentNullException();
    AssertNotDisposed();

    lock(this)
    {
      if(activeLock == GetLockByToken(activeLock.LockToken))
      {
        RemoveLockCore(activeLock);
        return true;
      }
    }

    return false;
  }

  /// <include file="documentation.xml" path="/DAV/ILockManager/RemoveLocks/node()" />
  public bool RemoveLocks(string absolutePath, LockRemoval removal)
  {
    lock(this)
    {
      absolutePath = DAVUtility.RemoveTrailingSlash(absolutePath);
      IList<ActiveLock> deadLocks = GetLocks(absolutePath, false, removal != LockRemoval.Nonrecursive, null);
      if(removal == LockRemoval.RequireEmpty)
      {
        foreach(ActiveLock lockObject in deadLocks)
        {
          if(lockObject.LockPath.Length > absolutePath.Length) return false;
        }
      }

      RemoveDeadLocks(deadLocks);
      return true;
    }
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/Dispose/node()" />
  protected abstract void Dispose(bool finalizing);

  /// <summary>Returns a list of all locks currently existing in the lock manager. Note that this method may call
  /// <see cref="OnLockRemoved"/> if an expired lock is discovered and removed by this method, so <see cref="OnLockRemoved"/> must be
  /// reentrant if it calls this method.
  /// </summary>
  protected IList<ActiveLock> GetAllLocks()
  {
    lock(this)
    {
      List<ActiveLock> activeLocks = new List<ActiveLock>(locksByToken.Count), deadLocks = null;
      AddLocks(locksByToken.Values, activeLocks, ref deadLocks, DateTime.UtcNow, null);
      RemoveDeadLocks(deadLocks);
      return activeLocks;
    }
  }

  /// <summary>Returns an appropriate refresh timeout for the given lock, in seconds, or zero if the lock should not time out, or null if
  /// the lock timeout should not be refreshed.
  /// </summary>
  /// <param name="lockObject">The lock that the user wants to refresh.</param>
  /// <param name="requestedTimeout">The requested timeout, in seconds, or zero if the lock should not time out, or null if no specific
  /// timeout is requested.
  /// </param>
  /// <remarks>The default implementation returns <paramref name="requestedTimeout"/> if it's not null, and
  /// <see cref="ActiveLock.Timeout"/> otherwise.
  /// </remarks>
  protected virtual uint? GetRefreshTimeout(ActiveLock lockObject, uint? requestedTimeout)
  {
    if(lockObject == null) throw new ArgumentNullException();
    return requestedTimeout ?? lockObject.Timeout;
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockAdded/node()" />
  protected abstract void OnLockAdded(ActiveLock newLock);
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockRemoved/node()" />
  protected abstract void OnLockRemoved(ActiveLock lockObject);
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockUpdated/node()" />
  protected abstract void OnLockUpdated(ActiveLock lockObject);

  internal static void ValidateAbsolutePath(string absolutePath)
  {
    if(absolutePath == null) throw new ArgumentNullException();
    if(absolutePath.Length == 0 || absolutePath[0] != '/') throw new ArgumentException("The given path is not absolute.");
  }

  /// <summary>Throws an exception if the lock manager has been disposed.</summary>
  void AssertNotDisposed()
  {
    if(disposed) throw new ObjectDisposedException(ToString());
  }

  /// <summary>Returns the lock with the given token, assuming it's not expired. If the lock is expired, it will be removed. The lock
  /// manager must be locked when this method is called.
  /// </summary>
  ActiveLock GetLockByToken(string lockToken)
  {
    ActiveLock activeLock;
    if(locksByToken.TryGetValue(lockToken, out activeLock) &&
       activeLock.Timeout != 0 && activeLock.ExpirationTime.Value <= DateTime.UtcNow)
    {
      RemoveLockCore(activeLock);
      activeLock = null;
    }
    return activeLock;
  }

  void RemoveDeadLocks(IList<ActiveLock> deadLocks)
  {
    if(deadLocks != null)
    {
      foreach(ActiveLock deadLock in deadLocks) RemoveLockCore(deadLock);
    }
  }

  /// <summary>Removes a lock from the lock manager and calls <see cref="OnLockRemoved"/>. The lock must exist in the lock manager, and
  /// the lock manager must be locked.
  /// </summary>
  void RemoveLockCore(ActiveLock activeLock)
  {
    locksByToken.Remove(activeLock.LockToken);
    locksByUrl.Remove(activeLock.LockPath, activeLock);
    OnLockRemoved(activeLock);
  }

  readonly Dictionary<string, ActiveLock> locksByToken = new Dictionary<string, ActiveLock>();
  readonly MultiValuedDictionary<string, ActiveLock> locksByUrl = new MultiValuedDictionary<string, ActiveLock>();
  bool disposed;

  static void AddLocks(IEnumerable<ActiveLock> locks, List<ActiveLock> activeLocks, ref List<ActiveLock> deadLocks, DateTime now,
                       Predicate<ActiveLock> filter)
  {
    foreach(ActiveLock lockObject in locks)
    {
      if(lockObject.Timeout == 0 || lockObject.ExpirationTime.Value > now)
      {
        if(filter == null || filter(lockObject)) activeLocks.Add(lockObject);
      }
      else
      {
        if(deadLocks == null) deadLocks = new List<ActiveLock>();
        deadLocks.Add(lockObject);
      }
    }
  }

  static string MakeLockToken()
  {
    return "urn:uuid:" + Guid.NewGuid().ToString("D");
  }
}
#endregion

#region MemoryLockManager
/// <summary>Implements a <see cref="LockManager"/> that only maintains locks in memory. All locks will be lost when the WebDAV server
/// terminates or is restarted.
/// </summary>
public class MemoryLockManager : LockManager
{
  /// <include file="documentation.xml" path="/DAV/LockManager/Dispose/node()" />
  protected override void Dispose(bool finalizing) { }
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockAdded/node()" />
  protected override void OnLockAdded(ActiveLock newLock) { }
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockRemoved/node()" />
  protected override void OnLockRemoved(ActiveLock lockObject) { }
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockUpdated/node()" />
  protected override void OnLockUpdated(ActiveLock lockObject) { }
}
#endregion

} // namespace HiA.WebDAV.Server
