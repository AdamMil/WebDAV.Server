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
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Xml;
using AdamMil.Utilities;
using AdamMil.Web;
using AdamMil.WebDAV.Server.Configuration;

// this file demonstrates how to implement a WebDAV service that serves files from a .zip file and supports the full range of WebDAV
// features, including creating, updating, and deleting files and directories, locking, strongly typed dead properties, partial GETs,
// partial PUTs, conditional requests, and the ability to copy and move data to and from other types of services. because it uses the
// System.IO.Compression.ZipArchive class, it requires .NET 4.5, unlike the rest of the WebDAV server, which only requires .NET 3.5.
//
// to serve data from zip files, you might add a location like the following to the WebDAV <locations> in your web.config file:
// <add match="/" type="AdamMil.WebDAV.Server.Examples.ZipFileService, AdamMil.WebDAV.Server.Examples"
//      path="D:/data/dav.zip" writable="true" resetOnError="false" />

namespace AdamMil.WebDAV.Server.Examples
{

#region ZipArchiveWithLengths
/// <summary>A <see cref="ZipArchive"/> that maintains the lengths of the entries within it.</summary>
sealed class ZipArchiveWithLengths : ZipArchive
{
  public ZipArchiveWithLengths(Stream stream, ZipArchiveMode mode) : base(stream, mode)
  {
    // unfortunately, ZipArchiveEntry.Length throws an exception if the stream has ever been opened in a writable archive,
    // which is frankly ridiculous. this means we have to maintain the length properties ourselves
    if(mode == ZipArchiveMode.Update) entryLengths = new Dictionary<ZipArchiveEntry, long>();
    IsReadOnly = mode == ZipArchiveMode.Read;
  }

  internal bool IsReadOnly { get; private set; }

  /// <summary>Removes the lengths of the entry at the given path and all descendants.</summary>
  internal void ClearEntryLengths(string path)
  {
    if(path.Length != 0 && !path.EndsWith('/'))
    {
      ZipArchiveEntry entry = GetEntry(path);
      if(entry != null) entryLengths.Remove(entry);
    }
    else
    {
      entryLengths.RemoveRange(entryLengths.Keys.Where(e => e.FullName.StartsWith(path, StringComparison.Ordinal)).ToList());
    }
  }

  /// <summary>Returns the length of the given entry.</summary>
  internal long GetEntryLength(ZipArchiveEntry entry)
  {
    long length;
    if(entryLengths == null || !entryLengths.TryGetValue(entry, out length)) length = entry.Length;
    return length;
  }

  /// <summary>Removes the length of the given entry.</summary>
  internal void RemoveEntryLength(ZipArchiveEntry entry)
  {
    entryLengths.Remove(entry);
  }

  /// <summary>Sets the length of the given entry.</summary>
  internal void SetEntryLength(ZipArchiveEntry entry, long length)
  {
    entryLengths[entry] = length;
  }

  readonly Dictionary<ZipArchiveEntry, long> entryLengths;
}
#endregion

#region ZipEntryResource
sealed class ZipEntryResource : WebDAVResource, IStandardResource<ZipEntryResource>
{
  internal ZipEntryResource(ZipArchiveEntry entry)
    : this((ZipArchiveWithLengths)entry.Archive, DAVUtility.CanonicalPathEncode(entry.FullName))
  {
    this.entry = entry;
  }

  internal ZipEntryResource(ZipArchiveWithLengths archive, string canonicalPath)
  {
    if(archive == null || canonicalPath == null) throw new ArgumentNullException();
    this.archive = archive;
    this.path    = canonicalPath;
  }

  public override string CanonicalPath
  {
    get { return path; }
  }

  public bool IsCollection
  {
    get { return path.Length == 0 || path.EndsWith('/'); } // the empty string is the root directory
  }

  public override void CopyOrMove(CopyOrMoveRequest request)
  {
    // this method is called when a COPY or MOVE request is made with this resource as the source. ProcessStandardRequest handles almost
    // all of the details. we just need to provide a method to delete a resource after a successful MOVE. if we want, though, we can
    // customize the process, for instance when a service can implement a COPY or MOVE more efficiently than the generic algorithm which
    // recursively and individually copies (and possibly deletes) all the descendants of the request resource. ZipArchive has no special
    // methods for moving or renaming items in the archive, so we can't do better than the generic algorithm
    if(request == null) throw new ArgumentNullException();
    if(archive.IsReadOnly && request.IsMove) request.Status = ConditionCodes.Forbidden; // disallow moving read-only resources
    else lock(archive) request.ProcessStandardRequest(this);
  }

