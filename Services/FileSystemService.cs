using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using HiA.WebDAV.Configuration;

// TODO: document usage, web.config example, etc.
// TODO: authorization against the request URI and/or handling of access exceptions in loading the request resource

namespace HiA.WebDAV
{

#region FileSystemService
/// <summary>Implements a <see cref="WebDAVService"/> that serves files from the filesystem.</summary>
public class FileSystemService : WebDAVService
{
  /// <summary>Initializes a new <see cref="FileSystemService"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>The <see cref="FileSystemService"/> supports two parameters:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>caseSensitive</term>
  ///     <description>boolean</description>
  ///     <description>Determines whether URL resolution will perform case-sensitive matches against the file system. The default is true.</description>
  ///   </item>
  ///   <item>
  ///     <term>fsRoot</term>
  ///     <description>string</description>
  ///     <description>Determines the root within the filesystem where files will be served. If null or empty, all files on all drives will
  ///     be served.
  ///     </description>
  ///   </item>
  /// </list>
  /// </remarks>
  public FileSystemService(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();

    string caseSensitive = parameters.TryGetValue("caseSensitive");
    CaseSensitive = string.IsNullOrEmpty(caseSensitive) || XmlConvert.ToBoolean(caseSensitive);
    comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    RootPath = parameters.TryGetValue("fsRoot");
    if(RootPath != null) RootPath = RootPath.Length == 0 ? null : PathUtility.NormalizePath(RootPath);
  }

  /// <summary>Gets whether URL resolution will perform case-sensitive matches against the file system.</summary>
  public bool CaseSensitive { get; private set; }

  /// <include file="documentation.xml" path="/DAV/IWebDAVService/IsReusable/node()" />
  public override bool IsReusable
  {
    get { return true; }
  }

  /// <summary>Gets the root path on the filesystem from which files will be served. If null, the root path will provide access to all
  /// files on all drives.
  /// </summary>
  public string RootPath { get; private set; }

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

    if(RootPath != null)
    {
      return ResolveResource(RootPath, context.RequestPath, "");
    }
    else
    {
      // resolve the first component to a drive name. if there is no first component, then they're referencing the root itself
      if(string.IsNullOrEmpty(context.RequestPath)) return new RootResource();

      int slash = context.RequestPath.IndexOf('/');
      string driveName = slash == -1 ? context.RequestPath : context.RequestPath.Substring(0, slash);
      string requestPath = slash == -1 ? null : context.RequestPath.Substring(slash+1);

      DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(driveName, GetDriveName(d), comparison));
      if(drive == null) return null; // if there's no such drive, then we couldn't find it

      // if the user requested the root of the drive, then we've got all the information we need to resolve the resource
      if(string.IsNullOrEmpty(requestPath)) return new DirectoryResource(drive.RootDirectory, GetDriveName(drive));
      else return ResolveResource(drive.RootDirectory.FullName, requestPath, GetDriveName(drive) + "/"); // otherwise resolve it normally
    }
  }

  readonly StringComparison comparison;

  /// <summary>Adds a <see cref="PropFindResource"/> representing a directory to the response.</summary>
  internal static void AddPropFindDirectory(PropFindRequest request, IEnumerable<XmlQualifiedName> dirProps,
                                            IEnumerable<XmlQualifiedName> fileProps, string davPath, DirectoryInfo directory, int level)
  {
    try
    {
      PropFindResource directoryResource = new PropFindResource(davPath);
      if((request.Flags & PropFindFlags.NamesOnly) != 0)
      {
        directoryResource.SetNames(dirProps);
      }
      else
      {
        foreach(XmlQualifiedName property in dirProps)
        {
          if(property == Names.resourcetype) directoryResource.SetValue(property, ResourceType.Collection, null);
          else SetEntryProperties(directoryResource, property, directory);
        }
      }
      request.Resources.Add(directoryResource);

      if(request.Depth == Depth.SelfAndDescendants || level == 0 && request.Depth == Depth.SelfAndChildren)
      {
        foreach(DirectoryInfo subdir in directory.GetDirectories())
        {
          AddPropFindDirectory(request, dirProps, fileProps, davPath + subdir.Name + "/", subdir, level+1);
        }

        foreach(FileInfo file in directory.GetFiles()) AddPropFindFile(request, fileProps, davPath, file);
      }
    }
    catch(UnauthorizedAccessException) // ignore inaccessible directories
    {
    }
  }

  /// <summary>Adds a <see cref="PropFindResource"/> representing a file to the response.</summary>
  internal static void AddPropFindFile(PropFindRequest request, IEnumerable<XmlQualifiedName> fileProps, string davPath, FileInfo file)
  {
    try
    {
      PropFindResource fileResource = new PropFindResource(davPath + file.Name);
      if((request.Flags & PropFindFlags.NamesOnly) != 0)
      {
        fileResource.SetNames(fileProps);
      }
      else
      {
        foreach(XmlQualifiedName property in fileProps)
        {
          if(property == Names.resourcetype) fileResource.SetValue(property, null);
          else if(property == Names.getcontentlength) fileResource.SetValue(property, (ulong)file.Length, null);
          else SetEntryProperties(fileResource, property, file);
        }
      }
      request.Resources.Add(fileResource);
    }
    catch(UnauthorizedAccessException) // ignore inaccessible files
    {
    }
  }

  internal static string GetDriveName(DriveInfo drive)
  {
    int sep = drive.Name.IndexOf(Path.VolumeSeparatorChar);
    return sep == -1 ? drive.Name : sep == 0 ? "fsroot" : drive.Name.Substring(0, sep);
  }

  static string GetCanonicalPath(string fsRoot, string fullPath, bool trailingSlash)
  {
    string relativePath = fullPath.Substring(fsRoot.Length);
    if(relativePath.Length != 0 && (relativePath[0] == Path.DirectorySeparatorChar || relativePath[0] == Path.AltDirectorySeparatorChar))
    {
      relativePath = relativePath.Substring(1);
    }
    if(trailingSlash)
    {
      char c = relativePath.Length == 0 ? '\0' : relativePath[relativePath.Length-1];
      if(c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar) relativePath += "/";
    }

    if(Path.DirectorySeparatorChar != '/') relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
    if(Path.AltDirectorySeparatorChar != '/') relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');
    return relativePath;
  }

  static IWebDAVResource ResolveResource(string fsRoot, string requestPath, string davRoot)
  {
    string combinedPath = Path.Combine(fsRoot, requestPath);

    DirectoryInfo directory = new DirectoryInfo(combinedPath);
    if(directory.Exists) return new DirectoryResource(directory, davRoot + GetCanonicalPath(fsRoot, directory.FullName, true));

    FileInfo file = new FileInfo(combinedPath);
    if(file.Exists) return new FileResource(file, davRoot + GetCanonicalPath(fsRoot, file.FullName, false));

    return null;
  }

  static void SetEntryProperties(PropFindResource resource, XmlQualifiedName property, FileSystemInfo entry)
  {
    if(property == Names.displayname) resource.SetValue(property, entry.Name, null);
    else if(property == Names.creationdate) resource.SetValue(property, new DateTimeOffset(entry.CreationTime));
    else if(property == Names.getlastmodified) resource.SetValue(property, new DateTimeOffset(entry.LastWriteTime));
    else resource.SetError(property, ConditionCodes.NotFound);
  }
}
#endregion

