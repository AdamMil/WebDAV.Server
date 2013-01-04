using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Xml;
using HiA.IO;
using HiA.WebDAV.Server.Configuration;

// TODO: add some helper methods somewhere to encapsulate common lock-related actions like getting the locks on a resource (e.g. the
// request.Context.LockManager.GetLocks(request.Context.ServiceRoot + CanonicalPath, true, false, null) bit)
// TODO: we may want FileLockManager to just clobber the file if it's invalid, possibly with an option to enable this. that way, a
// corrupt lock file (e.g. due to a sudden shutdown) won't stop the server from working

namespace HiA.WebDAV.Server
{

#region ActiveLock
/// <summary>Describes an active lock on a resource. This object is used with the <c>DAV:lockdiscovery</c> property.</summary>
public sealed class ActiveLock : IElementValue
{
  /// <summary>Initializes a new <see cref="ActiveLock"/> object.</summary>
  /// <param name="absoluteLockRoot">The absolute, canonical path to the resource to which the lock is directly applied.</param>
  /// <param name="lockToken">A URI string uniquely identifying the lock. The string should be unique among all locks on all resources
  /// on all servers in the world. (A good practice is to use the urn:uuid URI scheme defined in RFC 4122.)
  /// </param>
  /// <param name="lockType">A <see cref="Type"/> object representing the type and scope of the lock.</param>
  /// <param name="recursive">Whether the lock is recursive.</param>
  /// <param name="creationTime">The time when the lock was originally created. If the time has a <see cref="DateTimeKind"/> of
  /// <see cref="DateTimeKind.Local"/>, it will be converted to UTC. Otherwise it is assumed to be in UTC already.
  /// </param>
  /// <param name="timeoutSeconds">The number of seconds after <paramref name="creationTime"/> before the lock expires, or 0 if the lock
  /// does not expire.
  /// </param>
  /// <param name="ownerData">Arbitrary data about the owner of the lock submitted by the client.</param>
  /// <exception cref="ArgumentException">Thrown if <paramref name="lockToken"/> is empty, or if <paramref name="ownerData"/> is specified
  /// but is not a <c>DAV:owner</c> element.
  /// </exception>
  public ActiveLock(string absoluteLockRoot, string lockToken, LockType lockType, bool recursive,
                    DateTime creationTime, uint timeoutSeconds, XmlElement ownerData)
  {
    if(absoluteLockRoot == null || lockToken == null || lockType == null) throw new ArgumentNullException();
    if(string.IsNullOrEmpty(lockToken)) throw new ArgumentException("A lock token is required.");

    absoluteLockRoot = DAVUtility.RemoveTrailingSlash(absoluteLockRoot);
    LockManager.ValidateAbsolutePath(absoluteLockRoot);

    Path  = absoluteLockRoot;
    Token = lockToken;
    Type  = lockType;
    Recursive = recursive;

    if(ownerData != null)
    {
      if(!ownerData.HasName(DAVNames.owner)) throw new ArgumentException("The owner data must be a DAV:owner element.");
      _owner = ownerData.Extract();
    }

    CreationTime = creationTime.Kind == DateTimeKind.Local ?
      creationTime.ToUniversalTime() : DateTime.SpecifyKind(creationTime, DateTimeKind.Utc);

    Timeout = timeoutSeconds;
    if(timeoutSeconds != 0) ExpirationTime = CreationTime.AddSeconds(timeoutSeconds);
  }

  /// <summary>Initializes a new <see cref="ActiveLock"/> from the given <see cref="BinaryReader"/>. The data is expected to have been
  /// written by <see cref="Save"/>.
  /// </summary>
  public ActiveLock(BinaryReader reader)
  {
    if(reader == null) throw new ArgumentNullException();
    int version = reader.ReadByte();
    if(version != 0) throw new InvalidDataException("Unsupported lock version: " + version.ToInvariantString());

    CreationTime = reader.ReadDateTime();
    if(reader.ReadBoolean()) ExpirationTime = reader.ReadDateTime();
    Path      = reader.ReadString();
    Recursive = reader.ReadBoolean();
    Timeout   = reader.ReadEncodedUInt32();
    Token     = reader.ReadString();
    Type      = LockType.Load(reader);

    if(reader.ReadBoolean())
    {
      XmlDocument xml = new XmlDocument();
      xml.LoadXml(reader.ReadString());
      _owner = xml.DocumentElement;
    }
  }