  public override void Delete(DeleteRequest request)
  {
    // this method is called when the client issues a DELETE request. the ProcessStandardRequest override that we call is suitable for
    // resources that either have no children or can be reliably deleted recursively. if the deletion of a descendant might fail, the
    // ProcessStandardRequest<T>(T, Func<T,ConditionCode>) override, which works recursively on individual resources, should be used
    if(request == null) throw new ArgumentNullException();
    if(archive.IsReadOnly || string.IsNullOrEmpty(path)) request.Status = ConditionCodes.Forbidden; // the root can't be deleted
    else lock(archive) request.ProcessStandardRequest(this);
  }

  public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
  {
    // this method is called whenever the WebDAV server needs some basic metadata about the resource
    if(metadata == null) // cache the metadata so we don't have to compute it multiple times in a request (especially the entity tag)
    {
      metadata = new EntityMetadata();
      if(!IsCollection)
      {
        metadata.Length    = archive.GetEntryLength(entry);
        metadata.MediaType = MediaTypes.GuessMediaType(path);
      }
    }

    // if we need the entity tag but we haven't computed it yet, compute it now
    if(includeEntityTag && metadata.EntityTag == null && !IsCollection)
    {
      lock(archive)
      {
        using(Stream stream = OpenStream()) metadata.EntityTag = DAVUtility.ComputeEntityTag(stream);
      }
    }

    return metadata.Clone(); // clone the cached metadata to avoid giving out a reference to our internal state
  }

  public override void GetOrHead(GetOrHeadRequest request)
  {
    // this method is called when the client issues a GET or HEAD request to the resource. for most resources, the WriteStandardResponse
    // method is sufficient. it will send the entity body to the client if the resource has one. otherwise, for directories without entity
    // bodies, it will automatically generate an HTML index page listing the children of the request resource
    if(request == null) throw new ArgumentNullException();
    lock(archive) request.WriteStandardResponse(this);
  }

  public override void Lock(LockRequest request)
  {
    // this method is called when the client issues a LOCK request to the resource. as usual, ProcessStandardRequest does most of the work
    if(request == null) throw new ArgumentNullException();
    if(archive.IsReadOnly) base.Lock(request); // disallow locking (by calling the base class) for read-only resources
    else request.ProcessStandardRequest(LockType.WriteLocks, IsCollection);
  }

  public override void Options(OptionsRequest request)
  {
    // this method is called when the client issues an OPTIONS request to the resource. the defaults are suitable for a read-only resource
    if(request == null) throw new ArgumentNullException();
    if(!archive.IsReadOnly) // but if the resource is not read-only, report to the client that some additional methods are allowed
    {
      if(!string.IsNullOrEmpty(path)) request.AllowedMethods.Add(DAVMethods.Delete); // resources other than the root can be deleted
      if(!IsCollection) request.AllowedMethods.Add(DAVMethods.Put); // files can have their content replaced
      request.SupportsLocking = request.Context.LockManager != null; // support locking for writable resources if there's a lock manager
    }
  }

  public override void PropFind(PropFindRequest request)
  {
    // this method is called when the client issues a PROPFIND request to the resource. the ProcessStandardRequest method has overrides
    // for various scenarios, but this one, which is simple, works for both collection and non-collection resources, and still allows
    // expensive properties to be excluded unless specifically requested, is usually the best one to use
    if(request == null) throw new ArgumentNullException();
    lock(archive) request.ProcessStandardRequest(this, resource => resource.GetLiveProperties(request));
  }

