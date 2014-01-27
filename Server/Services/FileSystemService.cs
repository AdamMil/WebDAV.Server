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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

// TODO: authorization against the request URI and/or handling of access exceptions in resolving/loading the request resource
// TODO: improve GetEntityMetadata() for dynamic resources
// TODO: more cleanup and refactoring...
// TODO: some type of permissions model that allows us to separate GET access from LOCK access. we should probably tie LOCK access to
// write access...

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
  ///     <term>caseSensitive</term>
  ///     <description>xs:boolean</description>
  ///     <description>Determines whether URL resolution will perform case-sensitive matches against the file system. The default is true.</description>
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
  ///     <description>Determines whether the DAV service allows the creation, deletion, and modification of files and directories. The
  ///       default is false.
  ///     </description>
  ///   </item>
  /// </list>
  /// </remarks>
  public FileSystemService(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();

    string value = parameters.TryGetValue("caseSensitive");
    CaseSensitive = string.IsNullOrEmpty(value) || XmlConvert.ToBoolean(value);
    comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    value = parameters.TryGetValue("writable");
    IsReadOnly = string.IsNullOrEmpty(value) || !XmlConvert.ToBoolean(value);

    RootPath = parameters.TryGetValue("fsRoot");
    if(RootPath == null && (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX))
    {
      RootPath = "/"; // Unix-like operating systems don't have drives like Windows does, so we can just serve the root of the filesystem
    }
    if(RootPath != null) RootPath = RootPath.Length == 0 ? null : PathUtility.NormalizePath(RootPath);
  }

  /// <summary>Gets whether URL resolution will perform case-sensitive matches against the file system.</summary>
  public bool CaseSensitive { get; private set; }

  /// <summary>Gets whether the service allows the creation, deletion, and modification of file system resources.</summary>
  public bool IsReadOnly { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/IsReusable/node()" />
  public override bool IsReusable
  {
    get { return true; }
  }

  /// <summary>Gets the root path on the filesystem from which files will be served. If null, the root path will provide access to all
  /// files on all drives.
  /// </summary>
  public string RootPath { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Lock/node()" />
  public override void CreateAndLock(LockRequest request)
  {
    if(IsReadOnly || request.Context.LockManager == null)
    {
      base.CreateAndLock(request); // call the default implementation, which denies the request
    }
    else
    {
      string lockPath = request.Context.ServiceRoot + request.Context.RequestPath;
      CreateNewFile(request, s => request.ProcessStandardRequest(LockType.WriteLocks, lockPath, null, false));
      if(request.Status == null || request.Status.IsSuccessful)
      {
        request.Status = ConditionCodes.Created; // then return 201 Created
      }
      else if(request.NewLock != null) // if an error occurred but we already created the lock...
      {
        request.Context.LockManager.RemoveLock(request.NewLock); // remove the lock
        request.NewLock = null;
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/MakeCollection/node()" />
  public override void MakeCollection(MkColRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly)
    {
      base.MakeCollection(request); // call the default implementation, which denies the request
    }
    else if(request.Context.Request.InputStream.Length != 0) // if the client submitted a request body...
    {
      request.Status = ConditionCodes.UnsupportedMediaType; // reply with 415 Unsupported Media Type as per RFC 4918 section 9.3
    }
    else // the request might be valid...
    {
      // it's unlikely that the client will submit any preconditions with a MKCOL request, but check them just in case
      ConditionCode precondition = request.CheckPreconditions(null);
      if(precondition != null) // if the client specified that the entity must exist...
      {
        request.Status = precondition; // give an error
      }
      else
      {
        try
        {
          string path = DAVUtility.RemoveTrailingSlash(request.Context.RequestPath);
          if(string.IsNullOrEmpty(path)) // if the user is requesting to create the root directory...
          {
            // RootPath should be null and nonexistent. otherwise, ResolveResource would have returned something
            Directory.CreateDirectory(RootPath);
          }
          else
          {
            int lastSlash = path.LastIndexOf('/');
            string parent = lastSlash == -1 ? "" : path.Substring(0, lastSlash);

            IWebDAVResource resource = ResolveResource(parent);
            DirectoryResource directory = resource as DirectoryResource;
            if(resource == null) request.Status = ConditionCodes.Conflict; // nonexistent parent gets 409 Conflict as per RFC 4918 sec. 9.3
            else if(directory == null) request.Status = ConditionCodes.Forbidden; // non-directory parents don't support MKCOL
            else directory.Info.CreateSubdirectory(HttpUtility.UrlDecode(lastSlash == -1 ? path : path.Substring(lastSlash+1)));
          }
        }
        catch(Exception ex)
        {
          request.Status = FileSystemResource.GetStatusFromException(request, ex);
        }
      }
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
        request.AllowedMethods.Add(HttpMethods.Put);
      }
      request.SupportsLocking = request.Context.LockManager != null;
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
      CreateNewFile(request, stream => request.ProcessStandardRequest(stream, null));
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public override IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath)
  {
    if(context == null) throw new ArgumentNullException();
    return ResolveResource(resourcePath);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/Unlock/node()" />
  public override void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly || request.Context.LockManager == null) base.Unlock(request); // deny the request if locking is not supported
    else request.ProcessStandardRequest(); // otherwise, anyone can remove a dangling lock
  }

  readonly StringComparison comparison;

  /// <summary>Gets the path on disk to the resource named by the given path, even if the resource doesn't exist.</summary>
  internal string GetDiskPath(string resourcePath)
  {
    // disallow .. in the request path
    if(resourcePath.StartsWith("../", StringComparison.Ordinal) || resourcePath.EndsWith("/..", StringComparison.Ordinal) ||
       resourcePath.Contains("/../", StringComparison.Ordinal))
    {
      throw Exceptions.BadRequest("It is illegal to use .. to access a parent directory in the request URL.");
    }

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

  void CreateNewFile(WebDAVRequest request, Action<Stream> processRequest)
  {
    // check preconditions in case the client expects the resource to exist
    ConditionCode precondition = request.CheckPreconditions(null);
    if(precondition != null)
    {
      request.Status = precondition;
    }
    else
    {
      try
      {
        string path = request.Context.RequestPath;
        int lastSlash = path.LastIndexOf('/');
        string fileName = HttpUtility.UrlDecode(lastSlash == -1 ? path : path.Substring(lastSlash+1));
        if(fileName.Length == 0)
        {
          request.Status = new ConditionCode(HttpStatusCode.BadRequest, "The file name was missing.");
        }
        else
        {
          string parent = lastSlash == -1 ? "" : path.Substring(0, lastSlash);
          IWebDAVResource resource = ResolveResource(parent);
          DirectoryResource directory = resource as DirectoryResource;
          if(resource == null) // if the parent doesn't exist...
          {
            request.Status = ConditionCodes.Conflict; // nonexistent parents cause a 409 Conflict response as per RFC 4918 sec. 9.7
          }
          else if(directory == null) // if the parent is not a directory...
          {
            request.Status = ConditionCodes.Forbidden; // non-directory parents don't support creating children
          }
          else // the parent is a directory
          {
            path = Path.Combine(directory.Info.FullName, fileName);
            bool success;
            using(FileStream stream = File.Open(path, FileMode.CreateNew, FileAccess.ReadWrite))
            {
              processRequest(stream);
              success = request.Status == null || request.Status.IsSuccessful;
              if(success) // if the request was successfully executed...
              {
                // write the ETag and Last-Modified headers
                request.Context.Response.Headers[HttpHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
                stream.Close(); // close the file to ensure the last modified time gets updated
                request.Context.Response.Headers[HttpHeaders.LastModified] =
                    new FileInfo(path).LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);
              }
            }

            if(!success)
            {
              try { File.Delete(path); }
              catch { }
            }
          }
        }
      }
      catch(Exception ex)
      {
        request.Status = FileSystemResource.GetStatusFromException(request, ex);
      }
    }
  }

  IWebDAVResource ResolveResource(string resourcePath)
  {
    // disallow .. in the request path
    if(resourcePath.StartsWith("../", StringComparison.Ordinal) || resourcePath.EndsWith("/..", StringComparison.Ordinal) ||
       resourcePath.Contains("/../", StringComparison.Ordinal))
    {
      throw Exceptions.BadRequest("It is illegal to use .. to access a parent directory in the request URL.");
    }

    if(RootPath != null) // if we're serving a place on the filesystem...
    {
      return ResolveResource(RootPath, resourcePath, ""); // then resolve the request normally
    }
    else // otherwise, we have a virtual root that encompasses the system's drives, so we'll need to resolve it specially
    {
      // resolve the first component to a drive name. if there is no first component, then they're referencing the root itself
      if(string.IsNullOrEmpty(resourcePath)) return new FileSystemRootResource();

      int slash = resourcePath.IndexOf('/');
      string driveName = slash == -1 ? resourcePath : resourcePath.Substring(0, slash);
      resourcePath = slash == -1 ? null : resourcePath.Substring(slash+1);

      DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(driveName, GetDriveName(d), comparison));
      if(drive == null) return null; // if there's no such drive, then we couldn't find the resource

      string canonicalDrivePath = DAVUtility.CanonicalPathEncode(GetDriveName(drive)) + "/";
      // if the root of the drive was requested, then we've got all the information needed to resolve the resource
      if(string.IsNullOrEmpty(resourcePath)) return new DirectoryResource(drive.RootDirectory, canonicalDrivePath, IsReadOnly);
      else return ResolveResource(drive.RootDirectory.FullName, resourcePath, canonicalDrivePath); // otherwise, resolve it normally
    }
  }

  /// <summary>Performs the normal resolution algorithm for a request.</summary>
  /// <param name="fsRoot">A location on the filesystem from where to begin searching.</param>
  /// <param name="resourcePath">The relative request path.</param>
  /// <param name="davRoot">The canonical relative URL corresponding to <paramref name="fsRoot"/>.</param>
  IWebDAVResource ResolveResource(string fsRoot, string resourcePath, string davRoot)
  {
    string combinedPath = Path.Combine(fsRoot, HttpUtility.UrlDecode(resourcePath)); // add the request path to the file system root

    DirectoryInfo directory = new DirectoryInfo(combinedPath); // the combined path names a directory, return a directory resource
    if(directory.Exists) return new DirectoryResource(directory, davRoot + GetCanonicalPath(fsRoot, directory.FullName, true), IsReadOnly);

    FileInfo file = new FileInfo(combinedPath); // otherwise, if the combined path names a file, return a file resource
    if(file.Exists) return new FileResource(file, davRoot + GetCanonicalPath(fsRoot, file.FullName, false), IsReadOnly);

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
}
#endregion

#region FileSystemResource
/// <summary>Provides a base class for file system resources, intended to be used with <see cref="FileSystemService"/>.</summary>
public abstract class FileSystemResource : WebDAVResource
{
  /// <summary>Initializes a new <see cref="FileSystemResource"/>.</summary>
  /// <param name="canonicalPath">The canonical relative path to the resource.</param>
  /// <param name="isReadOnly">Determines whether the resource is read-only. If true, it will deny all requests to change the resource.</param>
  internal FileSystemResource(string canonicalPath, bool isReadOnly)
  {
    if(canonicalPath == null) throw new ArgumentNullException();
    _canonicalPath = canonicalPath;
    IsReadOnly     = isReadOnly;
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CanonicalPath/node()" />
  public override string CanonicalPath
  {
    get { return _canonicalPath; }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropPatch/node()" />
  public override void PropPatch(PropPatchRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.ProcessStandardRequest(); // allow setting of dead properties only
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Unlock/node()" />
  public override void Unlock(UnlockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly || request.Context.LockManager == null) base.Unlock(request); // use the base implementation if we don't support locking
    else request.ProcessStandardRequest();
  }

  /// <summary>Gets whether the resource is read-only. If true, the resource must deny all requests to change it.</summary>
  protected bool IsReadOnly { get; private set; }

  internal Dictionary<XmlQualifiedName, object> GetEntryProperties(WebDAVContext context, FileSystemInfo info, string canonicalPath)
  {
    Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
    properties.Add(DAVNames.displayname, info.Name);
    properties.Add(DAVNames.creationdate, new DateTimeOffset(info.CreationTime));
    properties.Add(DAVNames.getlastmodified, new DateTimeOffset(info.LastWriteTime));
    if(!IsReadOnly && context.LockManager != null)
    {
      Func<object> getLocks = () => context.LockManager.GetLocks(context.ServiceRoot + canonicalPath, true, false, null);
      properties.Add(DAVNames.lockdiscovery, getLocks);
      properties.Add(DAVNames.supportedlock, LockType.WriteLocks);
    }
    properties.Add(DAVNames.resourcetype, info is DirectoryInfo ? ResourceType.Collection : null);

    FileInfo fileInfo = info as FileInfo;
    if(fileInfo != null) properties.Add(DAVNames.getcontentlength, (ulong)fileInfo.Length);
    return properties;
  }

  internal Dictionary<XmlQualifiedName, object> TryGetEntryProperties(WebDAVContext context, FileSystemInfo info, string canonicalPath)
  {
    try { return GetEntryProperties(context, info, canonicalPath); }
    catch(DirectoryNotFoundException) { }
    catch(FileNotFoundException) { }
    catch(UnauthorizedAccessException) { }
    return new Dictionary<XmlQualifiedName, object>();
  }

  internal static ConditionCode GetStatusFromException(WebDAVRequest request, Exception ex)
  {
    return ex is UnauthorizedAccessException || ex is PathTooLongException ? ConditionCodes.Forbidden :
           ex is FileNotFoundException || ex is DirectoryNotFoundException ? ConditionCodes.NotFound :
           new ConditionCode(HttpStatusCode.InternalServerError, request.Context.Settings.ShowSensitiveErrors ? ex.Message : null);
  }

  internal static IEnumerable<FileSystemInfo> TryGetEntryChildren(FileSystemInfo info)
  {
    DirectoryInfo dirInfo = info as DirectoryInfo;
    if(dirInfo != null)
    {
      try { return dirInfo.GetFileSystemInfos(); }
      catch(DirectoryNotFoundException) { }
      catch(FileNotFoundException) { }
      catch(UnauthorizedAccessException) { }
    }
    return null;
  }

  readonly string _canonicalPath;
}
#endregion

#region DirectoryResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a directory in the filesystem.</summary>
public class DirectoryResource : FileSystemResource
{
  /// <summary>Initializes a new <see cref="DirectoryResource"/>.</summary>
  /// <param name="info">A <see cref="DirectoryInfo"/> representing the directory to serve. The directory must have a valid name; it cannot
  /// be a root directory.
  /// </param>
  /// <param name="canonicalPath">The canonical relative path to the resource. See <see cref="IWebDAVResource.CanonicalPath"/> for
  /// details.
  /// </param>
  /// <param name="isReadOnly">Determines whether the directory resource is read-only. If true, it will deny all requests to change it.</param>
  public DirectoryResource(DirectoryInfo info, string canonicalPath, bool isReadOnly) : base(canonicalPath, isReadOnly)
  {
    if(info == null) throw new ArgumentNullException();
    Info = info;
  }

  /// <summary>Gets the <see cref="DirectoryInfo"/> object representing the directory on the filesystem.</summary>
  public DirectoryInfo Info { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  public override void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(IsReadOnly)
    {
      base.Delete(request); // call the default implementation, which denies the request
    }
    else
    {
      ConditionCode status = request.CheckPreconditions(null);
      if(status == null) Delete(request, CanonicalPath, Info, true);
      else request.Status = status;
    }
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
    // write a basic index.html-like response containing the items in the directory
    FileSystemInfo[] entries = Info.GetFileSystemInfos();
    GetOrHeadRequest.IndexItem[] items = new GetOrHeadRequest.IndexItem[entries.Length];
    for(int i=0; i<items.Length; i++) items[i] = new GetOrHeadRequest.IndexItem(entries[i]);
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
      request.AllowedMethods.Add(HttpMethods.Delete); // writable directories can be deleted
      request.SupportsLocking = request.Context.LockManager != null;
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try
    {
      request.ProcessStandardRequest((FileSystemInfo)Info, info => info == Info ? "" : DAVUtility.CanonicalPathEncode(info.Name),
                                     (info, path) => TryGetEntryProperties(request.Context, info, path),
                                     (info, path) => TryGetEntryChildren(info));
    }
    catch(Exception ex)
    {
      request.Status = GetStatusFromException(request, ex);
    }
  }

  /// <summary>Attempts to recursively delete a directory. If anything goes wrong, appropriate error messages will be added to the request.</summary>
  /// <param name="request">The <see cref="DeleteRequest"/> that this method is serving.</param>
  /// <param name="directoryUrl">The canonical absolute path to the directory represented by <paramref name="dirInfo"/>, including the
  /// trailing slash.
  /// </param>
  /// <param name="dirInfo">The directory to delete.</param>
  /// <param name="isRequestResource">True if <paramref name="dirInfo"/> represents the request resource or false if it represents a
  /// descendant of it.
  /// </param>
  static bool Delete(DeleteRequest request, string directoryUrl, DirectoryInfo dirInfo, bool isRequestResource)
  {
    try
    {
      ILockManager lockManager = request.Context.LockManager;
      IPropertyStore propertyStore = request.Context.PropertyStore;
      bool childFailed = false; // keep track of whether any child failed to be deleted
      foreach(FileSystemInfo info in dirInfo.GetFileSystemInfos()) // for each file and subdirectory...
      {
        if((info.Attributes & FileAttributes.Directory) != 0) // if it's a directory, delete it recursively
        {
          childFailed |= !Delete(request, directoryUrl + DAVUtility.CanonicalPathEncode(info.Name) + "/", (DirectoryInfo)info, false);
        }
        else // otherwise, it's a file
        {
          string fileUrl = request.Context.ServiceRoot + directoryUrl + DAVUtility.CanonicalPathEncode(info.Name);
          try
          {
            info.Delete();
          }
          catch(Exception ex) // if the file couldn't be deleted, add an error message about it
          {
            request.FailedMembers.Add(fileUrl, GetStatusFromException(request, ex));
            childFailed = true; // and remember that a child failed
          }
          request.PostProcessRequest(fileUrl, false);
        }
      }

      if(!childFailed)
      {
        dirInfo.Delete(true); // if all children were successfully deleted, delete the current directory
        request.PostProcessRequest(request.Context.ServiceRoot + directoryUrl, false);
      }
      return !childFailed;
    }
    catch(Exception ex) // if the current directory couldn't be deleted (potentially because we couldn't read it in the first place)...
    {
      // if the root directory couldn't be deleted, set an error for the whole request. otherwise add the error to the FailedMembers
      // collection. if no children were deleted, failing the whole request is exactly the right thing to do. but if some children were
      // deleted, it seems a bit wrong because a client may interpret the whole request failing to mean that nothing could be deleted. i
      // would prefer to add the root directory to the FailedMembers collection in that case. the WebDAV specification (RFC 4918 section
      // 9.6.1) isn't very clear, but it seems to say that we shouldn't do that
      if(isRequestResource) request.Status = GetStatusFromException(request, ex);
      else request.FailedMembers.Add(request.Context.ServiceRoot + directoryUrl, GetStatusFromException(request, ex));
      return false;
    }
  }
}
#endregion

#region FileResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a file in the filesystem.</summary>
public class FileResource : FileSystemResource
{
  /// <summary>Initializes a new <see cref="FileResource"/>.</summary>
  /// <param name="info">A <see cref="FileInfo"/> representing the file to serve.</param>
  /// <param name="canonicalPath">The canonical relative path to the resource. See <see cref="IWebDAVResource.CanonicalPath"/> for
  /// details.
  /// </param>
  /// <param name="isReadOnly">Determines whether the file resource is read-only. If true, it will deny all requests to change it.</param>
  public FileResource(FileInfo info, string canonicalPath, bool isReadOnly) : base(canonicalPath, isReadOnly)
  {
    if(info == null) throw new ArgumentNullException();
    Info = info;
  }

  /// <summary>Gets the <see cref="FileInfo"/> object representing the file on the filesystem.</summary>
  public FileInfo Info { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/CopyOrMove/node()" />
  public override void CopyOrMove(CopyOrMoveRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    FileSystemService destService = request.DestinationService as FileSystemService;
    if(destService == null) // if we're not copying to another FileSystemService...
    {
      request.Status = ConditionCodes.BadGateway; // 502 Bad Gateway is used when we don't understand how to copy/move to the destination
    }
    else if(destService.IsReadOnly || request.IsMove && IsReadOnly ||
            request.DestinationResource != null && WebDAVModule.ShouldDenyAccess(request.Context, request.Destination))
    {
      request.Status = ConditionCodes.Forbidden; 
    }
    else if(!request.Overwrite && request.DestinationResource != null)
    {
      request.Status = ConditionCodes.PreconditionFailed;
    }
    else
    {
      ConditionCode precondition = request.CheckPreconditions(null);
      if(precondition != null)
      {
        request.Status = precondition;
      }
      else
      {
        string destPath = destService.GetDiskPath(request.DestinationPath);
        bool arePathsEqual =
          destPath != null && PathUtility.NormalizePath(destPath).OrdinalEquals(PathUtility.NormalizePath(Info.FullName));
        if(destPath == null || request.IsCopy && arePathsEqual)
        {
          request.Status = ConditionCodes.Forbidden;
        }
        else if(!arePathsEqual)
        {
          try
          {
            if(request.IsCopy)
            {
              bool existed = request.Overwrite && File.Exists(destPath);
              Info.CopyTo(destPath, request.Overwrite);
              request.Status = existed ? ConditionCodes.NoContent : ConditionCodes.Created;
            }
            else
            {
              bool existed = request.Overwrite && File.Exists(destPath);
              if(existed) File.Delete(destPath);
              Info.MoveTo(destPath);
            }
          }
          catch(DirectoryNotFoundException) { request.Status = ConditionCodes.Conflict; }
          catch(Exception ex) { request.Status = GetStatusFromException(request, ex); }
        }
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Delete/node()" />
  public override void Delete(DeleteRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    if(IsReadOnly)
    {
      request.Status = ConditionCodes.Forbidden;
    }
    else
    {
      ConditionCode status = request.CheckPreconditions(null);
      if(status != null)
      {
        request.Status = status;
      }
      else
      {
        // if the user doesn't have access to delete the file, respond with 403 Forbidden
        // TODO: it would be better to respond with 401 Unauthorized instead, because authorization might change whether the method
        // succeeds. we would need to add a WWW-Authorization header in that case, but perhaps we can do that... also, this same
        // consideration applies to other cases where UnauthorizedAccessException is transmuted into 403 Forbidden, such as
        // DirectoryResource.Delete()
        try
        {
          Info.Delete();
          request.PostProcessRequest(false);
        }
        catch(UnauthorizedAccessException) { request.Status = ConditionCodes.Forbidden; }
      }
    }
  }

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
        metadata.MediaType        = MimeTypes.GuessMimeType(Info.Name);
      }
    }
    if(includeEntityTag && metadata.EntityTag == null)
    {
      try { using(FileStream stream = Info.OpenRead()) metadata.EntityTag = DAVUtility.ComputeEntityTag(stream); }
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
      if(request.Context.Response.BufferOutput) request.Status = GetStatusFromException(request, ex);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Lock/node()" />
  public override void Lock(LockRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.ProcessStandardRequest(LockType.WriteLocks, false);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(!IsReadOnly) // writable files can be deleted and modified
    {
      request.AllowedMethods.Add(HttpMethods.Delete);
      request.AllowedMethods.Add(HttpMethods.Put);
      request.SupportsLocking = request.Context.LockManager != null;
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try { request.ProcessStandardRequest(GetEntryProperties(request.Context, Info, CanonicalPath)); }
    catch(Exception ex) { request.Status = GetStatusFromException(request, ex); }
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
            request.Context.Response.Headers[HttpHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
            stream.Close(); // close the file to ensure the last modified time gets updated
            Info.Refresh(); // refresh the last modified time
            request.Context.Response.Headers[HttpHeaders.LastModified] = Info.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);
          }
        }
      }
      catch(Exception ex)
      {
        request.Status = GetStatusFromException(request, ex);
      }
    }
  }

  EntityMetadata metadata;
}
#endregion

#region FileSystemRootResource
/// <summary>Implements a <see cref="FileSystemResource"/> that represents a virtual directory containing all of the system's active
/// drives.
/// </summary>
public class FileSystemRootResource : FileSystemResource
{
  /// <summary>Initializes a new <see cref="FileSystemRootResource"/> at the root of the WebDAV service.</summary>
  public FileSystemRootResource() : this("") { }

  /// <summary>Initializes a new <see cref="FileSystemRootResource"/>.</summary>
  /// <param name="canonicalPath">The canonical relative path to the resource. For a <see cref="FileSystemRootResource"/>, this is
  /// typically an empty string, to place the root resource at the root of the WebDAV service. See
  /// <see cref="IWebDAVResource.CanonicalPath"/> for details.
  /// </param>
  public FileSystemRootResource(string canonicalPath) : base(canonicalPath, true) { }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetEntityMetadata/node()" />
  public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  {
    return new EntityMetadata() { MediaType = "text/html" };
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.WriteSimpleIndexHtml(DriveInfo.GetDrives().Where(d => d.IsReady)
                                          .Select(d => new GetOrHeadRequest.IndexItem(FileSystemService.GetDriveName(d), true)).ToArray());
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

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try
    {
      request.ProcessStandardRequest((FileSystemInfo)null, info => GetMemberName(info),
                                     (info, path) => TryGetEntryProperties(request.Context, info, path),
                                     (info, path) => TryGetEntryChildren(info));
    }
    catch(Exception ex)
    {
      request.Status = GetStatusFromException(request, ex);
    }
  }

  new Dictionary<XmlQualifiedName, object> TryGetEntryProperties(WebDAVContext context, FileSystemInfo info, string canonicalPath)
  {
    if(info == null)
    {
      Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
      properties.Add(DAVNames.displayname, "Root");
      properties.Add(DAVNames.resourcetype, ResourceType.Collection);
      if(!IsReadOnly && context.LockManager != null)
      {
        Func<object> getLocks = () => context.LockManager.GetLocks(context.ServiceRoot, true, false, null);
        properties.Add(DAVNames.supportedlock, LockType.WriteLocks);
        properties.Add(DAVNames.lockdiscovery, getLocks);
      }
      return properties;
    }
    else
    {
      return base.TryGetEntryProperties(context, info, canonicalPath);
    }
  }

  static string GetMemberName(FileSystemInfo info)
  {
    if(info == null) return "";

    DirectoryInfo dirInfo = info as DirectoryInfo;
    if(dirInfo != null && dirInfo.Parent == null)
    {
      int colon = dirInfo.Name.IndexOf(':');
      return DAVUtility.CanonicalPathEncode(colon == -1 ? dirInfo.Name : dirInfo.Name.Substring(0, colon));
    }
    else
    {
      return DAVUtility.CanonicalPathEncode(info.Name);
    }
  }

  static new IEnumerable<FileSystemInfo> TryGetEntryChildren(FileSystemInfo info)
  {
    if(info == null)
    {
      return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory).Cast<FileSystemInfo>();
    }
    else
    {
      return FileSystemResource.TryGetEntryChildren(info);
    }
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
