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
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Xml;
using AdamMil.Collections;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

// TODO: authorization against the request URI and/or handling of access exceptions in resolving/loading the request resource
// TODO: more cleanup and refactoring...
// TODO: some type of permissions model that allows us to separate GET access from LOCK access. we should probably tie LOCK access to
// write access... (people can layer this on top using an authorization filter, but it may be nice to have it built-in)

namespace AdamMil.WebDAV.Server
{

#region FileSystemService
/// <summary>Implements a <see cref="WebDAVService"/> that serves files from the filesystem.</summary>
public class FileSystemService : WebDAVService
{
  /// <summary>Initializes a new <see cref="FileSystemService"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>The <see cref="FileSystemService"/> supports the following parameters:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>allowInfinitePropFind</term>
  ///     <description>xs:boolean</description>
  ///     <description>Specifies whether the service allows infinite-depth PROPFIND queries. This may be disabled if clients can't be
  ///       trusted to use them responsibly. The default is true.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>caseSensitive</term>
  ///     <description>xs:boolean</description>
  ///     <description>Determines whether URL resolution will perform case-sensitive matches against the file system. The default is false.</description>
  ///   </item>
  ///   <item>
  ///     <term>fsRoot</term>
  ///     <description>xs:string</description>
  ///     <description>Specifies the root within the filesystem where files will be served. If null or empty, all files on all drives will
  ///     be served.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>writable</term>
  ///     <description>xs:boolean</description>
  ///     <description>Determines whether the WebDAV service allows the creation, deletion, and modification of files and directories. The
  ///       default is false.
  ///     </description>
  ///   </item>
  /// </list>
  /// </remarks>
  public FileSystemService(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();

    string value = parameters.TryGetValue("caseSensitive");
    CaseSensitive = !string.IsNullOrEmpty(value) && XmlConvert.ToBoolean(value);
    comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    value = parameters.TryGetValue("allowInfinitePropFind");
    InfinitePropFind = string.IsNullOrEmpty(value) || XmlConvert.ToBoolean(value); 

    value = parameters.TryGetValue("writable");
    IsReadOnly = string.IsNullOrEmpty(value) || !XmlConvert.ToBoolean(value);

    RootPath = parameters.TryGetValue("fsRoot");
    if(RootPath == null && HasUnifiedFileSystem) RootPath = "/"; // for operating systems with unified file systems, just serve the root
    if(RootPath != null) RootPath = RootPath.Length == 0 ? null : PathUtility.NormalizePath(RootPath);
  }

  /// <summary>Gets whether URL resolution will perform case-sensitive matches against the file system.</summary>
  public bool CaseSensitive { get; private set; }

  /// <summary>Gets whether infinite-depth PROPFIND queries are allowed.</summary>
  public bool InfinitePropFind { get; private set; }

  /// <summary>Gets whether the service allows the creation, deletion, and modification of file system resources.</summary>
  public bool IsReadOnly { get; private set; }