  public override void Put(PutRequest request)
  {
    // this method is called when the client issues a PUT request to the resource. even though directories in .zip files can technically
    // have data streams, they're unlikely to work correctly with most clients so we'll disallow setting a directory's data stream
    if(request == null) throw new ArgumentNullException();
    if(archive.IsReadOnly || IsCollection) // if the resource is read-only or a directory...
    {
      base.Put(request); // call the base class to deny the request
    }
    else // otherwise, it's a writable file
    {
      lock(archive)
      {
        try
        {
          using(Stream stream = entry.Open())
          {
            long newLength = request.ProcessStandardRequest(stream);
            if(DAVUtility.IsSuccess(request.Status)) // if the request succeeded, send the ETag and Last-Modified headers to the client
            {
              entry.LastWriteTime = DateTime.Now; // update the entry's LastWriteTime, which isn't done automatically
              request.Context.Response.Headers[DAVHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
              request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(entry.LastWriteTime.UtcDateTime);
              archive.SetEntryLength(entry, newLength); // and update the stored entry length
            }
          }
        }
        catch(IOException ex) // the entry may have been deleted before we could take the lock
        {
          request.Status = ZipFileService.GetStatusFromException(ex);
        }
      }
    }
  }

  public override void Unlock(UnlockRequest request)
  {
    // this method is called when the client issues an UNLOCK request to the resource. ProcessStandardRequest does all the work
    if(request == null) throw new ArgumentNullException();
    if(archive.IsReadOnly) base.Unlock(request); // disallow locking (by calling the base class) for read-only resources
    else request.ProcessStandardRequest();
  }

  /// <summary>Deletes the resource and all its descendants from the archive. This method must be called with the archive locked.</summary>
  internal void Delete()
  {
    archive.ClearEntryLengths(path);
    foreach(ZipArchiveEntry entry in GetSelfAndDescendants(path)) entry.Delete();
  }

  /// <summary>Returns a collection containing the children of this resource.</summary>
  IEnumerable<ZipEntryResource> GetChildren()
  {
    if(!IsCollection) return null;

    // since we may have to infer the existence of child directories based on other entries, keep track of which ones we've seen
    HashSet<string> subdirectories = new HashSet<string>();
    List<ZipEntryResource> children = new List<ZipEntryResource>();
    lock(archive)
    {
      foreach(ZipArchiveEntry entry in archive.Entries)
      {
        // if the entry is a descendant of this resource...
        if(entry.FullName.Length > path.Length && entry.FullName.StartsWith(path, StringComparison.OrdinalIgnoreCase))
        {
          int start = path.Length == 0 ? 0 : path.Length+1, nextSlash = entry.FullName.IndexOf('/', start);
          if(nextSlash == -1) // if the entry is a child that's a file...
          {
            children.Add(new ZipEntryResource(entry)); // add it
          }
          else // the entry is a descendant that's either not a file or not a child 
          {
            // the entry must be a subdirectory or in a subdirectory, so get that subdirectory's path. if we haven't seen it yet, add it
            string subdirectory = entry.FullName.Substring(0, nextSlash+1);
            if(subdirectories.Add(subdirectory)) children.Add(new ZipEntryResource(archive, subdirectory));
          }
        }
      }
    }
    return children;
  }

  /// <summary>Returns a dictionary containing the live properties of this resource. Dead properties are handled automatically.</summary>
  Dictionary<XmlQualifiedName,object> GetLiveProperties(PropFindRequest request) // NOTE: request will be null for non-PROPFIND requests
  {
    Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
    properties[DAVNames.resourcetype] = IsCollection ? ResourceType.Collection : null; // null indicates a non-collection resource

    // add file-related properties if this is a file
    if(!IsCollection)
    {
      properties[DAVNames.getcontentlength] = archive.GetEntryLength(entry);
      properties[DAVNames.getlastmodified]  = entry.LastWriteTime;

      string mediaType = MediaTypes.GuessMediaType(entry.Name);
      if(mediaType != null) properties[DAVNames.getcontenttype] = mediaType;

      // we don't want to return the DAV:getetag property unless it's necessary, since it's expensive to compute
      if(request != null && request.MustIncludeProperty(DAVNames.getetag)) // if we must include it (but not necessarily its value)...
      {
        // we have to report the property, but depending on the NamesOnly property we may be able avoid computing its value
        properties[DAVNames.getetag] = request.NamesOnly ? null : GetEntityMetadata(true).EntityTag;
      }
    }

    // if we support locking and we're servicing a PROPFIND request (as opposed to COPY/MOVE, etc.), add lock-related properties
    if(!archive.IsReadOnly && request != null && request.Context.LockManager != null)
    {
      // here we want to include the lockdiscovery value unless it must not be returned. we'll use the MustExcludePropertyValue function
      // to replace the value with null whenever it's not needed
      properties[DAVNames.lockdiscovery] = request.MustExcludePropertyValue(DAVNames.lockdiscovery) ?
        null : request.Context.LockManager.GetLocks(CanonicalPath, LockSelection.SelfAndRecursiveAncestors, null);
      properties[DAVNames.supportedlock] = LockType.WriteLocks;
    }

    return properties;
  }

  string GetMemberName()
  {
    if(path.Length < 2) return path;
    int slash = path.LastIndexOf('/', path.Length-2); // find the slash that separates the resource name from its parent, if any
    return slash == -1 ? path : path.Substring(slash+1);
  }

  /// <summary>Returns a collection of all archive entries at or below the given path.</summary>
  IList<ZipArchiveEntry> GetSelfAndDescendants(string path)
  {
    path = DAVUtility.UriPathDecode(path);
    if(path.Length != 0 && !path.EndsWith('/'))
    {
      ZipArchiveEntry entry = archive.GetEntry(path);
      return entry == null ? new ZipArchiveEntry[0] : new ZipArchiveEntry[] { entry };
    }
    else
    {
      return archive.Entries.Where(e => e.FullName.StartsWith(path, StringComparison.Ordinal)).ToList();
    }
  }

  Stream OpenStream()
  {
    // once we open it we can no longer retrieve the length if the archive is writable, even if we only read. so record the length
    if(!archive.IsReadOnly) archive.SetEntryLength(entry, archive.GetEntryLength(entry));
    return entry.Open();
  }

  #region ISourceResource<T> Members
  ConditionCode IStandardResource.Delete()
  {
    if(archive.IsReadOnly) return ConditionCodes.Forbidden;
    lock(archive) Delete();
    return null;
  }

  IEnumerable<ZipEntryResource> IStandardResource<ZipEntryResource>.GetChildren(WebDAVContext context)
  {
    return GetChildren();
  }

  IDictionary<XmlQualifiedName, object> IStandardResource.GetLiveProperties(WebDAVContext context)
  {
    return GetLiveProperties(null);
  }

  string IStandardResource.GetMemberName(WebDAVContext context)
  {
    return GetMemberName();
  }

  Stream IStandardResource.OpenStream(WebDAVContext context)
  {
    return !IsCollection ? OpenStream() : null;
  }
  #endregion

  readonly ZipArchiveWithLengths archive;
  readonly string path;
  ZipArchiveEntry entry;
  EntityMetadata metadata;
}
#endregion

#region ZipFileService
/// <summary>A WebDAV service that serves files from a .zip file.</summary>
public class ZipFileService : WebDAVService, IDisposable
{
  /// <summary>Initializes a new <see cref="ZipFileService"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>The <see cref="ZipFileService"/> supports the following parameters:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>path</term>
  ///     <description>xs:string</description>
  ///     <description>The full path to the .zip file on disk.</description>
  ///   </item>
  ///   <item>
  ///     <term>writable</term>
  ///     <description>xs:boolean</description>
  ///     <description>Determines whether the WebDAV service allows the creation, deletion, and modification of items inside the .zip file.
  ///       The default is false.
  ///     </description>
  ///   </item>
  /// </list>
  /// </remarks>
  public ZipFileService(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();

    string value = parameters.TryGetValue("writable");
    isReadOnly = string.IsNullOrEmpty(value) || !XmlConvert.ToBoolean(value);

    value = parameters.TryGetValue("path");
    if(string.IsNullOrEmpty(value)) throw new ArgumentException("The path parameter is required.");
    Stream stream = null;
    try
    {
      Impersonation.RunWithImpersonation(Impersonation.RevertToSelf, false,
        () => { stream = isReadOnly ? File.OpenRead(value) : File.Open(value, FileMode.OpenOrCreate); });
      zipArchive = new ZipArchiveWithLengths(stream, isReadOnly ? ZipArchiveMode.Read : ZipArchiveMode.Update);
    }
    catch
    {
      Utility.Dispose(stream);
      throw;
    }
  }