#region DirectoryResource
sealed class DirectoryResource : WebDAVResource
{
  public DirectoryResource(DirectoryInfo info, string canonicalPath)
  {
    if(info == null || canonicalPath == null) throw new ArgumentNullException();
    Info           = info;
    _canonicalPath = canonicalPath;
  }

  public override string CanonicalPath
  {
    get { return _canonicalPath; }
  }

  public DirectoryInfo Info { get; private set; }

  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    HashSet<XmlQualifiedName> fileProperties = properties;

    if((request.Flags & PropFindFlags.IncludeAll) != 0)
    {
      properties.Add(Names.displayname);
      properties.Add(Names.resourcetype);
      properties.Add(Names.creationdate);
      properties.Add(Names.getlastmodified);

      fileProperties = new HashSet<XmlQualifiedName>(properties);
      fileProperties.Add(Names.getcontentlength);
    }

    FileSystemService.AddPropFindDirectory(request, properties, fileProperties, CanonicalPath, Info, 0);
  }

  string _canonicalPath;
}
#endregion

#region FileResource
sealed class FileResource : WebDAVResource
{
  public FileResource(FileInfo info, string canonicalPath)
  {
    if(info == null || canonicalPath == null) throw new ArgumentNullException();
    Info           = info;
    _canonicalPath = canonicalPath;
  }

  public override string CanonicalPath
  {
    get { return _canonicalPath; }
  }

  public FileInfo Info { get; private set; }

  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    if((request.Flags & PropFindFlags.IncludeAll) != 0)
    {
      properties.Add(Names.displayname);
      properties.Add(Names.resourcetype);
      properties.Add(Names.creationdate);
      properties.Add(Names.getlastmodified);
      properties.Add(Names.getcontentlength);
    }

    FileSystemService.AddPropFindFile(request, properties, CanonicalPath, Info);
  }

  string _canonicalPath;
}
#endregion

#region RootResource
sealed class RootResource : WebDAVResource
{
  public override string CanonicalPath
  {
    get { return ""; }
  }

  public override void PropFind(PropFindRequest request)
  {
    if(request == null) throw new ArgumentNullException();

    bool namesOnly = (request.Flags & PropFindFlags.NamesOnly) != 0;
    HashSet<XmlQualifiedName> properties = namesOnly ? new HashSet<XmlQualifiedName>() : new HashSet<XmlQualifiedName>(request.Properties);
    if((request.Flags & PropFindFlags.IncludeAll) != 0)
    {
      properties.Add(Names.displayname);
      properties.Add(Names.resourcetype);
    }

    PropFindResource rootResource = new PropFindResource(CanonicalPath);
    if(namesOnly)
    {
      rootResource.SetNames(properties);
    }
    else
    {
      foreach(XmlQualifiedName property in properties)
      {
        if(property == Names.displayname) rootResource.SetValue(property, "Root", null);
        else if(property == Names.resourcetype) rootResource.SetValue(property, ResourceType.Collection, null);
        else rootResource.SetError(property, ConditionCodes.NotFound);
      }
    }
    request.Resources.Add(rootResource);

    if(request.Depth == Depth.SelfAndChildren || request.Depth == Depth.SelfAndDescendants)
    {
      HashSet<XmlQualifiedName> fileProperties = properties;
      if((request.Flags & PropFindFlags.IncludeAll) != 0)
      {
        properties.Add(Names.creationdate);
        properties.Add(Names.getlastmodified);

        fileProperties = new HashSet<XmlQualifiedName>(properties);
        fileProperties.Add(Names.getcontentlength);
      }

      foreach(DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
      {
        FileSystemService.AddPropFindDirectory(request, properties, fileProperties, FileSystemService.GetDriveName(drive) + "/",
                                               drive.RootDirectory, 1);
      }
    }
  }

}
#endregion

} // namespace HiA.WebDAV 