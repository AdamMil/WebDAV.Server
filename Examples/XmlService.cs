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
using System.Xml;
using System.Xml.Schema;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.Utilities.Encodings;
using AdamMil.WebDAV.Server.Configuration;

// this file demonstrates how to implement a read-only WebDAV service that serves data from an XML file. it supports the full range of
// WebDAV features you'd expect from a read-only service, including strongly typed dead properties, partial GETs, conditional requests,
// and the ability to copy data to other types of services. the XML files are expected to conform to the schema given in XmlService.xsd.
// a sample XML file is available in XmlService.xml. this file is not as heavily commented as ZipFileService, so you may want to look there
// for comments about service implementation.
//
// to serve data from XML files, you might add a location like the following to the WebDAV <locations> in your web.config file:
// <add match="/" type="AdamMil.WebDAV.Server.Examples.XmlService, AdamMil.WebDAV.Server.Examples" path="D:/data/dav.xml" />

namespace AdamMil.WebDAV.Server.Examples
{

public class XmlService : WebDAVService
{
  public XmlService(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentNullException();
    string path = parameters.TryGetValue("path");
    if(string.IsNullOrEmpty(path)) throw new ArgumentException("The path parameter is required.");

    // load the XML and validate it against the schema. this service doesn't watch the file on disk and reload it if it changes, but
    // that would be a simple and straightforward addition
    XmlDocument doc = new XmlDocument();
    Stream schemaStream =
      System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(XmlService).Namespace + ".XmlService.xsd");
    doc.Schemas.Add(XmlSchema.Read(schemaStream, null));
    doc.Load(path);
    doc.Validate(null);

    resources = new Dictionary<string, XmlResource>();
    foreach(XmlResource resource in EnumerableExtensions.Flatten(new XmlResource(doc.DocumentElement, ""), r => r.children))
    {
      resources.Add(resource.CanonicalPath, resource);
    }
  }

  public override IWebDAVResource ResolveResource(WebDAVContext context, string resourcePath)
  {
    // paths with slashes embedded in the path segments can't match anything. (such paths are disallowed by our schema)
    if(resourcePath.Contains("%2F", StringComparison.OrdinalIgnoreCase)) return null;
    resourcePath = DAVUtility.UriPathDecode(resourcePath); // decode encoded percent signs (the only other character that might be encoded)
    XmlResource resource = resources.TryGetValue(resourcePath);
    // if we couldn't find it, see if appending a slash to the request path helps
    if(resource == null && !resourcePath.EndsWith('/')) resources.TryGetValue(resourcePath+"/", out resource);
    return resource;
  }

  #region XmlResource
  sealed class XmlResource : WebDAVResource, IStandardResource<XmlResource>
  {
    internal XmlResource(XmlElement element, string path)
    {
      XmlElement childElem = element.FirstChild as XmlElement;
      if(childElem != null && childElem.HasName(Properties))
      {
        xmlProps = childElem;
        childElem  = childElem.NextSibling as XmlElement;
      }

      if(childElem.HasName(Children))
      {
        children = new XmlResource[childElem.ChildNodes.Count];
        path = DAVUtility.WithTrailingSlash(path);
        for(int i=0; i<children.Length; i++)
        {
          XmlElement childResElem = (XmlElement)childElem.ChildNodes[i];
          children[i] = new XmlResource(childResElem, path + childResElem.GetAttribute("name"));
        }
      }
      else
      {
        data = childElem;
      }

      this.path = path;
    }

    public override string CanonicalPath
    {
      get { return path; }
    }

    public bool IsCollection
    {
      get { return children != null; }
    }

    public override void CopyOrMove(CopyOrMoveRequest request)
    {
      if(request == null) throw new ArgumentNullException();
      request.ProcessStandardRequest(this);
    }

    public override EntityMetadata GetEntityMetadata(bool includeEntityTag)
    {
      EntityMetadata local = metadata; // since XmlService shares resources between threads, rather than allocating them on each request,
      if(local == null)                // build up the metadata in a local variable to prevent threads from returning incomplete metadata.
      {                                // this lets us avoid locking
        local = new EntityMetadata();
        if(!IsCollection)
        {
          long length = 0;
          byte[] buffer = new byte[4096];
          using(Stream stream = OpenStream())
          {
            while(true)
            {
              int read = stream.Read(buffer, 0, buffer.Length);
              if(read == 0) break;
              length += read;
            }
          }
          local.Length    = length;
          local.MediaType = StringUtility.MakeNullIfEmpty(data.GetAttribute("mediaType"));
        }
        metadata = local;
      }

      if(includeEntityTag && local.EntityTag == null)
      {
        using(Stream stream = OpenStream()) local.EntityTag = DAVUtility.ComputeEntityTag(stream);
      }

      return local.Clone();
    }

    public override void GetOrHead(GetOrHeadRequest request)
    {
      if(request == null) throw new ArgumentNullException();
      request.WriteStandardResponse(this);
    }

    public override void PropFind(PropFindRequest request)
    {
      if(request == null) throw new ArgumentNullException();
      request.ProcessStandardRequest(this);
    }

    Stream OpenStream()
    {
      if(data == null) return null;
      Stream stream = new TextStream(data.InnerText ?? "");
      if(data.GetAttribute("encoding") == "base64") stream = new EncodedStream(stream, new Base64Decoder(), null);
      return stream;
    }

    #region IStandardResource<XmlResource> Members
    IEnumerable<XmlResource> IStandardResource<XmlResource>.GetChildren(WebDAVContext context)
    {
      return children;
    }

    ConditionCode IStandardResource.Delete()
    {
      return ConditionCodes.Forbidden;
    }

    IDictionary<XmlQualifiedName, object> IStandardResource.GetLiveProperties(WebDAVContext context)
    {
      var properties = new Dictionary<XmlQualifiedName, object>();
      properties[DAVNames.resourcetype] = IsCollection ? ResourceType.Collection : null;
      if(!IsCollection)
      {
        EntityMetadata metadata = GetEntityMetadata(true);
        properties[DAVNames.getcontentlength] = metadata.Length;
        properties[DAVNames.getetag]          = metadata.EntityTag;
        if(metadata.MediaType != null) properties[DAVNames.getcontenttype] = metadata.MediaType;
      }
      if(xmlProps != null)
      {
        foreach(XmlElement xmlProp in xmlProps.ChildNodes) properties[xmlProp.GetQualifiedName()] = xmlProp;
      }
      return properties;
    }

    string IStandardResource.GetMemberName(WebDAVContext context)
    {
      if(path.Length < 2) return path;
      int slash = path.LastIndexOf('/', path.Length-2);
      return slash == -1 ? path : path.Substring(slash+1);
    }

    Stream IStandardResource.OpenStream(WebDAVContext context)
    {
      return OpenStream();
    }
    #endregion

    internal readonly XmlResource[] children;
    readonly XmlElement data, xmlProps;
    readonly string path;
    EntityMetadata metadata;

    const string NS = "http://adammil.net/webdav.server.examples/xmlService";
    static readonly XmlQualifiedName Children = new XmlQualifiedName("children", NS);
    static readonly XmlQualifiedName Properties = new XmlQualifiedName("properties", NS);
  }
  #endregion

  readonly Dictionary<string, XmlResource> resources;
}

} // namespace AdamMil.WebDAV.Server.Examples