  /// <summary>Gets the time when the lock was originally created, in UTC.</summary>
  public DateTime CreationTime { get; private set; }

  /// <summary>Gets the time when the lock is scheduled to time out, in UTC, or null if the lock should not time out.</summary>
  public DateTime? ExpirationTime { get; private set; }

  /// <summary>Gets the absolute path to the resource to which the lock is directly applied (i.e. the lock root). The path is canonical
  /// except that it does not have a trailing slash. It is safe to pass this path to <see cref="ILockManager"/> methods.
  /// </summary>
  public string Path { get; private set; }

  /// <summary>Gets whether the lock is recursive.</summary>
  public bool Recursive { get; private set; }

  /// <summary>Gets the number of seconds most recently used to compute the <see cref="ExpirationTime"/>, or zero if the lock should not
  /// expire.
  /// </summary>
  public uint Timeout { get; private set; }

  /// <summary>Gets a URI string that uniquely identifies this lock.</summary>
  public string Token { get; private set; }

  /// <summary>Gets the type and scope of the lock.</summary>
  public LockType Type { get; private set; }

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
    return Path.OrdinalEquals(absolutePath) ||
           Recursive && absolutePath.StartsWith(Path, StringComparison.Ordinal) && absolutePath[Path.Length] == '/';
  }

  /// <summary>Saves the lock to a <see cref="BinaryWriter"/>. This method is generally used in the implementation of
  /// <see cref="ILockManager"/> classes, to save locks to persistent storage.
  /// </summary>
  public void Save(BinaryWriter writer)
  {
    if(writer == null) throw new ArgumentNullException();
    writer.Write((byte)0);
    writer.Write(CreationTime);
    writer.Write(ExpirationTime.HasValue);
    if(ExpirationTime.HasValue) writer.Write(ExpirationTime.Value);
    writer.Write(Path);
    writer.Write(Recursive);
    writer.WriteEncoded(Timeout);
    writer.Write(Token);
    Type.Save(writer);

    writer.Write(_owner != null);
    if(_owner != null)
    {
      StringWriter sw = new StringWriter(CultureInfo.InvariantCulture);
      _owner.OwnerDocument.Save(sw);
      writer.Write(sw.ToString());
    }
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return "Lock " + Token + " of type " + Type.ToString() + " on " + Path + (Recursive ? " (recursive)" : null);
  }