  /// <summary>Gets the root path on the filesystem from which files will be served. If null, the root path will provide access to all
  /// files on all drives.
  /// </summary>
  public string RootPath { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CopyResource/node()" />
  public override ConditionCode CopyResource<T>(CopyOrMoveRequest request, string destinationPath, IStandardResource<T> sourceResource)
  {
    if(IsReadOnly) return ConditionCodes.Forbidden;

    string diskPath = GetDiskPath(destinationPath);
    if(diskPath == null) return ConditionCodes.Forbidden; // the path doesn't actually exist on disk and so can't be modified

    try
    {
      // if the resource already exists, we have to delete it or return an error
      bool overwrote = false;
      if(request.Overwrite)
      {
        if(File.Exists(diskPath)) { File.Delete(diskPath); overwrote = true; }
        else if(Directory.Exists(diskPath)) { DeleteDirectory(diskPath); overwrote = true; }
      }
      else if(File.Exists(diskPath) || Directory.Exists(diskPath))
      {
        return ConditionCodes.PreconditionFailed; // return 412 Precondition Failed when Overwrite is false but the destination exists
      }

      // now the resource shouldn't exist (although race conditions are possible)
      if(sourceResource.IsCollection) // if we should create a directory...
      {
        // Dirctory.CreateDirectory creates the ancestors if they don't exist, but we want to return 409 Conflict in that case
        if(!Directory.Exists(Path.GetDirectoryName(diskPath))) return ConditionCodes.Conflict;
        Directory.CreateDirectory(diskPath);
      }
      else // otherwise, we should create a file...
      {
        using(FileStream dest = new FileStream(diskPath, FileMode.CreateNew, FileAccess.Write))
        using(Stream source = sourceResource.OpenStream(request.Context))
        {
          if(source != null) // if the resource has an entity body, copy it. (otherwise, we'll get an empty file, which is appropriate)
          {
            EntityMetadata metadata = sourceResource.GetEntityMetadata(false);
            if(metadata.Length.HasValue) dest.SetLength(metadata.Length.Value);
            source.CopyTo(dest);
            // consider the stream length to take precedence over metadata regarding length
            if(metadata.Length.HasValue && dest.Position < metadata.Length.Value) dest.SetLength(dest.Position);
          }
        }
      }

      // canonicalize the path for use by the lock manager and property store
      destinationPath = NormalizePath(destinationPath);

      // preserve creation and modification times when moving objects
      if(request.IsMove)
      {
        FileSystemInfo info = sourceResource.IsCollection ? (FileSystemInfo)new DirectoryInfo(diskPath) : new FileInfo(diskPath);

        IDictionary<XmlQualifiedName, object> liveProperties = sourceResource.GetLiveProperties(request.Context);
        DateTime time = GetTimePropertyValue(liveProperties.TryGetValue(DAVNames.creationdate));
        if(time != default(DateTime))
        {
          try { info.CreationTimeUtc = time; }
          catch(PlatformNotSupportedException) { }
        }

        EntityMetadata metadata = sourceResource.GetEntityMetadata(false);
        time = metadata.LastModifiedTime ?? GetTimePropertyValue(liveProperties.TryGetValue(DAVNames.getlastmodified));
        if(time != default(DateTime))
        {
          try { info.LastWriteTimeUtc = time; }
          catch(PlatformNotSupportedException) { }
        }
      }

      request.PostProcessCopy(sourceResource.CanonicalPath, destinationPath);

      return overwrote ? ConditionCodes.NoContent : ConditionCodes.Created; // return 204 or 201 as RFC 4918 says we should
    }
    catch(DirectoryNotFoundException)
    {
      return ConditionCodes.Conflict; // return 409 Conflict when the parent directory of the destination doesn't exist
    }
    catch(Exception ex)
    {
      return GetStatusFromException(ex);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/CreateAndLock/node()" />
  public override void CreateAndLock(LockRequest request)
  {
    if(IsReadOnly) base.CreateAndLock(request);
    else request.ProcessStandardRequest(LockType.WriteLocks, () => CreateNewFile(request.Context, null));
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/GetCanonicalPath/node()" />
  public override string GetCanonicalPath(WebDAVContext context, string relativePath)
  {
    if(context == null || relativePath == null) throw new ArgumentNullException();
    return NormalizePath(relativePath);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/MakeCollection/node()" />
  public override void MakeCollection(MkColRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly)
    {
      base.MakeCollection(request); // call the default implementation, which denies the request
    }
    else
    {
      request.ProcessStandardRequest(NormalizePath(request.Context.RequestPath), () =>
      {
        try
        {
          string path = DAVUtility.RemoveTrailingSlash(request.Context.RequestPath);
          if(string.IsNullOrEmpty(path)) // if the user is requesting to create the root directory...
          {
            Directory.CreateDirectory(RootPath); // RootPath should be non-null and nonexistent. otherwise, the URL would have been mapped
          }
          else
          {
            int lastSlash = path.LastIndexOf('/');
            string parent = lastSlash == -1 ? "" : path.Substring(0, lastSlash);

            IWebDAVResource resource = ResolveResource(request.Context, parent);
            DirectoryResource directory = resource as DirectoryResource;
            if(resource == null) return ConditionCodes.Conflict; // nonexistent parent gets 409 Conflict as per RFC 4918 sec. 9.3
            else if(directory == null) return ConditionCodes.Forbidden; // non-directory parents don't support MKCOL
            else directory.Info.CreateSubdirectory(HttpUtility.UrlDecode(lastSlash == -1 ? path : path.Substring(lastSlash+1)));
          }
          return null;
        }
        catch(ArgumentException) // thrown when there are illegal characters in the path
        {
          return ConditionCodes.BadPathCharacters;
        }
        catch(Exception ex)
        {
          return GetStatusFromException(ex);
        }
      });
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    if(!IsReadOnly)
    {
      if(request.IsServerQuery ||
         request.Context.RequestPath.Length != 0 && request.Context.RequestPath[request.Context.RequestPath.Length-1] != '/')
      {
        request.AllowedMethods.Add(DAVMethods.Put);
        request.SupportsLocking = request.Context.LockManager != null;
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Put/node()" />
  public override void Put(PutRequest request)
  {
    if(IsReadOnly)
    {
      base.Put(request); // call the default implementation, which denies the request
    }
    else
    {
      request.Status = CreateNewFile(request.Context, stream =>
      {
        string path = NormalizePath(request.Context.RequestPath);
        request.ProcessStandardRequest(path, delegate(out Stream s) { s = stream; return null; });
        if(request.Status == null || request.Status.IsSuccessful) // if the request was successfully executed,
        {                                                         // write the ETag and Last-Modified headers
          request.Context.Response.Headers[DAVHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
          stream.Close(); // close the file to ensure the last modified time gets updated
          request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(File.GetLastWriteTimeUtc(path));
        }
        return request.Status;
      });
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public override IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath)
  {
    if(context == null || resourcePath == null) throw new ArgumentNullException();

    bool hadTrailingSlash = resourcePath.EndsWith('/');
    resourcePath = DAVUtility.RemoveTrailingSlash(resourcePath);

    // disallow .. in the request path. this should have already been stripped out, but there are usually ways to slip it past ASP.NET...
    if(resourcePath.StartsWith("../", StringComparison.Ordinal) || resourcePath.EndsWith("/..", StringComparison.Ordinal) ||
       resourcePath.OrdinalEquals("..") || resourcePath.Contains("/../", StringComparison.Ordinal))
    {
      throw Exceptions.BadRequest("It is illegal to use .. to access a parent directory in the request URL.");
    }

    // don't allow slash characters embedded in file or directory names...
    if(resourcePath.Contains("%2F", StringComparison.OrdinalIgnoreCase)) return null;

    // normalize file names so that locking, etc. works properly regardless of case
    resourcePath = NormalizePath(resourcePath);

    if(RootPath != null) // if we're serving a place on the filesystem...
    {
      return ResolveResource(RootPath, resourcePath, "", hadTrailingSlash); // then resolve the request normally
    }
    else // otherwise, we have a virtual root that encompasses the system's drives, so we'll need to resolve it specially
    {
      // resolve the first component to a drive name. if there is no first component, then they're referencing the root itself
      if(string.IsNullOrEmpty(resourcePath)) return new FileSystemRootResource(CaseSensitive, InfinitePropFind);

      int slash = resourcePath.IndexOf('/');
      string driveName = slash == -1 ? resourcePath : resourcePath.Substring(0, slash);
      resourcePath = slash == -1 ? null : resourcePath.Substring(slash+1);

      DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(driveName, GetDriveName(d), comparison));
      if(drive == null) return null; // if there's no such drive, then we couldn't find the resource

      string canonicalDrivePath = DAVUtility.CanonicalSegmentEncode(GetDriveName(drive)) + "/";
      // if the root of the drive was requested, then we've got all the information needed to resolve the resource
      if(string.IsNullOrEmpty(resourcePath))
      {
        return new DirectoryResource(drive.RootDirectory, canonicalDrivePath, IsReadOnly, CaseSensitive, InfinitePropFind);
      }
      else
      {
        return ResolveResource(drive.RootDirectory.FullName, resourcePath, canonicalDrivePath, hadTrailingSlash); // resolve it normally
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Unlock/node()" />
  public override void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly || request.Context.LockManager == null) base.Unlock(request); // deny the request if locking is not supported
    else request.ProcessStandardRequest(NormalizePath(request.Context.RequestPath)); // otherwise, remove the dangling lock
  }

  readonly StringComparison comparison;

  internal static bool HasUnifiedFileSystem
  {
    get { return Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX; }
  }

  /// <summary>Computes an <see cref="EntityTag"/> for the given file.</summary>
  internal static EntityTag ComputeEntityTag(FileInfo info)
  {
    using(FileStream stream = info.OpenRead()) return DAVUtility.ComputeEntityTag(stream);
  }

  internal static void DeleteDirectory(string path)
  {
    try { Directory.Delete(path, true); }
    catch
    {
      // sometimes the Delete method throws an exception when it shouldn't
      int lastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
      if(lastError == 18 || lastError == 145) // NO_MORE_FILES or DIR_NOT_EMPTY
      {
        // if we get DIR_NOT_EMPTY, another process (like Explorer) may have a handle open, so give it a bit of time to close it.
        // on the other hand, if we get NO_MORE_FILES, that seems like a bug in Directory.Delete(). it also goes away after a bit
        System.Threading.Thread.Sleep(50);
        Directory.Delete(path, true);
      }
      else throw;
    }
  }

  /// <summary>Gets the path on disk to the resource named by the given path, even if the resource doesn't exist.</summary>
  internal string GetDiskPath(string resourcePath)
  {
    resourcePath = DAVUtility.RemoveTrailingSlash(resourcePath);

    // disallow .. in the request path
    if(resourcePath.StartsWith("../", StringComparison.Ordinal) || resourcePath.EndsWith("/..", StringComparison.Ordinal) ||
       resourcePath.OrdinalEquals("..") || resourcePath.Contains("/../", StringComparison.Ordinal))
    {
      throw Exceptions.BadRequest("It is illegal to use .. to access a parent directory in the request URL.");
    }

    // don't allow slash characters embedded in file or directory names...
    if(resourcePath.Contains("%2F", StringComparison.OrdinalIgnoreCase)) return null;

    try
    {
      if(RootPath != null) // if we're serving a place on the filesystem...
      {
        return Path.Combine(RootPath, HttpUtility.UrlDecode(resourcePath));
      }
      else // otherwise, we have a virtual root that encompasses the system's drives, so we'll need to resolve it specially
      {
        // resolve the first component to a drive name. if there is no first component, then they're referencing the root itself
        if(string.IsNullOrEmpty(resourcePath)) return null;

        int slash = resourcePath.IndexOf('/');
        string driveName = slash == -1 ? resourcePath : resourcePath.Substring(0, slash);
        resourcePath = slash == -1 ? null : resourcePath.Substring(slash+1);

        DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(driveName, GetDriveName(d), comparison));
        if(drive == null) return null; // if there's no such drive, then we couldn't find the resource

        return Path.Combine(drive.RootDirectory.FullName, HttpUtility.UrlDecode(resourcePath));
      }
    }
    catch(ArgumentException) // thrown when illegal characters are in the path
    {
      return null;
    }
  }

  internal static string NormalizePath(string path, bool caseSensitive)
  {
    return caseSensitive ? path : path.ToUpperInvariant();
  }

  /// <summary>Returns a name for a drive. For instance, for <c>C:\</c>, it will return "C".</summary>
  /// <param name="drive"></param>
  /// <returns></returns>
  internal static string GetDriveName(DriveInfo drive)
  {
    // the drive name on Windows has the format C:\, so we'll look for the volume separator (:) and grab what's before it.
    // if there's no volume separator, return the whole thing minus any trailing slash. (this may be incorrect, but I don't have any such
    // systems to test with.) if there's no name, return a dummy name. (this also won't happen on Windows.)
    int sep = drive.Name.IndexOf(Path.VolumeSeparatorChar);
    string name = sep == -1 ? drive.Name.TrimEnd(Path.DirectorySeparatorChar) : drive.Name.Substring(0, sep);
    if(name.Length == 0) name = "fsroot";
    return name;
  }

  internal static ConditionCode GetStatusFromException(Exception ex)
  {
    return ex is UnauthorizedAccessException || ex is PathTooLongException ? ConditionCodes.Forbidden :
           ex is FileNotFoundException || ex is DirectoryNotFoundException ? ConditionCodes.NotFound :
           new ConditionCode(HttpStatusCode.InternalServerError, WebDAVModule.FilterErrorMessage(ex.Message));
  }

  ConditionCode CreateNewFile(WebDAVContext context, Func<Stream,ConditionCode> processRequest)
  {
    string path = context.RequestPath;
    int lastSlash = path.LastIndexOf('/');
    string fileName = HttpUtility.UrlDecode(lastSlash == -1 ? path : path.Substring(lastSlash+1));
    if(fileName.Length == 0) // if an attempt was made to LOCK a path with a trailing slash...
    {
      return ConditionCodes.Forbidden; // it's not clear what the client means, so deny the request
    }
    else
    {
      string parent = lastSlash == -1 ? "" : path.Substring(0, lastSlash);
      IWebDAVResource resource = ResolveResource(context, parent);
      DirectoryResource directory = resource as DirectoryResource;
      if(resource == null) // if the parent doesn't exist...
      {
        return ConditionCodes.Conflict; // nonexistent parents cause a 409 Conflict response as per RFC 4918 sec. 9.7
      }
      else if(directory == null) // if the parent is not a directory...
      {
        return ConditionCodes.Forbidden; // non-directory parents don't support creating children
      }
      else // the parent is a directory
      {
        try
        {
          path = Path.Combine(directory.Info.FullName, fileName);
          ConditionCode status;
          using(FileStream stream = File.Open(path, FileMode.CreateNew, FileAccess.ReadWrite))
          {
            status = processRequest != null ? processRequest(stream) : null;
          }

          if(status != null && !status.IsSuccessful)
          {
            try { File.Delete(path); }
            catch { }
          }

          return status;
        }
        catch(ArgumentException) // thrown when there are illegal characters in the path
        {
          return ConditionCodes.BadPathCharacters;
        }
      }
    }
  }

  string NormalizePath(string path)
  {
    return NormalizePath(path, CaseSensitive);
  }

  /// <summary>Performs the normal resolution algorithm for a request.</summary>
  /// <param name="fsRoot">A location on the filesystem from where to begin searching.</param>
  /// <param name="resourcePath">The relative request path, with no trailing slash.</param>
  /// <param name="davRoot">The canonical relative URL corresponding to <paramref name="fsRoot"/>.</param>
  /// <param name="hadTrailingSlash">Whether <paramref name="resourcePath"/> originally had a trailing slash.</param>
  FileSystemResource ResolveResource(string fsRoot, string resourcePath, string davRoot, bool hadTrailingSlash)
  {
    // turn C: into C:/, since C: refers to the current directory on the C drive
    if(!HasUnifiedFileSystem && fsRoot.EndsWith(Path.VolumeSeparatorChar)) fsRoot += new string(Path.DirectorySeparatorChar, 1);

    try
    {
      string combinedPath = Path.Combine(fsRoot, HttpUtility.UrlDecode(resourcePath)); // add the request path to the file system root

      if(!hadTrailingSlash) // i decree that you can't access a file through a path with a trailing slash
      {
        FileInfo file = new FileInfo(combinedPath); // if the combined path names a file, return a file resource
        if(file.Exists)
        {
          return new FileResource(file, davRoot + GetCanonicalPath(fsRoot, file.FullName, false), IsReadOnly, CaseSensitive);
        }
      }

      DirectoryInfo directory = new DirectoryInfo(combinedPath); // otherwise, if it names a directory, return a directory resource
      if(directory.Exists)
      {
        return new DirectoryResource(directory, davRoot + GetCanonicalPath(fsRoot, directory.FullName, true),
                                     IsReadOnly, CaseSensitive, InfinitePropFind);
      }
    }
    catch(ArgumentException) // thrown when the path is illegal
    {
    }

    return null;
  }

  /// <summary>Returns the canonical relative path to a resource, given the full path to it and the full path to the file system root.</summary>
  /// <param name="fsRoot">A location on the filesystem.</param>
  /// <param name="fullPath">A location on the filesystem equal to or beneath <paramref name="fsRoot"/>.</param>
  /// <param name="isDirectory">Whether the resource is a directory (and should have a trailing slash).</param>
  static string GetCanonicalPath(string fsRoot, string fullPath, bool isDirectory)
  {
    // because both filesystem paths are full paths, we can simply chop the front off of fullPath to get the relative portion
    string relativePath = fullPath.Substring(fsRoot.Length);
    // ensure that the relative path is relative by removing any leading slash
    if(relativePath.Length != 0 && (relativePath[0] == Path.DirectorySeparatorChar || relativePath[0] == Path.AltDirectorySeparatorChar))
    {
      relativePath = relativePath.Substring(1);
    }
    // if the relative path is a directory, add a trailing slash (if that wouldn't make it into an absolute path)
    if(isDirectory && relativePath.Length != 0)
    {
      char c = relativePath[relativePath.Length-1];
      if(c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar) relativePath += "/";
    }

    // convert OS path separator characters into URL path segment separators
    if(Path.DirectorySeparatorChar != '/') relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
    if(Path.AltDirectorySeparatorChar != '/') relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');

    // encode special characters and return it
    return DAVUtility.CanonicalPathEncode(relativePath);
  }

  static DateTime GetTimePropertyValue(object value)
  {
    Func<object> getter = value as Func<object>;
    if(getter != null) value = getter();
    if(value is DateTime) return (DateTime)value;
    else if(value is DateTimeOffset) return ((DateTimeOffset)value).UtcDateTime;
    else return default(DateTime);
  }
}
#endregion

#region FileSystemResource
/// <summary>Provides a base class for file system resources, intended to be used with <see cref="FileSystemService"/>.</summary>
public abstract class FileSystemResource : WebDAVResource, IStandardResource<FileSystemResource>
{
  /// <summary>Initializes a new <see cref="FileSystemResource"/>.</summary>
  /// <param name="canonicalPath">The canonical relative path to the resource.</param>
  /// <param name="isReadOnly">Determines whether the resource is read-only. If true, it will deny all requests to change the resource.</param>
  /// <param name="caseSensitive">This should be true if the file system is case-sensitive and false if not.</param>
  /// <param name="allowInfinitePropFind">Determines whether the resource allows infinite-depth PROPFIND queries.</param>
  protected FileSystemResource(string canonicalPath, bool isReadOnly, bool caseSensitive, bool allowInfinitePropFind)
  {
    if(canonicalPath == null) throw new ArgumentNullException();
    _canonicalPath   = canonicalPath;
    IsReadOnly       = isReadOnly;
    CaseSensitive    = caseSensitive;
    InfinitePropFind = allowInfinitePropFind;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  public override string CanonicalPath
  {
    get { return _canonicalPath; }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  public override void CopyOrMove(CopyOrMoveRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    // have special handling if both services are FileSystemServices, both for efficiency and to protect the filesystem
    FileSystemService srcService = request.Context.Service as FileSystemService;
    FileSystemService destService = request.Destination.Service as FileSystemService;
    if(IsReadOnly && request.IsMove || destService != null && destService.IsReadOnly)
    {
      request.Status = ConditionCodes.Forbidden;
    }
    else
    {
      Func<CopyOrMoveRequest,string,FileSystemResource,ConditionCode> createDest = null;
      if(srcService != null && destService != null) // if both services are file system services...
      {
        // check to make sure we're not attempting to copy/move to an ancestor or descendant of the source
        ConditionCode pathStatus = null;
        string sourcePath = srcService.GetDiskPath(request.Context.RequestPath);
        string destPath = destService.GetDiskPath(request.Destination.RequestPath);
        if(sourcePath == null || destPath == null) // if either path doesn't refer to a real location on disk...
        {
          pathStatus = ConditionCodes.Forbidden; // then we can't process the request
        }
        else if(!Directory.Exists(Path.GetDirectoryName(destPath))) // if the parent of the destination doesn't exist...
        {
          pathStatus = ConditionCodes.Conflict; // we aren't allowed to create it automatically
        }
        else
        {
          string slashChar = new string(Path.DirectorySeparatorChar, 1);
          sourcePath = PathUtility.NormalizePath(sourcePath) + slashChar;
          destPath   = PathUtility.NormalizePath(destPath)   + slashChar;
          if(sourcePath.OrdinalEquals(destPath)) // if the source and destination are the same...
          {
            // it's a no-op if Overwrite is true. (RFC 4918 suggests 403 Forbidden, but i don't see any harm in allowing and ignoring it)
            pathStatus = request.Overwrite ? ConditionCodes.NoContent : ConditionCodes.PreconditionFailed;
          }
          else if(sourcePath.StartsWith(destPath, StringComparison.Ordinal) || destPath.StartsWith(sourcePath, StringComparison.Ordinal))
          {
            pathStatus = ConditionCodes.BadCopyOrMovePath; // otherwise, if it's to an ancestor or descendant, we don't support that
          }
        }

        // if we have a return code from the path check, return it or a precondition status, which may take precedence
        if(pathStatus != null)
        {
          ConditionCode precondition = request.CheckPreconditions(null);
          request.Status = precondition != null && (precondition.IsError || pathStatus.IsSuccessful) ? precondition : pathStatus;
          return;
        }

        // the paths were okay, so create a custom createDest implementation that uses native file copying/moving APIs
        createDest = (req, path, sourceFile) =>
        {
          string diskPath = destService.GetDiskPath(path);
          if(diskPath == null) return ConditionCodes.Forbidden;

          try
          {
            // delete any existing item
            bool overwrote = false;
            if(Directory.Exists(diskPath))
            {
              if(req.Overwrite) FileSystemService.DeleteDirectory(diskPath);
              else return ConditionCodes.PreconditionFailed;
              overwrote = true;
            }
            else if(File.Exists(diskPath))
            {
              if(req.Overwrite) File.Delete(diskPath);
              else return ConditionCodes.PreconditionFailed;
              overwrote = true;
            }

            // copy or move the item non-recursively
            if(req.IsCopy) sourceFile.CopyTo(diskPath);
            else sourceFile.MoveTo(diskPath);

            // canonicalize the path for use by the lock manager and property store
            path = FileSystemService.NormalizePath(path, CaseSensitive);

            // remove any locks that existed at the destination
            if(req.Destination.LockManager != null) req.Destination.LockManager.RemoveLocks(path, LockRemoval.Nonrecursive);

            // and copy the dead properties
            if(req.Context.PropertyStore != null && req.Destination.PropertyStore != null)
            {
              IEnumerable<XmlProperty> properties = req.Context.PropertyStore.GetProperties(sourceFile.CanonicalPath).Values;
              req.Destination.PropertyStore.SetProperties(path, properties, true);
            };

            return overwrote ? ConditionCodes.NoContent : ConditionCodes.Created;
          }
          catch(DirectoryNotFoundException) { return ConditionCodes.Conflict; }
          catch(Exception ex) { return FileSystemService.GetStatusFromException(ex); }
        };
      }

      Func<FileSystemResource,ConditionCode> deleteFile = res =>
      {
        try
        {
          if(res.IsCollection || createDest == null) res.Delete(); // files will have already been moved if we use our custom createDest
          return ConditionCodes.NoContent;
        }
        catch(Exception ex)
        {
          return FileSystemService.GetStatusFromException(ex);
        }
      };

      request.ProcessStandardRequest(this, deleteFile, createDest, null);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  public override void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly)
    {
      base.Delete(request);
    }
    else
    {
      request.ProcessStandardRequest(this, resource =>
      {
        try { resource.Delete(); }
        catch(Exception ex) { return FileSystemService.GetStatusFromException(ex); }
        return null;
      });
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(request.Depth == Depth.SelfAndDescendants && !InfinitePropFind)
    {
      request.Status = ConditionCodes.PropFindFiniteDepth;
    }
    else
    {
      try { request.ProcessStandardRequest(this, resource => resource.TryGetLiveProperties(request)); }
      catch(Exception ex) { request.Status = FileSystemService.GetStatusFromException(ex); }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  public override void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly) base.Unlock(request); // use the base implementation if we don't support locking
    else request.ProcessStandardRequest();
  }

  /// <summary>Gets whether the resource is located on a case-sensitive filesystem.</summary>
  protected bool CaseSensitive { get; private set; }

  /// <summary>Gets whether infinite-depth PROPFIND queries are allowed.</summary>
  protected bool InfinitePropFind { get; private set; }

  /// <summary>Gets whether the resource is read-only. If true, the resource must deny all requests to change it.</summary>
  protected bool IsReadOnly { get; private set; }

  /// <summary>Gets whether the resource is a collection resource (i.e. whether it contains child resources).</summary>
  /// <remarks>The default implementation returns false.</remarks>
  internal virtual bool IsCollection
  {
    get { return false; }
  }

  /// <summary>Non-recursively copies the current item to the given path.</summary>
  internal abstract void CopyTo(string diskPath);

  /// <summary>Deletes the resource.</summary>
  internal abstract void Delete();

  /// <summary>Enumerates the children of this resource. This function may return null if <see cref="IsCollection"/> is false.</summary>
  /// <remarks>The default implementation returns null.</remarks>
  internal virtual IEnumerable<FileSystemResource> GetChildResources()
  {
    return null;
  }

  /// <summary>Returns a dictionary containing the live properties of the resource.</summary>
  internal abstract IDictionary<XmlQualifiedName, object> GetLiveProperties(PropFindRequest request);

  /// <summary>Returns the member name of the resource, which is the minimally escaped path segment for the resource.</summary>
  internal abstract string GetMemberName();

  /// <summary>Non-recursively moves the current item to the given path. If the item has children, an identical item but without children
  /// should be created at the destination.
  /// </summary>
  internal abstract void MoveTo(string diskPath);

  /// <summary>Returns a stream for the resource with read access, or null if the resource has no stream. The stream will be closed by the
  /// caller.
  /// </summary>
  /// <remarks>The default implementation returns null.</remarks>
  internal virtual Stream OpenStream()
  {
    return null;
  }

  internal IEnumerable<FileSystemResource> TryGetChildResources()
  {
    try { return GetChildResources(); }
    catch(DirectoryNotFoundException) { }
    catch(FileNotFoundException) { }
    catch(UnauthorizedAccessException) { }
    return null;
  }

  internal IDictionary<XmlQualifiedName, object> TryGetLiveProperties(PropFindRequest request)
  {
    try { return GetLiveProperties(request); }
    catch(DirectoryNotFoundException) { }
    catch(FileNotFoundException) { }
    catch(UnauthorizedAccessException) { }
    return new Dictionary<XmlQualifiedName, object>();
  }

  #region ISourceResource<T> Members
  bool IStandardResource<FileSystemResource>.IsCollection
  {
    get { return IsCollection; }
  }

  IEnumerable<FileSystemResource> IStandardResource<FileSystemResource>.GetChildren(WebDAVContext context)
  {
    return TryGetChildResources();
  }

  IDictionary<XmlQualifiedName, object> IStandardResource<FileSystemResource>.GetLiveProperties(WebDAVContext context)
  {
    return TryGetLiveProperties(null);
  }

  string IStandardResource<FileSystemResource>.GetMemberName(WebDAVContext context)
  {
    return GetMemberName();
  }

  Stream IStandardResource<FileSystemResource>.OpenStream(WebDAVContext context)
  {
    return OpenStream();
  }
  #endregion

  readonly string _canonicalPath;
}
#endregion

#region FileSystemResource<T>
/// <summary>Provides a base class for file system resources, intended to be used with <see cref="FileSystemService"/>.</summary>
public abstract class FileSystemResource<T> : FileSystemResource where T : FileSystemInfo
{
  /// <summary>Initializes a new <see cref="FileSystemResource{T}"/>.</summary>
  /// <param name="info">The <see cref="FileSystemInfo"/> describing the resource on the filesystem.</param>
  /// <param name="canonicalPath">The canonical relative path to the resource.</param>
  /// <param name="isReadOnly">Determines whether the resource is read-only. If true, it will deny all requests to change the resource.</param>
  /// <param name="caseSensitive">This should be true if the file system is case-sensitive and false if not.</param>
  /// <param name="allowInfinitePropFind">Determines whether the resource allows infinite-depth PROPFIND queries.</param>
  internal FileSystemResource(T info, string canonicalPath, bool isReadOnly, bool caseSensitive, bool allowInfinitePropFind)
    : base(canonicalPath, isReadOnly, caseSensitive, allowInfinitePropFind)
  {
    Info = info;
  }

  /// <summary>Gets the <see cref="FileSystemInfo"/> object describing this resource, or null if the resource is the virtual root of a
  /// non-unified filesystem (e.g. a filesystem with drives, as in Windows).
  /// </summary>
  public T Info { get; private set; }

  /// <summary>Returns a dictionary containing the live properties of the resource.</summary>
  internal override IDictionary<XmlQualifiedName, object> GetLiveProperties(PropFindRequest request)
  {
    FileSystemInfo info = Info;
    Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
    properties.Add(DAVNames.displayname, info.Name);
    properties.Add(DAVNames.creationdate, new DateTimeOffset(info.CreationTime));
    properties.Add(DAVNames.getlastmodified, new DateTimeOffset(info.LastWriteTime));
    if(!IsReadOnly && request != null && request.Context.LockManager != null)
    {
      properties.Add(DAVNames.lockdiscovery, request.MustExcludePropertyValue(DAVNames.lockdiscovery) ?
        null : request.Context.LockManager.GetLocks(CanonicalPath, LockSelection.SelfAndRecursiveAncestors, null));
      properties.Add(DAVNames.supportedlock, LockType.WriteLocks);
    }
    properties.Add(DAVNames.resourcetype, (info.Attributes & FileAttributes.Directory) != 0 ? ResourceType.Collection : null);
    return properties;
  }

  /// <summary>Returns the member name of the resource, which is the canonical path segment for the resource.</summary>
  internal override string GetMemberName()
  {
    return Info == null ? "" : DAVUtility.CanonicalSegmentEncode(Info.Name);
  }
}
#endregion

#region DirectoryResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a directory in the filesystem.</summary>
public class DirectoryResource : FileSystemResource<DirectoryInfo>
{
  /// <summary>Initializes a new <see cref="DirectoryResource"/>.</summary>
  /// <param name="info">A <see cref="DirectoryInfo"/> representing the directory to serve. The directory must have a valid name; it cannot
  /// be a root directory.
  /// </param>
  /// <param name="canonicalPath">The canonical relative path to the resource. See <see cref="IWebDAVResource.CanonicalPath"/> for
  /// details.
  /// </param>
  /// <param name="isReadOnly">Determines whether the directory resource is read-only. If true, it will deny all requests to change it.</param>
  /// <param name="caseSensitive">This should be true if the file system is case-sensitive and false if not.</param>
  /// <param name="allowInfinitePropFind">Determines whether the resource allows infinite-depth PROPFIND queries.</param>
  public DirectoryResource(DirectoryInfo info, string canonicalPath, bool isReadOnly, bool caseSensitive, bool allowInfinitePropFind)
    : base(info, canonicalPath, isReadOnly, caseSensitive, allowInfinitePropFind) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  {
    return new EntityMetadata() { MediaType = "text/html" };
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    // write a basic index.html-like response containing the items in the directory
    FileSystemInfo[] entries = Info.GetFileSystemInfos();
    GetOrHeadRequest.IndexItem[] items = new GetOrHeadRequest.IndexItem[entries.Length];
    for(int i=0; i<items.Length; i++) items[i] = GetIndexItem(entries[i]);
    request.WriteSimpleIndexHtml(items);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  public override void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.ProcessStandardRequest(LockType.WriteLocks, true);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(!IsReadOnly)
    {
      request.AllowedMethods.Add(DAVMethods.Delete); // writable directories can be deleted
      request.SupportsLocking = request.Context.LockManager != null;
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  public override void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.MethodNotAllowed; // can't PUT to a directory
  }

  /// <summary>Gets whether the resource is a collection resource (i.e. whether it contains child resources).</summary>
  internal override bool IsCollection
  {
    get { return true; }
  }

  /// <summary>Non-recursively copies the current item to the given path.</summary>
  internal override void CopyTo(string diskPath)
  {
    Directory.CreateDirectory(diskPath);
  }

  /// <summary>Non-recursively moves the current item to the given path.</summary>
  internal override void MoveTo(string diskPath)
  {
    // "move" the directory by creating a new one and copying over some of the attributes
    DirectoryInfo newDir = new DirectoryInfo(diskPath);
    const FileAttributes AttributesToCopy =
      FileAttributes.Archive | FileAttributes.Compressed | FileAttributes.Hidden | FileAttributes.NotContentIndexed;
    newDir.Create(Info.GetAccessControl()); // TODO: make sure this works with various ACLs and ACL inheritance, etc...
    newDir.Attributes        = Info.Attributes & AttributesToCopy;
    newDir.CreationTimeUtc   = Info.CreationTimeUtc;
    newDir.LastAccessTimeUtc = Info.LastAccessTimeUtc;
    newDir.LastWriteTimeUtc  = Info.LastWriteTimeUtc;
  }

  /// <summary>Recursively deletes the directory.</summary>
  internal override void Delete()
  {
    FileSystemService.DeleteDirectory(Info.FullName);
  }

  /// <summary>Enumerates the children of this resource.</summary>
  internal override IEnumerable<FileSystemResource> GetChildResources()
  {
    return Info.GetFileSystemInfos().Select<FileSystemInfo,FileSystemResource>(info =>
    {
      string canonicalPath =
        CanonicalPath + DAVUtility.CanonicalSegmentEncode(FileSystemService.NormalizePath(info.Name, CaseSensitive));
      FileInfo file = info as FileInfo;
      if(file != null) return new FileResource(file, canonicalPath, IsReadOnly, CaseSensitive);
      DirectoryInfo dir = info as DirectoryInfo;
      if(dir != null) return new DirectoryResource(dir, canonicalPath + "/", IsReadOnly, CaseSensitive, InfinitePropFind);
      return null;
    }).WhereNotNull();
  }

  /// <summary>Returns the member name of the resource, which is the canonical path segment for the resource.</summary>
  internal override string GetMemberName()
  {
    if(!FileSystemService.HasUnifiedFileSystem && Info.Parent == null)
    {
      int colon = Info.Name.IndexOf(Path.VolumeSeparatorChar);
      if(colon != -1) return DAVUtility.CanonicalSegmentEncode(Info.Name.Substring(0, colon));
    }
    return base.GetMemberName();
  }

  GetOrHeadRequest.IndexItem GetIndexItem(FileSystemInfo info)
  {
    string name = info.Name, segment = DAVUtility.CanonicalSegmentEncode(FileSystemService.NormalizePath(name, CaseSensitive));
    bool isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
    GetOrHeadRequest.IndexItem item = new GetOrHeadRequest.IndexItem(segment, name, isDirectory);
    item.LastModificationTime = info.LastWriteTime;
    if(!isDirectory)
    {
      FileInfo fileInfo = (FileInfo)info;
      item.Size = fileInfo.Length;
      string type = fileInfo.Extension;
      if(!string.IsNullOrEmpty(type)) item.Type = type.Substring(1); // remove the leading period from the extension
    }
    return item;
  }
}
#endregion

#region FileResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a file in the filesystem.</summary>
public class FileResource : FileSystemResource<FileInfo>
{
  /// <summary>Initializes a new <see cref="FileResource"/>.</summary>
  /// <param name="info">A <see cref="FileInfo"/> representing the file to serve.</param>
  /// <param name="canonicalPath">The canonical relative path to the resource. See <see cref="IWebDAVResource.CanonicalPath"/> for
  /// details.
  /// </param>
  /// <param name="isReadOnly">Determines whether the file resource is read-only. If true, it will deny all requests to change it.</param>
  /// <param name="caseSensitive">This should be true if the file system is case-sensitive and false if not.</param>
  public FileResource(FileInfo info, string canonicalPath, bool isReadOnly, bool caseSensitive)
    : base(info, canonicalPath, isReadOnly, caseSensitive, true) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  {
    if(metadata == null)
    {
      metadata = new EntityMetadata() { Exists = Info.Exists };
      if(metadata.Exists)
      {
        metadata.LastModifiedTime = Info.LastWriteTimeUtc;
        metadata.Length           = Info.Length;
        metadata.MediaType        = MediaTypes.GuessMediaType(Info.Name);
      }
    }
    if(includeEntityTag && metadata.EntityTag == null)
    {
      try { metadata.EntityTag = FileSystemService.ComputeEntityTag(Info); }
      catch(FileNotFoundException) { metadata = new EntityMetadata() { Exists = false }; }
    }
    return metadata.Clone(); // clone the metadata to prevent callers from mutating cached data
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try
    {
      using(FileStream stream = Info.OpenRead()) request.WriteStandardResponse(stream, GetEntityMetadata(false));
    }
    catch(Exception ex)
    {
      // set the error status if we didn't start writing any file content yet
      if(request.Context.Response.BufferOutput) request.Status = FileSystemService.GetStatusFromException(ex);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  public override void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly) base.Lock(request);
    else request.ProcessStandardRequest(LockType.WriteLocks, false);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(!IsReadOnly) // writable files can be deleted and modified
    {
      request.AllowedMethods.Add(DAVMethods.Delete);
      request.AllowedMethods.Add(DAVMethods.Put);
      request.SupportsLocking = request.Context.LockManager != null;
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try { request.ProcessStandardRequest(GetLiveProperties(request)); }
    catch(Exception ex) { request.Status = FileSystemService.GetStatusFromException(ex); }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  public override void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly)
    {
      base.Put(request); // call the default implementation, which denies the request
    }
    else
    {
      try
      {
        using(FileStream stream = Info.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
          request.ProcessStandardRequest(stream, GetEntityMetadata(false));
          if(request.Status == null || request.Status.IsSuccessful) // if the request was successfully executed...
          {
            // write the ETag and Last-Modified headers
            request.Context.Response.Headers[DAVHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
            stream.Close(); // close the file to ensure the last modified time gets updated
            Info.Refresh(); // refresh the last modified time
            request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(Info.LastWriteTimeUtc);
          }
        }
      }
      catch(Exception ex)
      {
        request.Status = FileSystemService.GetStatusFromException(ex);
      }
    }
  }

  /// <summary>Non-recursively copies the current item to the given path.</summary>
  internal override void CopyTo(string diskPath)
  {
    Info.CopyTo(diskPath, true);
  }

  /// <summary>Deletes the file.</summary>
  internal override void Delete()
  {
    Info.Delete();
  }

  /// <summary>Returns a dictionary containing the live properties of the resource.</summary>
  internal override IDictionary<XmlQualifiedName, object> GetLiveProperties(PropFindRequest request)
  {
    IDictionary<XmlQualifiedName,object> properties = base.GetLiveProperties(request);

    string mediaType = MediaTypes.GuessMediaType(Info.Name);
    if(mediaType != null) properties.Add(DAVNames.getcontenttype, mediaType);

    properties.Add(DAVNames.getcontentlength, (ulong)Info.Length);

    // we want to report the getetag property as being available in the list of all property names, but we don't want to actually compute
    // it unless it's specifically requested
    if(request != null && request.MustIncludeProperty(DAVNames.getetag))
    {
      EntityTag entityTag = null;
      if(!request.NamesOnly)
      {
        FileResource fileResource = this as FileResource;
        if(fileResource != null && fileResource.Info == Info) // use the potentially cached value if the info refers to this resource
        {
          entityTag = GetEntityMetadata(true).EntityTag;
        }
        else
        {
          try { entityTag = FileSystemService.ComputeEntityTag(Info); }
          catch { }
        }
      }

      properties.Add(DAVNames.getetag, entityTag);
    }

    return properties;
  }

  /// <summary>Non-recursively moves the current item to the given path.</summary>
  internal override void MoveTo(string diskPath)
  {
    Info.MoveTo(diskPath);
  }

  /// <summary>Opens the file for reading.</summary>
  internal override Stream OpenStream()
  {
    return Info.OpenRead();
  }

  EntityMetadata metadata;
}
#endregion

#region FileSystemRootResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a virtual directory containing all of the system's active
/// drives.
/// </summary>
public class FileSystemRootResource : FileSystemResource<FileSystemInfo>
{
  /// <summary>Initializes a new <see cref="FileSystemRootResource"/> at the root of the WebDAV service.</summary>
  public FileSystemRootResource(bool caseSensitive, bool allowInfinitePropFind) : this("", caseSensitive, allowInfinitePropFind) { }

  /// <summary>Initializes a new <see cref="FileSystemRootResource"/>.</summary>
  /// <param name="canonicalPath">The canonical relative path to the resource. For a <see cref="FileSystemRootResource"/>, this is
  /// typically an empty string, to place the root resource at the root of the WebDAV service. See
  /// <see cref="IWebDAVResource.CanonicalPath"/> for details.
  /// </param>
  /// <param name="caseSensitive">This should be true if the file system is case-sensitive and false if not.</param>
  /// <param name="allowInfinitePropFind">Determines whether the resource allows infinite-depth PROPFIND queries.</param>
  public FileSystemRootResource(string canonicalPath, bool caseSensitive, bool allowInfinitePropFind)
    : base(null, canonicalPath, true, caseSensitive, allowInfinitePropFind) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  public override void CopyOrMove(CopyOrMoveRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.BadCopyOrMovePath; // the root cannot be copied or moved anywhere
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  public override void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.Forbidden;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  {
    return new EntityMetadata() { MediaType = "text/html" };
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.WriteSimpleIndexHtml(DriveInfo.GetDrives().Where(d => d.IsReady).Select(d =>
    {
      string name = FileSystemService.GetDriveName(d);
      return new GetOrHeadRequest.IndexItem(DAVUtility.CanonicalSegmentEncode(name), name, true);
    }));
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  public override void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.ProcessStandardRequest(LockType.WriteLocks, true);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.SupportsLocking = !IsReadOnly && request.Context.LockManager != null;
  }

  /// <summary>Gets whether the resource is a collection resource (i.e. whether it contains child resources).</summary>
  internal override bool IsCollection
  {
    get { return true; }
  }

  /// <summary>Non-recursively copies the current item to the given path.</summary>
  internal override void CopyTo(string diskPath)
  {
    throw new NotSupportedException();
  }

  /// <summary>Deletes the resource. This method always throws an exception, because the filesystem root cannot be deleted.</summary>
  internal override void Delete()
  {
    throw new NotSupportedException();
  }

  /// <summary>Enumerates the children of this resource, which is the set of drives on the system.</summary>
  internal override IEnumerable<FileSystemResource> GetChildResources()
  {
    return DriveInfo.GetDrives().Where(d => d.IsReady).Select<DriveInfo,FileSystemResource>(d =>
    {
      string name = FileSystemService.NormalizePath(FileSystemService.GetDriveName(d), CaseSensitive);
      return new DirectoryResource(d.RootDirectory, DAVUtility.CanonicalSegmentEncode(name) + "/",
                                   IsReadOnly, CaseSensitive, InfinitePropFind);
    });
  }

  /// <summary>Returns a dictionary containing the live properties of the resource.</summary>
  internal override IDictionary<XmlQualifiedName, object> GetLiveProperties(PropFindRequest request)
  {
    Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
    properties.Add(DAVNames.displayname, "Root");
    properties.Add(DAVNames.resourcetype, ResourceType.Collection);
    if(!IsReadOnly && request.Context.LockManager != null)
    {
      Func<object> getLocks = () => request.Context.LockManager.GetLocks("", LockSelection.SelfAndRecursiveAncestors, null);
      properties.Add(DAVNames.supportedlock, LockType.WriteLocks);
      properties.Add(DAVNames.lockdiscovery, getLocks);
    }
    return properties;
  }

  /// <summary>Non-recursively moves the current item to the given path. This method always throws <see cref="NotSupportedException"/>.</summary>
  internal override void MoveTo(string diskPath)
  {
    throw new NotSupportedException();
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
