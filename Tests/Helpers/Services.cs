using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using AdamMil.IO;
using AdamMil.WebDAV.Server;
using AdamMil.WebDAV.Server.Configuration;

namespace AdamMil.WebDAV.Server.Tests.Helpers
{
  #region TestFileSystemService
  public sealed class TestFileSystemService : FileSystemService
  {
    public TestFileSystemService(ParameterCollection parameters) : base(parameters) { }

    public override bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
    {
      return string.Equals(context.CurrentUserId, lockObject.OwnerId, System.StringComparison.Ordinal) || context.CurrentUserId == "admin";
    }

    public override string GetCurrentUserId(WebDAVContext context)
    {
      string userId = context.Request.Headers["UserId"];
      return string.IsNullOrEmpty(userId) ? null : userId;
    }
  }
  #endregion

  #region TestMemoryService
  public sealed class TestMemoryService : WebDAVService
  {
    public override ConditionCode CopyResource<T>(CopyOrMoveRequest request, string destinationPath, IStandardResource<T> sourceResource)
    {
      Resource resource = (Resource)ResolveResource(request.Context, destinationPath);
      if(resource != null && !request.Overwrite) return ConditionCodes.PreconditionFailed;
      // can't create or overwrite directories
      if(sourceResource.IsCollection || resource == null || resource.children != null) return ConditionCodes.Forbidden;
      using(Stream stream = sourceResource.OpenStream(request.Context)) resource.SetBody(stream ?? new MemoryStream());
      if(request.Context.PropertyStore != null && request.Destination.PropertyStore != null)
      {
        IEnumerable<XmlProperty> properties = request.Context.PropertyStore.GetProperties(sourceResource.CanonicalPath).Values;
        request.Destination.PropertyStore.SetProperties(resource.CanonicalPath, properties, true);
      }
      return ConditionCodes.NoContent;
    }

    public override string GetCanonicalPath(WebDAVContext context, string relativePath)
    {
      Resource resource = (Resource)ResolveResource(context, relativePath);
      return resource != null ? resource.CanonicalPath : relativePath;
    }

    public override void Options(OptionsRequest request)
    {
    }

    public override IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath)
    {
      return ResolveResource(rootResource, resourcePath.TrimEnd('/'));
    }

    #region Resource
    sealed class Resource : WebDAVResource, IStandardResource<Resource>
    {
      public Resource(string path, params Resource[] children)
      {
        int slash = path.LastIndexOf('/', path.Length-2);
        memberName     = path.Substring(slash+1);
        this.children  = children == null || children.Length == 0 ? null : children;
        _canonicalPath = this.children == null ? path : DAVUtility.WithTrailingSlash(path);
        body           = System.Text.Encoding.ASCII.GetBytes(memberName + " test");
      }

      public override string CanonicalPath
      {
	      get { return _canonicalPath; }
      }

      public override void CopyOrMove(CopyOrMoveRequest request)
      {
        request.ProcessStandardRequest(this);
      }

      public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
      {
        EntityMetadata metadata = new EntityMetadata();
        if(children == null) // if it's a file resource...
        {
          metadata.Length    = body.Length;
          metadata.MediaType = "text/plain";
          if(includeEntityTag) metadata.EntityTag = DAVUtility.ComputeEntityTag(GetStream());
        }
        return metadata;
      }

      public override void GetOrHead(GetOrHeadRequest request)
      {
        if(children == null) request.WriteStandardResponse(GetStream());
        else request.WriteSimpleIndexHtml(children.Select(c => new GetOrHeadRequest.IndexItem(c.memberName, c.children != null)));
      }

      public override void PropFind(PropFindRequest request)
      {
        request.ProcessStandardRequest(this);
      }

      internal void SetBody(Stream stream)
      {
        body = stream.ReadToEnd();
      }

      Dictionary<XmlQualifiedName, object> GetLiveProperties()
      {
        Dictionary<XmlQualifiedName, object> properties = new Dictionary<XmlQualifiedName, object>();
        properties[DAVNames.displayname]  = memberName;
        properties[DAVNames.resourcetype] = children == null ? null : DAVNames.collection;
        if(children == null)
        {
          EntityMetadata metadata = GetEntityMetadata(true);
          properties[DAVNames.getcontentlength] = metadata.Length.Value;
          properties[DAVNames.getcontenttype]   = metadata.MediaType;
          properties[DAVNames.getetag]          = metadata.EntityTag;
        }
        return properties;
      }

      MemoryStream GetStream()
      {
        return children == null ? new MemoryStream(body) : null;
      }

      internal readonly Resource[] children;
      readonly string _canonicalPath, memberName;
      byte[] body;

      #region ISourceResource Members
      bool IStandardResource<Resource>.IsCollection
      {
        get { return children != null; }
      }

      IEnumerable<Resource> IStandardResource<Resource>.GetChildren(WebDAVContext context)
      {
        return children;
      }

      IDictionary<XmlQualifiedName, object> IStandardResource<Resource>.GetLiveProperties(WebDAVContext context)
      {
        return GetLiveProperties();
      }

      string IStandardResource<Resource>.GetMemberName(WebDAVContext context)
      {
        return memberName;
      }

      Stream IStandardResource<Resource>.OpenStream(WebDAVContext context)
      {
        return GetStream();
      }
      #endregion
    }
    #endregion

    static Resource ResolveResource(Resource resource, string resourcePath)
    {
      if(resource.CanonicalPath.TrimEnd('/') == resourcePath) return resource;
      if(resource.children != null)
      {
        foreach(Resource child in resource.children)
        {
          Resource res = ResolveResource(child, resourcePath);
          if(res != null) return res;
        }
      }
      return null;
    }

    static readonly Resource rootResource =
      new Resource("", new Resource("dir1", new Resource("dir1/file1"), new Resource("dir1/file2")),
                       new Resource("dir2", new Resource("dir2/file1"), new Resource("dir2/file2")),
                       new Resource("file"));
  }
  #endregion
}