  ~ZipFileService() { Dispose(false); }

  public override ConditionCode CopyResource(CopyOrMoveRequest request, string destinationPath, IStandardResource sourceResource)
  {
    // this method is called when the service is the destination of a COPY or MOVE request, in order to create a new file or directory at
    // the given location, potentially overwriting an existing resource there. this works for copies and moves between unrelated services
    // as well as copies and moves within the same service (if the resource doesn't have an optimized in-service copy/move routine)
    if(request == null || destinationPath == null || sourceResource == null) throw new ArgumentNullException();
    if(isReadOnly) return ConditionCodes.Forbidden;
    if(IsIllegalPath(destinationPath)) return ConditionCodes.BadPathCharacters;

    lock(zipArchive)
    {
      ConditionCode status;
      bool overwrote = false;
      try
      {
        IWebDAVResource resource = ResolveResource(request.Context, destinationPath);
        if(resource != null) // if a resource already exists at the destination path...
        {
          if(!request.Overwrite) return ConditionCodes.PreconditionFailed; // return 412 Precondition Failed if we can't overwrite it
          IStandardResource stdResource = resource as IStandardResource;
          if(stdResource == null) return ConditionCodes.Forbidden; // if it's not a standard resource, then we don't know how to delete it
          status = stdResource.Delete(); // otherwise, try to delete the existing resource
          if(!DAVUtility.IsSuccess(status)) return status;
          request.PostProcessOverwrite(stdResource.CanonicalPath); // delete its locks and dead properties
          overwrote = true; // and remember that we overwrote it
        }

        if(sourceResource.IsCollection) // if we should create a directory...
        {
          destinationPath = DAVUtility.WithTrailingSlash(destinationPath); // canonicalize the path, since we'll use it later
          status = CreateNewDirectory(request.Context, destinationPath);
        }
        else // otherwise, we should create a new file
        {
          status = CreateNewFile(request.Context, destinationPath, destStream => // the path is already canonical
          {
            using(Stream srcStream = sourceResource.OpenStream(request.Context))
            {
              if(srcStream != null) srcStream.CopyTo(destStream); // not all source resources have data streams, but copy it if it does
            }
            return null;
          });
        }
      }
      catch(IOException ex)
      {
        status = GetStatusFromException(ex);
      }

      if(DAVUtility.IsSuccess(status)) // if the copy was okay, do postprocessing of locks and properties
      {
        // preserve modification time when moving objects
        if(request.IsMove)
        {
          EntityMetadata metadata = sourceResource.GetEntityMetadata(false);
          if(metadata.LastModifiedTime.HasValue) // if the source resource has a last-modified time...
          {
            zipArchive.GetEntry(DAVUtility.UriPathDecode(destinationPath)).LastWriteTime = metadata.LastModifiedTime.Value;
          }
        }

        request.PostProcessCopy(sourceResource.CanonicalPath, destinationPath); // this method handles locks and properties
        status = overwrote ? ConditionCodes.NoContent : ConditionCodes.Created; // return 204 or 201 as RFC 4918 says we should
      }

      return status;
    }
  }

