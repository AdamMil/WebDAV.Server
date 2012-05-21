using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using HiA.WebDAV.Server.Configuration;

// TODO: document usage, web.config example, etc.
// TODO: authorization against the request URI and/or handling of access exceptions in resolving/loading the request resource
// TODO: add the ETag header

namespace HiA.WebDAV.Server
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
  ///     <term>readOnly</term>
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

    value = parameters.TryGetValue("readOnly");
    IsReadOnly = string.IsNullOrEmpty(value) || XmlConvert.ToBoolean(value);

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

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/MakeCollection/node()" />
  public override void MakeCollection(MkColRequest request)
  {
    // TODO: implement this
    base.MakeCollection(request);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    // if we allow writes and the OPTIONS request refers to a specific unmapped file...
    if(!IsReadOnly && !request.IsServerQuery && request.Context.RequestPath.EndsWith("/", StringComparison.Ordinal))
    {
      request.AllowedMethods.Add(HttpMethods.Put);   // then we support the PUT verb to create a new file there
      request.AllowedMethods.Add(HttpMethods.MkCol); // and the MKCOL verb to create a new directory there
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/ResolveResource/node()" />
  public override IWebDAVResource ResolveResource(WebDAVContext context)
  {
    if(context == null) throw new ArgumentNullException();

    // disallow .. in the request path
    if(context.RequestPath.StartsWith("../", StringComparison.Ordinal) || context.RequestPath.EndsWith("/..", StringComparison.Ordinal) ||
       context.RequestPath.Contains("/../", StringComparison.Ordinal))
    {
      throw Exceptions.BadRequest("It is illegal to use .. to access a parent directory in the request URL.");
    }

    if(RootPath != null) // if we're serving a place on the filesystem...
    {
      return ResolveResource(RootPath, context.RequestPath, ""); // then resolve the request normally
    }
    else // otherwise, we have a virtual root that encompasses the system's drives, so we'll need to resolve it specially
    {
      // resolve the first component to a drive name. if there is no first component, then they're referencing the root itself
      if(string.IsNullOrEmpty(context.RequestPath)) return new FileSystemRootResource();

      int slash = context.RequestPath.IndexOf('/');
      string driveName = slash == -1 ? context.RequestPath : context.RequestPath.Substring(0, slash);
      string requestPath = slash == -1 ? null : context.RequestPath.Substring(slash+1);

      DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(driveName, GetDriveName(d), comparison));
      if(drive == null) return null; // if there's no such drive, then we couldn't find the resource

      string canonicalDrivePath = DAVUtility.CanonicalPathEncode(GetDriveName(drive)) + "/";
      // if the root of the drive was requested, then we've got all the information needed to resolve the resource
      if(string.IsNullOrEmpty(requestPath)) return new DirectoryResource(drive.RootDirectory, canonicalDrivePath, IsReadOnly);
      else return ResolveResource(drive.RootDirectory.FullName, requestPath, canonicalDrivePath); // otherwise, resolve it normally
    }
  }

  readonly StringComparison comparison;

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

  /// <summary>Performs the normal resolution algorithm for a request.</summary>
  /// <param name="fsRoot">A location on the filesystem from where to begin searching.</param>
  /// <param name="requestPath">The relative request path.</param>
  /// <param name="davRoot">The canonical relative URL corresponding to <paramref name="fsRoot"/>.</param>
  IWebDAVResource ResolveResource(string fsRoot, string requestPath, string davRoot)
  {
    string combinedPath = Path.Combine(fsRoot, requestPath); // add the request path to the file system root

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
    throw new WebDAVException(ConditionCodes.NotImplemented); // TODO: implement property setting
  }

  /// <summary>Gets whether the resource is read-only. If true, the resource must deny all requests to change it.</summary>
  protected bool IsReadOnly { get; private set; }

  readonly string _canonicalPath;

  /// <summary>Adds a <see cref="PropFindResource"/> representing a directory to the response.</summary>
  internal static void AddPropFindDirectory(PropFindRequest request, IEnumerable<XmlQualifiedName> dirProps,
                                            IEnumerable<XmlQualifiedName> fileProps, string davPath, DirectoryInfo directory,
                                            string displayName, int level)
  {
    // add the resource for the directory itself
    PropFindResource directoryResource = new PropFindResource(davPath);
    if((request.Flags & PropFindFlags.NamesOnly) != 0)
    {
      directoryResource.SetNames(dirProps);
    }
    else
    {
      foreach(XmlQualifiedName property in dirProps)
      {
        if(property == Names.resourcetype) directoryResource.SetValue(property, ResourceType.Collection);
        else if(displayName != null && property == Names.displayname) directoryResource.SetValue(property, displayName);
        else SetEntryProperties(directoryResource, property, directory);
      }
    }
    request.Resources.Add(directoryResource);

    // if we need to provide information about the directory's children too...
    if(request.Depth == Depth.SelfAndDescendants || level == 0 && request.Depth == Depth.SelfAndChildren)
    {
      try
      {
        foreach(FileSystemInfo info in directory.GetFileSystemInfos())
        {
          if((info.Attributes & FileAttributes.Directory) != 0) // if the child is another directory, recurse
          {
            AddPropFindDirectory(request, dirProps, fileProps, davPath + DAVUtility.CanonicalPathEncode(info.Name) + "/",
                                  (DirectoryInfo)info, null, level+1);
          }
          else // otherwise, the child is a file
          {
            try { AddPropFindFile(request, fileProps, davPath + DAVUtility.CanonicalPathEncode(info.Name), (FileInfo)info); }
            catch(UnauthorizedAccessException) { } // ignore inaccessible files
            catch(FileNotFoundException) { } // ignore files that disappear before we have a chance to process them
          }
        }
      }
      catch(UnauthorizedAccessException) // ignore inaccessible directories
      {
      }
      catch(DirectoryNotFoundException) // ignore directories that disappear before we have a chance to process them
      {
      }
    }
  }

  /// <summary>Adds a <see cref="PropFindResource"/> representing a file to the response.</summary>
  internal static void AddPropFindFile(PropFindRequest request, IEnumerable<XmlQualifiedName> fileProps, string davPath, FileInfo file)
  {
    PropFindResource fileResource = new PropFindResource(davPath);
    if((request.Flags & PropFindFlags.NamesOnly) != 0)
    {
      fileResource.SetNames(fileProps);
    }
    else
    {
      foreach(XmlQualifiedName property in fileProps)
      {
        if(property == Names.resourcetype) fileResource.SetValue(property, null);
        else if(property == Names.getcontentlength) fileResource.SetValue(property, null, (ulong)file.Length, null);
        else SetEntryProperties(fileResource, property, file);
      }
    }
    request.Resources.Add(fileResource);
  }

  internal static ConditionCode GetStatusFromException(WebDAVRequest request, Exception ex)
  {
    return ex is UnauthorizedAccessException ? ConditionCodes.Forbidden :
           ex is FileNotFoundException || ex is DirectoryNotFoundException ? ConditionCodes.NotFound :
           new ConditionCode(HttpStatusCode.InternalServerError, request.Context.Settings.ShowSensitiveErrors ? ex.Message : null);
  }

  static void SetEntryProperties(PropFindResource resource, XmlQualifiedName property, FileSystemInfo entry)
  {
    if(property == Names.displayname) resource.SetValue(property, null, entry.Name, null);
    else if(property == Names.creationdate) resource.SetValue(property, new DateTimeOffset(entry.CreationTime));
    else if(property == Names.getlastmodified) resource.SetValue(property, new DateTimeOffset(entry.LastWriteTime));
    else resource.SetError(property, ConditionCodes.NotFound);
  }
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
    if(IsReadOnly) request.Status = ConditionCodes.Forbidden;
    else Delete(request, request.Context.ServiceRoot + CanonicalPath, Info, true);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Context.Response.AddHeader("Last-Modified", Info.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture));
    // write a basic index.html-like response containing the items in the directory
    FileSystemInfo[] entries = Info.GetFileSystemInfos();
    GetOrHeadRequest.IndexItem[] items = new GetOrHeadRequest.IndexItem[entries.Length];
    for(int i=0; i<items.Length; i++) items[i] = new GetOrHeadRequest.IndexItem(entries[i]);
    request.WriteSimpleIndexHtml(items);
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(!IsReadOnly) // writable directories support deletion, copying, and moving
    {
      request.AllowedMethods.Add(HttpMethods.Delete);
      request.AllowedMethods.Add(HttpMethods.Copy); // TODO: implement copy and move
      request.AllowedMethods.Add(HttpMethods.Move);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    HashSet<XmlQualifiedName> fileProperties = properties;

    if((request.Flags & PropFindFlags.IncludeAll) != 0) // if the client requested all properties, add the ones we support
    {
      properties.Add(Names.displayname); // all filesystem resources support these
      properties.Add(Names.resourcetype);
      properties.Add(Names.creationdate);
      properties.Add(Names.getlastmodified);

      fileProperties = new HashSet<XmlQualifiedName>(properties);
      fileProperties.Add(Names.getcontentlength); // files also have the DAV:getcontentlength property
    }

    try { AddPropFindDirectory(request, properties, fileProperties, CanonicalPath, Info, null, 0); }
    catch(Exception ex) { throw new WebDAVException(GetStatusFromException(request, ex)); }
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
      bool childFailed = false; // keep track of whether any child failed to be deleted
      foreach(FileSystemInfo info in dirInfo.GetFileSystemInfos()) // for each file and subdirectory...
      {
        if((info.Attributes & FileAttributes.Directory) != 0) // if it's a directory, delete it recursively
        {
          childFailed |= !Delete(request, directoryUrl + DAVUtility.CanonicalPathEncode(info.Name) + "/", (DirectoryInfo)info, false);
        }
        else // otherwise, it's a file
        {
          try
          {
            info.Delete();
          }
          catch(Exception ex) // if the file couldn't be deleted, add an error message about it
          {
            request.FailedMembers.Add(directoryUrl + DAVUtility.CanonicalPathEncode(info.Name), GetStatusFromException(request, ex));
            childFailed = true; // and remember that a child failed
          }
        }
      }

      if(!childFailed) dirInfo.Delete(true); // if all children were successfully deleted, delete the current directory
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
      else request.FailedMembers.Add(directoryUrl, GetStatusFromException(request, ex));
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
      // if the user doesn't have access to delete the file, respond with 403 Forbidden
      // TODO: it would be better to respond with 401 Unauthorized instead, because authorization might change whether the method succeeds.
      // we would need to add a WWW-Authorization header in that case, but perhaps we can do that... also, this same consideration applies
      // to other cases where UnauthorizedAccessException is transmuted into 403 Forbidden, such as DirectoryResource.Delete()
      try { Info.Delete(); }
      catch(UnauthorizedAccessException) { request.Status = ConditionCodes.Forbidden; }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    try
    {
      request.Context.Response.AddHeader("Last-Modified", Info.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture));
      using(FileStream stream = Info.OpenRead()) request.WriteStandardResponse(stream);
    }
    catch(Exception ex)
    {
      // set the error status if we didn't start writing any file content yet
      if(request.Context.Response.BufferOutput) request.Status = GetStatusFromException(request, ex);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Options/node()" />
  public override void Options(OptionsRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    if(!IsReadOnly) // writable directories support deletion, copying, and moving
    {
      request.AllowedMethods.Add(HttpMethods.Delete);
      request.AllowedMethods.Add(HttpMethods.Copy); // TODO: implement copy and move
      request.AllowedMethods.Add(HttpMethods.Move);
      request.AllowedMethods.Add(HttpMethods.Put);
    }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    if((request.Flags & PropFindFlags.IncludeAll) != 0) // if the client requested all properties...
    {
      properties.Add(Names.displayname); // add the properties we support to the list
      properties.Add(Names.resourcetype);
      properties.Add(Names.creationdate);
      properties.Add(Names.getlastmodified);
      properties.Add(Names.getcontentlength);
    }

    try { AddPropFindFile(request, properties, CanonicalPath, Info); }
    catch(Exception ex) { throw new WebDAVException(GetStatusFromException(request, ex)); }
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/Put/node()" />
  public override void Put(PutRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.Status = ConditionCodes.NotImplemented; // TODO: implement PUT
  }
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

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/GetOrHead/node()" />
  public override void GetOrHead(GetOrHeadRequest request)
  {
    if(request == null) throw new ArgumentNullException();
    request.WriteSimpleIndexHtml(DriveInfo.GetDrives().Where(d => d.IsReady)
                                          .Select(d => new GetOrHeadRequest.IndexItem(FileSystemService.GetDriveName(d), true)).ToArray());
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void Options(OptionsRequest request)
  {
    // the defaults are fine because RootResource is always read-only and we haven't implemented locking yet
  }

  /// <include file="documentation.xml" path="/DAV/IWebDAVResource/PropFind/node()" />
  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    if((request.Flags & PropFindFlags.IncludeAll) != 0)
    {
      properties.Add(Names.displayname); // the root resource doesn't actually exist, so it only supports a couple properties
      properties.Add(Names.resourcetype);
    }

    // add the properties for the root resource to the response
    PropFindResource rootResource = new PropFindResource(CanonicalPath);
    if(namesOnly)
    {
      rootResource.SetNames(properties);
    }
    else
    {
      foreach(XmlQualifiedName property in properties)
      {
        if(property == Names.displayname) rootResource.SetValue(property, null, "Root", null);
        else if(property == Names.resourcetype) rootResource.SetValue(property, null, ResourceType.Collection, null);
        else rootResource.SetError(property, ConditionCodes.NotFound);
      }
    }
    request.Resources.Add(rootResource);

    if(request.Depth == Depth.SelfAndChildren || request.Depth == Depth.SelfAndDescendants) // if we need to recurse...
    {
      try
      {
        HashSet<XmlQualifiedName> fileProperties = properties;
        if((request.Flags & PropFindFlags.IncludeAll) != 0)
        {
          properties.Add(Names.creationdate); // add the properties that our descendants will have
          properties.Add(Names.getlastmodified);

          fileProperties = new HashSet<XmlQualifiedName>(properties);
          fileProperties.Add(Names.getcontentlength);
        }

        // add directory resources for each drive that is ready
        foreach(DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
          string name = FileSystemService.GetDriveName(drive);
          AddPropFindDirectory(request, properties, fileProperties, CanonicalPath + name + "/", drive.RootDirectory, name, 1);
        }
      }
      catch(Exception ex)
      {
        throw new WebDAVException(GetStatusFromException(request, ex));
      }
    }
  }
}
#endregion

} // namespace HiA.WebDAV.Server