  /// <summary>Refreshes the lock timeout by computing a new <see cref="ExpirationTime"/> <paramref name="timeoutSeconds"/> seconds in the
  /// future, or setting <see cref="ExpirationTime"/> to null if <paramref name="timeoutSeconds"/> is zero.
  /// </summary>
  internal void Refresh(uint timeoutSeconds)
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
    return ((IElementValue)Type).GetNamespaces();
  }

  void IElementValue.WriteValue(XmlWriter writer, WebDAVContext context)
  {
    if(writer == null) throw new ArgumentNullException();
    writer.WriteStartElement(DAVNames.activelock);
    writer.WriteStartElement(DAVNames.lockscope);
    writer.WriteEmptyElement(Type.Exclusive ? DAVNames.exclusive : DAVNames.shared);
    writer.WriteEndElement(); // lockscope
    writer.WriteStartElement(DAVNames.locktype);
    writer.WriteEmptyElement(Type.Type);
    writer.WriteEndElement(); // locktype
    writer.WriteElementString(DAVNames.depth, Recursive ? "infinity" : "0");
    if(_owner != null) writer.WriteNode(_owner.CreateNavigator(), false);
    if(ExpirationTime.HasValue)
    {
      double dsecs = (ExpirationTime.Value - DateTime.UtcNow).TotalSeconds;
      uint secs = dsecs < 0 ? 0 : dsecs > uint.MaxValue ? uint.MaxValue : (uint)Math.Round(dsecs);
      writer.WriteElementString(DAVNames.timeout, "Second-" + secs.ToInvariantString());
    }
    else
    {
      writer.WriteElementString(DAVNames.timeout, "Infinite");
    }
    writer.WriteStartElement(DAVNames.locktoken);
    writer.WriteElementString(DAVNames.href, Token);
    writer.WriteEndElement(); // locktoken
    writer.WriteStartElement(DAVNames.lockroot);
    writer.WriteElementString(DAVNames.href, Path);
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
public sealed class LockType : IElementValue
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
  /// <remarks>Two locks conflict if they have the same type and at least one is exclusive.</remarks>
  public bool ConflictsWith(LockType type)
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
    return this == other || other != null && Exclusive == other.Exclusive && Type == other.Type;
  }

  /// <inheritdoc/>
  public override int GetHashCode()
  {
    int hash = Type.GetHashCode();
    return Exclusive ? hash ^ 1 : hash;
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return Type.ToString() + (Exclusive ? " (exclusive)" : " (shared)");
  }

  /// <summary>A standard exclusive <c>DAV:write</c> lock, as defined in RFC 4918 section 7.</summary>
  public static readonly LockType ExclusiveWrite = new LockType(DAVNames.write, true);

  /// <summary>A standard shared <c>DAV:write</c> lock, as defined in RFC 4918 section 7.</summary>
  public static readonly LockType SharedWrite = new LockType(DAVNames.write, false);

  /// <summary>An <see cref="IEnumerable{T}"/> of <see cref="LockType"/> containing <see cref="ExclusiveWrite"/> and
  /// <see cref="SharedWrite"/>, useful for specifying the value of the <c>DAV:supportedlock</c> property or for passing to
  /// <see cref="LockRequest.ProcessStandardRequest(IEnumerable{LockType},bool)"/>.
  /// </summary>
  public static readonly IEnumerable<LockType> WriteLocks =
    new ReadOnlyListWrapper<LockType>(new LockType[] { ExclusiveWrite, SharedWrite });

  internal void Save(BinaryWriter writer)
  {
    int type = Equals(ExclusiveWrite) ? 1 : Equals(SharedWrite) ? 2 : 0;
    writer.Write((byte)type);
    if(type == 0)
    {
      writer.Write(Type.Name);
      writer.Write(Type.Namespace);
      writer.Write(Exclusive);
    }
  }

  internal static LockType Load(BinaryReader reader)
  {
    switch(reader.ReadByte())
    {
      case 0: return new LockType(new XmlQualifiedName(reader.ReadString(), reader.ReadString()), reader.ReadBoolean());
      case 1: return ExclusiveWrite;
      case 2: return SharedWrite;
      default: throw new InvalidDataException();
    }
  }

  #region IElementValue Members
  IEnumerable<string> IElementValue.GetNamespaces()
  {
    return new string[] { Type.Namespace };
  }

  void IElementValue.WriteValue(XmlWriter writer, WebDAVContext context)
  {
    if(writer == null) throw new ArgumentNullException();
    writer.WriteStartElement(DAVNames.lockentry);
    writer.WriteStartElement(DAVNames.lockscope);
    writer.WriteEmptyElement(Exclusive ? DAVNames.exclusive : DAVNames.shared);
    writer.WriteEndElement(); // lockscope
    writer.WriteStartElement(DAVNames.locktype);
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
  /// <summary>Initializes a new <see cref="LockManager"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>The <see cref="FileLockManager"/> supports the following parameters:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>defaultTimeout</term>
  ///     <description>xs:unsignedInt</description>
  ///     <description>The default timeout of a lock, in seconds, when the client does not specify a timeout. A value of zero indicates
  ///       that the lock does not time out. The default is zero.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>maximumLocks</term>
  ///     <description>xs:unsignedInt</description>
  ///     <description>The maximum total number of locks that can be active at any one time. A value of zero indicates no limit. The
  ///       default is zero.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>maximumLocksPerUrl</term>
  ///     <description>xs:unsignedInt</description>
  ///     <description>The maximum number of locks that can be active on a single URL. A value of zero indicates no limit.
  ///       The default is zero.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>maximumTimeout</term>
  ///     <description>xs:unsignedInt</description>
  ///     <description>The maximum lock timeout, in seconds. A value of zero indicates that there is no maximum. The default is zero.</description>
  ///   </item>
  /// </list>
  /// </remarks>
  protected LockManager(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();

    DefaultTimeout     = ParseParameter(parameters, "defaultTimeout", 0);
    MaximumLocks       = ParseParameter(parameters, "maximumLocks", 0);
    MaximumLocksPerUrl = ParseParameter(parameters, "maximumLocksPerUrl", 0);
    MaximumTimeout     = ParseParameter(parameters, "maximumTimeout", 0);
  }

  /// <summary>Finalizes the <see cref="LockManager"/> by calling <see cref="Dispose(bool)"/>.</summary>
  ~LockManager()
  {
    Dispose(false);
    disposed = true;
  }

  /// <summary>Gets or sets the default lock timeout, in seconds, used when the client does not specify a lock timeout. Zero indicates
  /// that the lock should not time out. The default value is zero.
  /// </summary>
  public uint DefaultTimeout { get; set; }

  /// <summary>Gets or sets the maximum total number of locks that can exist at any one time. A value of zero indicates that there is no
  /// maximum. The default is zero.
  /// </summary>
  public uint MaximumLocks { get; set; }

  /// <summary>Gets or sets the maximum number of locks per URL. A value of zero indicates that there is no maximum. The default is zero.</summary>
  public uint MaximumLocksPerUrl { get; set; }

  /// <summary>Gets or sets the maximum lock timeout, in seconds. A value of zero indicates that there is no maximum. The default is zero.</summary>
  public uint MaximumTimeout { get; set; }

  /// <include file="documentation.xml" path="/DAV/ILockManager/AddLock/node()" />
  public ActiveLock AddLock(string absolutePath, LockType type, bool recursive, uint? timeoutSeconds, XmlElement ownerData)
  {
    ValidateAbsolutePath(absolutePath);
    if(type == null) throw new ArgumentNullException();
    AssertNotDisposed();
    absolutePath = DAVUtility.RemoveTrailingSlash(absolutePath);
    lock(this)
    {
      foreach(ActiveLock activeLock in GetLocks(absolutePath, true, recursive, null))
      {
        if(activeLock.Type.ConflictsWith(type)) throw new LockConflictException(activeLock);
      }

      // check for lock limits after calling GetLocks() because GetLocks() may remove stale locks, freeing up some slots
      List<ActiveLock> locksOnUrl;
      if(MaximumLocks != 0 && locksByToken.Count >= MaximumLocks ||
         MaximumLocksPerUrl != 0 && locksByUrl.TryGetValue(absolutePath, out locksOnUrl) && locksOnUrl.Count >= MaximumLocksPerUrl)
      {
        throw new LockLimitReachedException();
      }

      ActiveLock newLock = new ActiveLock(absolutePath, MakeLockToken(), type, recursive, DateTime.UtcNow,
                                          ClipTimeout(timeoutSeconds ?? DefaultTimeout), ownerData);
      locksByToken.Add(newLock.Token, newLock);
      locksByUrl.Add(newLock.Path, newLock);
      OnLockAdded(newLock);
      return newLock;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
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
        if(activeLock.Type.ConflictsWith(type)) conflictingLocks.Add(activeLock);
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
      if(activeLock == GetLockByToken(activeLock.Token))
      {
        timeoutSeconds = GetRefreshTimeout(activeLock, timeoutSeconds);
        if(timeoutSeconds.HasValue)
        {
          activeLock.Refresh(ClipTimeout(timeoutSeconds.Value));
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
      if(activeLock == GetLockByToken(activeLock.Token))
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
    absolutePath = DAVUtility.RemoveTrailingSlash(absolutePath);
    lock(this)
    {
      IList<ActiveLock> deadLocks = GetLocks(absolutePath, false, removal != LockRemoval.Nonrecursive, null);
      if(removal == LockRemoval.RequireEmpty)
      {
        foreach(ActiveLock lockObject in deadLocks)
        {
          if(lockObject.Path.Length > absolutePath.Length) return false;
        }
      }

      RemoveDeadLocks(deadLocks);
      return true;
    }
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/Dispose/node()" />
  protected virtual void Dispose(bool manualDispose) { }

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

  /// <summary>Loads the given locks into the <see cref="LockManager"/>, clearing all existing locks first. The locks themselves are not
  /// validated (e.g. checked for conflicts), and are assumed to have come from the <see cref="GetAllLocks"/> method.
  /// </summary>
  protected void LoadLocks(IEnumerable<ActiveLock> locks)
  {
    if(locks == null) throw new ArgumentNullException();
    AssertNotDisposed();
    lock(this)
    {
      locksByToken.Clear();
      locksByUrl.Clear();

      foreach(ActiveLock lockObject in locks)
      {
        if(lockObject == null) throw new ArgumentException("A lock was null.");
        locksByToken.Add(lockObject.Token, lockObject);
        locksByUrl.Add(lockObject.Path, lockObject);
      }
    }
  }

  /// <summary>Returns an appropriate refresh timeout for the given lock, in seconds, or zero if the lock should not time out, or null if
  /// the lock timeout should not be refreshed.
  /// </summary>
  /// <param name="lockObject">The lock that the user wants to refresh.</param>
  /// <param name="requestedTimeout">The requested timeout, in seconds, or zero if the lock should not time out, or null if no specific
  /// timeout is requested.
  /// </param>
  /// <remarks>The default implementation returns <paramref name="requestedTimeout"/> if it's not null and
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

  internal static uint ParseParameter(ParameterCollection parameters, string paramName, uint defaultValue)
  {
    return ParseParameter(parameters, paramName, defaultValue, 0, 0);
  }

  internal static uint ParseParameter(ParameterCollection parameters, string paramName, uint defaultValue, uint minValue, uint maxValue)
  {
    uint value = defaultValue;
    string str = parameters.TryGetValue(paramName);
    if(!string.IsNullOrEmpty(str))
    {
      if(!InvariantCultureUtility.TryParse(str, out value))
      {
        throw new ArgumentException("The " + paramName + " value \"" + str + "\" is not a valid integer or is out of range.");
      }
      else if(value < minValue || maxValue != 0 && value > maxValue)
      {
        throw new ArgumentException("The " + paramName + " value \"" + str + "\" is out of range. It must be at least " +
                                    minValue.ToInvariantString() +
                                    (maxValue == 0 ? null : " and at most " + maxValue.ToInvariantString()) + ".");
      }
    }
    return value;
  }

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

  /// <summary>Clips the given timeout value to be no longer than <see cref="MaximumTimeout"/>.</summary>
  uint ClipTimeout(uint timeout)
  {
    if(MaximumTimeout != 0 && (timeout == 0 || timeout > MaximumTimeout)) timeout = MaximumTimeout;
    return timeout;
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
    locksByToken.Remove(activeLock.Token);
    locksByUrl.Remove(activeLock.Path, activeLock);
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
    return "urn:uuid:" + Guid.NewGuid().ToString("D"); // use the UUID URN described in RFC 4122
  }
}
#endregion

#region FileLockManager
/// <summary>Implements a <see cref="LockManager"/> that stores locks in a file on disk.</summary>
public class FileLockManager : LockManager
{
  /// <summary>Initializes a new <see cref="FileLockManager"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>The <see cref="FileLockManager"/> supports the following parameters:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>lockFile</term>
  ///     <description>xs:string</description>
  ///     <description>The full path to the file in which the locks will be saved.</description>
  ///   </item>
  ///   <item>
  ///     <term>revertToSelf</term>
  ///     <description>xs:bool</description>
  ///     <description>Determines whether the lock manager will revert to the process identity before opening the file on disk. This allows
  ///     the file to be opened with the IIS process account, which is usually more privileged. The default is true.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>writeInterval</term>
  ///     <description>xs:positiveInteger</description>
  ///     <description>The number of seconds to wait before flushing pending changes to disk. The default is 60 and the maximum is 2147483.</description>
  ///   </item>
  /// </list>
  /// </remarks>
  public FileLockManager(ParameterCollection parameters) : base(parameters)
  {
    string value = parameters.TryGetValue("revertToSelf");
    bool revertToSelf = string.IsNullOrEmpty(value) || XmlConvert.ToBoolean(value);

    writeInterval = (int)ParseParameter(parameters, "writeInterval", 60, 1, int.MaxValue/1000) * 1000;

    value = parameters.TryGetValue("lockFile");
    if(string.IsNullOrEmpty(value)) throw new ArgumentException("The lockFile attribute is required for the FileLockManager.");

    FileStream file = null;
    Action openFile = delegate { file = new FileStream(value, FileMode.OpenOrCreate, FileAccess.ReadWrite); };
    if(revertToSelf) Impersonation.RunWithImpersonation(Impersonation.RevertToSelf, false, openFile);
    else ElevatePrivileges(openFile);
    if(file == null) throw new ArgumentException("Unable to open file: " + value);
    this.file = file;
    LoadLocks(value);

    timer = new Timer(OnTimerTick);
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/Dispose/node()" />
  protected override void Dispose(bool manualDispose)
  {
    if(!disposed)
    {
      Utility.Dispose(timer);
      try
      {
        if(file != null)
        {
          Monitor.Enter(file);
          try { WriteChanges(); }
          catch(ObjectDisposedException) { } // if the app domain was unloaded, the file may have been closed already...
          file.Close();
        }
        disposed = true;
      }
      finally
      {
        if(file != null) Monitor.Exit(file);
      }
    }

    base.Dispose(manualDispose);
  }

  /// <summary>Called to execute the given action, such as opening the lock file, in an elevated privilege context when the
  /// <c>revertToSelf</c> parameter is false.
  /// </summary>
  /// <remarks>The default implementation simply executes the action without altering privileges in any way.</remarks>
  protected virtual void ElevatePrivileges(Action action)
  {
    if(action == null) throw new ArgumentNullException();
    action();
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockAdded/node()" />
  protected override void OnLockAdded(ActiveLock newLock)
  {
    OnChanged();
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockRemoved/node()" />
  protected override void OnLockRemoved(ActiveLock lockObject)
  {
    OnChanged();
  }

  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockUpdated/node()" />
  protected override void OnLockUpdated(ActiveLock lockObject)
  {
    OnChanged();
  }

  /// <summary>A magic number identifying the file as a lock file.</summary>
  const uint MagicNumber = 0xd6cf9eb7;

  void LoadLocks(string fileName)
  {
    if(file.Length != 0)
    {
      try
      {
        if(file.Length > 5)
        {
          byte[] magic = file.Read(5);
          if(magic[0] == unchecked((byte)MagicNumber)     && magic[1] == unchecked((byte)(MagicNumber>>8)) &&
             magic[2] == unchecked((byte)MagicNumber>>16) && magic[3] == unchecked((byte)(MagicNumber>>24)))
          {
            int version = magic[4];
            if(version == 0)
            {
              using(BinaryReader reader = new BinaryReader(new GZipStream(file, CompressionMode.Decompress, true)))
              {
                ActiveLock[] locks = new ActiveLock[reader.ReadInt32()];
                for(int i=0; i<locks.Length; i++) locks[i] = new ActiveLock(reader);
                LoadLocks(locks);
                return;
              }
            }
          }
        }
      }
      catch(IOException) { }
      catch(OutOfMemoryException) { }

      throw new InvalidDataException(fileName + " is not a valid lock file. If you want to use this file name, remove the file first.");
    }
  }

  void OnChanged()
  {
    // OnChanged() is always called from within a lock, so we don't need any locking semantics here
    if(!pendingWrite)
    {
      timer.Change(writeInterval, Timeout.Infinite);
      pendingWrite = true;
    }
  }

  void OnTimerTick(object state)
  {
    WriteChanges();
  }

  void WriteChanges()
  {
    if(pendingWrite && !disposed)
    {
      lock(file)
      {
        IList<ActiveLock> locks = null;
        lock(this)
        {
          if(pendingWrite && !disposed)
          {
            locks = GetAllLocks();
            pendingWrite = false;
          }
        }

        if(locks != null)
        {
          file.Position = 0;
          byte[] magic = new byte[]
          {
            unchecked((byte)MagicNumber),     unchecked((byte)(MagicNumber>>8)),
            unchecked((byte)MagicNumber>>16), unchecked((byte)(MagicNumber>>24)), 0
          };
          file.Write(magic);
          using(BinaryWriter writer = new BinaryWriter(new GZipStream(file, CompressionMode.Compress, true)))
          {
            writer.Write(locks.Count);
            foreach(ActiveLock lockObject in locks) lockObject.Save(writer);
          }
          if(file.Position < file.Length) file.SetLength(file.Position);
          file.Flush();
        }
      }
    }
  }

  readonly FileStream file;
  readonly Timer timer;
  readonly int writeInterval;
  bool disposed, pendingWrite;
}
#endregion

#region MemoryLockManager
/// <summary>Implements a <see cref="LockManager"/> that only maintains locks in memory. All locks will be lost when the WebDAV server
/// terminates or is restarted.
/// </summary>
public class MemoryLockManager : LockManager
{
  /// <summary>Initializes a new <see cref="MemoryLockManager"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  public MemoryLockManager(ParameterCollection parameters) : base(parameters) { }

  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockAdded/node()" />
  protected override void OnLockAdded(ActiveLock newLock) { }
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockRemoved/node()" />
  protected override void OnLockRemoved(ActiveLock lockObject) { }
  /// <include file="documentation.xml" path="/DAV/LockManager/OnLockUpdated/node()" />
  protected override void OnLockUpdated(ActiveLock lockObject) { }
}
#endregion

} // namespace HiA.WebDAV.Server