  public override void CreateAndLock(LockRequest request)
  {
    // this method is called when the client issues a LOCK request to an unmapped URL. the method should create a new, empty file
    // and lock it. the method must not create directories and should reject any request to create a directory
    if(request == null) throw new ArgumentNullException();
    if(isReadOnly) // if the service is read-only...
    {
      base.CreateAndLock(request); // call the default implementation, which denies the request
    }
    else
    {
      lock(zipArchive)
      {
        // the archive may have been changed on another thread, so resolve the URL again to make sure it's still unmapped
        IWebDAVResource resource = ResolveResource(request.Context, request.Context.RequestPath);
        if(resource != null) resource.Lock(request); // a resource was created on another thread, so let the resource handle the request
        else request.ProcessStandardRequest(LockType.WriteLocks, () => CreateNewFile(request.Context, null, null));
      }
    }
  }

  public override void MakeCollection(MkColRequest request)
  {
    // this method is called to service a MKCOL request to an unmapped URL, and should create a directory at the request path
    if(request == null) throw new ArgumentNullException();

    if(isReadOnly) // if the service is read-only...
    {
      base.MakeCollection(request); // call the default implementation, which denies the request
    }
    else
    {
      lock(zipArchive)
      {
        request.ProcessStandardRequest(() =>
        {
          // the archive may have been changed on another thread, so resolve the URL again to make sure it's still unmapped.
          // if a resource was created on another thread, reply with 405 Method Not Allowed as per RFC 4918 section 9.3.1
          if(ResolveResource(request.Context, request.Context.RequestPath) != null) return ConditionCodes.MethodNotAllowed;
          else CreateNewDirectory(request.Context, null);
          return null;
        });
      }
    }
  }

  public override void Options(OptionsRequest request)
  {
    // this method is called when the client makes an OPTIONS request to an unmapped URL (i.e. a URL that doesn't resolve to a resource).
    // the defaults are sufficient for a read-only service. if we're not read-only, we should inform the clients that some additional
    // HTTP methods are supported
    if(request == null) throw new ArgumentNullException();
    if(!isReadOnly)
    {
      // if request.IsServerQuery is true, then it's asking about the capabilities of the service /in general/, so we'll report that
      // the MKCOL and PUT methods are supported. otherwise, it's asking about a specific (non-existent) resource. we'll report MKCOL for
      // any unmapped URL (since a client can do MKCOL /dir or MKCOL /dir/), and PUT for any unmapped URL that doesn't end with a slash
      // (because a client shouldn't do PUT /file/)
      if(request.IsServerQuery || request.Context.RequestPath.Length != 0) request.AllowedMethods.Add(DAVMethods.MkCol);
      if(request.IsServerQuery || request.Context.RequestPath.Length != 0 && !request.Context.RequestPath.EndsWith('/'))
      {
        request.AllowedMethods.Add(DAVMethods.Put);
      }
      request.SupportsLocking = request.Context.LockManager != null;
    }
  }

  public override void Put(PutRequest request)
  {
    // this method is called when the client issues a PUT request to an unmapped URL. it should create a new file at the request path using
    // the request body as the file's content, and then return the ETag and Last-Modified headers to the client
    if(request == null) throw new ArgumentNullException();
    if(isReadOnly) // if the service is read-only...
    {
      base.Put(request); // call the default implementation, which denies the request
    }
    else
    {
      lock(zipArchive)
      {
        // the archive may have been changed on another thread, so resolve the URL again to make sure it's still unmapped
        IWebDAVResource resource = ResolveResource(request.Context, request.Context.RequestPath);
        if(resource != null) resource.Put(request); // a resource was created on another thread, so let the resource handle the request
        else if(request.Context.RequestPath.EndsWith('/')) request.Status = ConditionCodes.MethodNotAllowed; // can't PUT to a directory
        else
        {
          // this is slightly complicated due to the way CreateNewFile works. even though CreateNewFile calls a delegate that returns
          // request.Status, we still have to set request.Status to the value of CreateNewFile because CreateNewFile can also return an
          // error without ever calling the delegate
          request.Status = CreateNewFile(request.Context, null, stream => // this delegate is called if an empty file was created
          {
            request.ProcessStandardRequest(delegate(out Stream s) { s = stream; return null; }); // try to set the body
            if(DAVUtility.IsSuccess(request.Status)) // if the request succeeded, send the ETag and Last-Modified headers to the client
            {
              ZipArchiveEntry entry = zipArchive.GetEntry(DAVUtility.UriPathDecode(request.Context.RequestPath));
              request.Context.Response.Headers[DAVHeaders.ETag] = DAVUtility.ComputeEntityTag(stream, true).ToHeaderString();
              request.Context.Response.Headers[DAVHeaders.LastModified] = DAVUtility.GetHttpDateHeader(entry.LastWriteTime.UtcDateTime);
            }
            return request.Status;
          });
        }
      }
    }
  }

  public override IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath)
  {
    // this method is called to map a relative path to the corresponding resource. it returns null if the path is not mapped to a resource.
    // a .zip file doesn't contain an entry for every directory in the zip file. for instance, it might only contain a single entry with
    // the path /dir/subdir/file.txt, where the existence of /dir/ and /dir/subdir/ are implied. so if no entry exists, we may have to
    // return a resource that represents a directory whose existence was inferred
    if(IsIllegalPath(resourcePath)) return null; // prevent illegal paths from being obscured by UrlDecode
    string zipPath = DAVUtility.UriPathDecode(resourcePath); // get the path to look for in the .zip file
    lock(zipArchive)
    {
      ZipArchiveEntry entry = zipArchive.GetEntry(zipPath);
      // if the path looked like a file and we couldn't find it, try appending a slash to see if it's actually a directory
      if(entry == null && zipPath.Length != 0 && !zipPath.EndsWith('/'))
      {
        resourcePath += "/"; // modify both paths since later code assumes that they represent the path to a directory
        zipPath      += "/";
        entry         = zipArchive.GetEntry(zipPath);
      }
      if(entry != null) return new ZipEntryResource(entry);
    }

    // we couldn't find the resource path exactly. the above code ensured that the paths looks like a directory now, so return a virtual
    // resource if any entry in the .zip file starts with the named directory (i.e. if we can infer the existence of the directory
    // from the existing entries)
    if(zipArchive.Entries.Any(e => e.FullName.StartsWith(zipPath))) return new ZipEntryResource(zipArchive, resourcePath);

    return null; // we couldn't find or infer any matching entry in the .zip file, so return null
  }

  protected void Dispose(bool manuallyDisposing)
  {
    Utility.Dispose(ref zipArchive);
  }

  internal static ConditionCode GetStatusFromException(Exception ex)
  {
    // return a ConditionCode representing an error from an otherwise unhandled exception. call FilterErrorMessage to exclude the message
    // when showSensitiveErrors is false in the web server configuration, to avoid disclosing potentially sensitive information
    return new ConditionCode(HttpStatusCode.InternalServerError, WebDAVModule.FilterErrorMessage(ex.Message));
  }

  /// <summary>Creates a new directory and returns a <see cref="ConditionCode"/> indicating whether the attempt was successful.</summary>
  /// <remarks>This method must be called while the archive is locked.</remarks>
  ConditionCode CreateNewDirectory(WebDAVContext context, string path)
  {
    path = DAVUtility.WithTrailingSlash(path ?? context.RequestPath); // directories in .zip files end with a slash
    ConditionCode status = ValidateNewPath(context, path);
    if(status == null) zipArchive.CreateEntry(DAVUtility.UriPathDecode(path), CompressionLevel.NoCompression);
    return status;
  }

  /// <summary>Creates a new file and calls <paramref name="processStream"/> on the file's stream. Returns a <see cref="ConditionCode"/>
  /// indicating whether the attempt was successful.
  /// </summary>
  /// <remarks>This method must be called while the archive is locked.</remarks>
  ConditionCode CreateNewFile(WebDAVContext context, string path, Func<Stream,ConditionCode> processStream)
  {
    if(path == null) path = context.RequestPath;
    if(path.EndsWith('/')) return ConditionCodes.Forbidden; // can't create a directory

    ConditionCode status = ValidateNewPath(context, path);
    if(status != null) return status;

    ZipArchiveEntry entry = zipArchive.CreateEntry(DAVUtility.UriPathDecode(path));
    try
    {
      if(processStream != null)
      {
        using(Stream stream = entry.Open())
        {
          status = processStream(stream);
          zipArchive.SetEntryLength(entry, stream.Length);
        }
      }
    }
    catch(IOException ex)
    {
      status = GetStatusFromException(ex);
    }

    // if an error occurred, delete the new entry
    if(status != null && status.IsError)
    {
      entry.Delete();
      zipArchive.RemoveEntryLength(entry);
    }

    return status;
  }

  /// <summary>Provides common validation for file and directory paths and returns a <see cref="ConditionCode"/> indicating whether the
  /// path appears legal.
  /// </summary>
  ConditionCode ValidateNewPath(WebDAVContext context, string path)
  {
    // disallow the creation of paths with illegal characters
    if(IsIllegalPath(path)) return ConditionCodes.BadPathCharacters;

    // the parent directory must exist, according to RFC 4918, so verify that it does
    int lastSlash = path.Length < 2 ? -1 : path.LastIndexOf('/', path.Length-2); // find the slash before the parent directory
    if(lastSlash != -1)
    {
      ZipEntryResource resource = ResolveResource(context, path.Substring(0, lastSlash+1)) as ZipEntryResource;
      if(resource == null) return ConditionCodes.Conflict; // the parent must exist
      else if(!resource.IsCollection) return ConditionCodes.Forbidden; // can't create a directory under a file
    }

    return null;
  }

  void IDisposable.Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ZipArchiveWithLengths zipArchive;
  readonly bool isReadOnly;

  static bool IsIllegalPath(string path)
  {
    // disallow embedded slash characters. the paths from the WebDAV server should be minimally escaped, meaning that they can only contain
    // encoded slashes and percent signs. we'll allow embedded percent signs but disallow embedded slashes, since slashes are used to
    // separate directories in .zip files but percent signs have no special meaning
    return path.Contains("%2F", StringComparison.OrdinalIgnoreCase);
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server.Examples