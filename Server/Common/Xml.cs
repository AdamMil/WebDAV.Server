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
using System.Xml;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

#region DAVNames
/// <summary>Contains the names of XML elements, attributes, etc. commonly used in generating WebDAV responses.</summary>
public static class DAVNames
{
  /* Namespaces */
  /// <summary>The <c>DAV:</c> namespace, in which all standard WebDAV names are defined.</summary>
  public static readonly string DAV = "DAV:";
  /// <summary>The <c>http://microsoft.com/wsdl/types/</c> namespace, used for additional data types such as <c>guid</c>.</summary>
  public static readonly string MSWSDLTypes = "http://microsoft.com/wsdl/types/";
  /// <summary>The <c>http://www.w3.org/XML/1998/namespace</c> namespace, which is bound by definition to the <c>xml:</c> prefix.</summary>
  public static readonly string Xml = "http://www.w3.org/XML/1998/namespace";
  /// <summary>The <c>http://www.w3.org/2001/XMLSchema</c> namespace, conventionally bound to the <c>xs:</c> prefix.</summary>
  public static readonly string XmlSchema = "http://www.w3.org/2001/XMLSchema";
  /// <summary>The <c>http://www.w3.org/2001/XMLSchema-instance</c> namespace, conventionally bound to the <c>xsi:</c> prefix.</summary>
  public static readonly string XmlSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

  /* WebDAV Element Names */
  /// <summary>The <c>DAV:activelock</c> element, used to describe a lock on a resource.</summary>
  public static readonly XmlQualifiedName activelock = new XmlQualifiedName("activelock", DAV);
  /// <summary>The <c>DAV:allprop</c> element, used within <c>PROPFIND</c> requests to indicate that all elements are desired.</summary>
  public static readonly XmlQualifiedName allprop = new XmlQualifiedName("allprop", DAV);
  /// <summary>The <c>DAV:collection</c> element, used within <see cref="resourcetype"/> to identify a collection resource.</summary>
  public static readonly XmlQualifiedName collection = new XmlQualifiedName("collection", DAV);
  /// <summary>The <c>DAV:depth</c> element, used to represent a depth value (e.g. the value of the <c>Depth</c> header) in XML.</summary>
  public static readonly XmlQualifiedName depth = new XmlQualifiedName("depth", DAV);
  /// <summary>The <c>DAV:error</c> element, used to return additional information about errors.</summary>
  public static readonly XmlQualifiedName error = new XmlQualifiedName("error", DAV);
  /// <summary>The <c>DAV:href</c> element, used to represent URIs in various situations.</summary>
  public static readonly XmlQualifiedName href = new XmlQualifiedName("href", DAV);
  /// <summary>The <c>DAV:exclusive</c> element, used to describe exclusive locks.</summary>
  public static readonly XmlQualifiedName exclusive = new XmlQualifiedName("exclusive", DAV);
  /// <summary>The <c>DAV:include</c> element, used to describe properties that should be included in a <c>PROPFIND</c> request.</summary>
  public static readonly XmlQualifiedName include = new XmlQualifiedName("include", DAV);
  /// <summary>The <c>DAV:lockentry</c> element, used to describe a lock type.</summary>
  public static readonly XmlQualifiedName lockentry = new XmlQualifiedName("lockentry", DAV);
  /// <summary>The <c>DAV:lockinfo</c> element, used to in a <c>LOCK</c> request to describe the desired lock.</summary>
  public static readonly XmlQualifiedName lockinfo = new XmlQualifiedName("lockinfo", DAV);
  /// <summary>The <c>DAV:lockroot</c> element, used to contain the root URL of a lock.</summary>
  public static readonly XmlQualifiedName lockroot = new XmlQualifiedName("lockroot", DAV);
  /// <summary>The <c>DAV:lockscope</c> element, used to describe whether a lock is shared or exclusive.</summary>
  public static readonly XmlQualifiedName lockscope = new XmlQualifiedName("lockscope", DAV);
  /// <summary>The <c>DAV:locktoken</c> element, used to contain a lock token.</summary>
  public static readonly XmlQualifiedName locktoken = new XmlQualifiedName("locktoken", DAV);
  /// <summary>The <c>DAV:locktype</c> element, used to describe the access type of a lock, such as <c>DAV:write</c>.</summary>
  public static readonly XmlQualifiedName locktype = new XmlQualifiedName("locktype", DAV);
  /// <summary>The <c>DAV:multistatus</c> element, used to return the status of multiple resources or operations to the client.</summary>
  public static readonly XmlQualifiedName multistatus = new XmlQualifiedName("multistatus", DAV);
  /// <summary>The <c>DAV:owner</c> element, used to hold information about the owner of a lock.</summary>
  public static readonly XmlQualifiedName owner = new XmlQualifiedName("owner", DAV);
  /// <summary>The <c>DAV:prop</c> element, used to contain a property value.</summary>
  public static readonly XmlQualifiedName prop = new XmlQualifiedName("prop", DAV);
  /// <summary>The <c>DAV:propertyupdate</c> element, used to describe a set of property changes in a <c>PROPPATCH</c> request.</summary>
  public static readonly XmlQualifiedName propertyupdate = new XmlQualifiedName("propertyupdate", DAV);
  /// <summary>The <c>DAV:propfind</c> element, used to describe the desired property data in a <c>PROPFIND</c> request.</summary>
  public static readonly XmlQualifiedName propfind = new XmlQualifiedName("propfind", DAV);
  /// <summary>The <c>DAV:propname</c> element, used to indicate that only property names are desired in a <c>PROPFIND</c> request.</summary>
  public static readonly XmlQualifiedName propname = new XmlQualifiedName("propname", DAV);
  /// <summary>The <c>DAV:propstat</c> element, used to describe the status of a property value in a <c>PROPFIND</c> or <c>PROPPATCh</c>
  /// request.
  /// </summary>
  public static readonly XmlQualifiedName propstat = new XmlQualifiedName("propstat", DAV);
  /// <summary>The <c>DAV:remove</c> element, used to describe the properties to remove from a resource.</summary>
  public static readonly XmlQualifiedName remove = new XmlQualifiedName("remove", DAV);
  /// <summary>The <c>DAV:response</c> element, used to describe one of the responses in a multi-status response.</summary>
  public static readonly XmlQualifiedName response = new XmlQualifiedName("response", DAV);
  /// <summary>The <c>DAV:responsedescription</c> element, used to provide user-readable data in a <c>DAV:response</c>.</summary>
  public static readonly XmlQualifiedName responsedescription = new XmlQualifiedName("responsedescription", DAV);
  /// <summary>The <c>DAV:set</c> element, used to describe properties that should be set in a <c>PROPPATCH</c> request.</summary>
  public static readonly XmlQualifiedName set = new XmlQualifiedName("set", DAV);
  /// <summary>The <c>DAV:shared</c> element, used to describe shared locks.</summary>
  public static readonly XmlQualifiedName shared = new XmlQualifiedName("shared", DAV);
  /// <summary>The <c>DAV:status</c> element, used to provide machine-readable data in a <c>DAV:response</c>.</summary>
  public static readonly XmlQualifiedName status = new XmlQualifiedName("status", DAV);
  /// <summary>The <c>DAV:timeout</c> element, used to describe the number of seconds until a lock expires.</summary>
  public static readonly XmlQualifiedName timeout = new XmlQualifiedName("timeout", DAV);
  /// <summary>The <c>DAV:write</c> element, representing a write lock.</summary>
  public static readonly XmlQualifiedName write = new XmlQualifiedName("write", DAV);

  /* WebDAV Property Names */
  /// <summary>The <c>DAV:creationdate</c> property, which is a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> property that
  /// records the time and date that a resource was created. The property may be protected and should be defined on all DAV-compliant
  /// resources (assuming they have creation dates). The property should be preserved in <c>MOVE</c> operations, but not <c>COPY</c>
  /// operations.
  /// </summary>
  public static readonly XmlQualifiedName creationdate = new XmlQualifiedName("creationdate", DAV);
  /// <summary>The <c>DAV:displayname</c> property, which is a <see cref="string"/> property that provides a resource name suitable for
  /// presentation to the user. The property should not be protected and should have the same value regardless of the URI used to access
  /// the resource.
  /// </summary>
  public static readonly XmlQualifiedName displayname = new XmlQualifiedName("displayname", DAV);
  /// <summary>The <c>DAV:getcontentlanguage</c> property, which is a <see cref="string"/> property that describes the language(s) in which
  /// the resource was created. The value must be in the format defined in section 3.1.3.2 of RFC 7231 (sample: <c>en, es</c>). The
  /// property should not be protected and must be defined on any resource that returns a <c>Content-Language</c> header in response to a
  /// <c>GET</c> request.
  /// </summary>
  public static readonly XmlQualifiedName getcontentlanguage = new XmlQualifiedName("getcontentlanguage", DAV);
  /// <summary>The <c>DAV:getcontentlength</c> property, which is an unsigned integer (i.e. <see cref="uint"/> or <see cref="ulong"/>)
  /// property that contains the value of the length of the resource's content. The property should be protected and must be defined on
  /// any DAV-compliant resource that returns a <c>Content-Length</c> header in response to a <c>GET</c> request.
  /// </summary>
  public static readonly XmlQualifiedName getcontentlength = new XmlQualifiedName("getcontentlength", DAV);
  /// <summary>The <c>DAV:getcontenttype</c> property, which is a <see cref="string"/> property that contains the media type of the
  /// resource. The value must be in the format defined in section 3.1.1.5 of RFC 7231. The property may be protected and must be defined
  /// on any DAV-compliant resource that returns a <c>Content-Type</c> header in response to a <c>GET</c> request.
  /// </summary>
  public static readonly XmlQualifiedName getcontenttype = new XmlQualifiedName("getcontenttype", DAV);
  /// <summary>The <c>DAV:getetag</c> property, which is an <see cref="EntityTag"/> property that represents the state of a resource's
  /// content. (See the description of entity tags in RFC 7232 for more details.) The property must be protected and must be defined on any
  /// DAV-compliant resource that returns an <c>ETag</c> header in response to any request.
  /// </summary>
  public static readonly XmlQualifiedName getetag = new XmlQualifiedName("getetag", DAV);
  /// <summary>The <c>DAV:getlastmodified</c> property, which is a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> property that
  /// records the time and date that a resource was last modified. The property should be protected and should reflect changes to the
  /// content of a resource; mere property changes should not affect the modification time. The property must be defined on any
  /// DAV-compliant resource that returns a <c>Last-Modified</c> header in response to a <c>GET</c> request. In response to a <c>COPY</c>
  /// or <c>MOVE</c> request, the value of the modification time at the destination should be updated only if it would cause the content
  /// to change (i.e. if the destination resource didn't already have the same content).
  /// </summary>
  public static readonly XmlQualifiedName getlastmodified = new XmlQualifiedName("getlastmodified", DAV);
  /// <summary>The <c>DAV:lockdiscovery</c> property, which has a value of <see cref="ActiveLock"/> or <see cref="IEnumerable{T}"/> of
  /// <see cref="ActiveLock"/> (or subclasses of <see cref="ActiveLock"/>), and which describes the active locks on a resource. The
  /// property must be protected, and is not lockable with respect to write locks. The property should not necessarily be copied in a
  /// <c>COPY</c> or <c>MOVE</c> request, since locks are not copied or moved along with the resource. If the resource is copied or moved
  /// into or out of a lock scope, the property value would change. If the server supports locks, but a resource does not currently have
  /// any locks, the resource should still expose this property.
  /// </summary>
  public static readonly XmlQualifiedName lockdiscovery = new XmlQualifiedName("lockdiscovery", DAV);
  /// <summary>The <c>DAV:resourcetype</c> property, which has a value of <see cref="ResourceType"/> or <see cref="IEnumerable{T}"/> of
  /// <see cref="ResourceType"/> (or subclasses of <see cref="ResourceType"/>), and which describes the nature of a resource with respect
  /// to WebDAV. (Examples are collection resources, represented by <see cref="ResourceType.Collection"/>.) The property should be
  /// protected and must be defined on all DAV-compliant resources.
  /// </summary>
  public static readonly XmlQualifiedName resourcetype = new XmlQualifiedName("resourcetype", DAV);
  /// <summary>The <c>DAV:supportedlock</c> property, which has a value of <see cref="LockType"/> or <see cref="IEnumerable{T}"/> of
  /// <see cref="LockType"/> (or subclasses of <see cref="LockType"/>), and which describes the lock types supported by a resource.
  /// The property must be protected. In response to a <c>PROPFIND</c> request, the server needn't return lock types that the user isn't
  /// authorized to use. The property is not lockable with respect to write locks.
  /// </summary>
  public static readonly XmlQualifiedName supportedlock = new XmlQualifiedName("supportedlock", DAV);

  /* Other Attribute Names */
  /// <summary>The <c>xml:lang</c> attribute, used to describe the language of XML content.</summary>
  public static readonly XmlQualifiedName xmlLang = new XmlQualifiedName("lang", Xml);
  /// <summary>The <c>xsi:type</c> attribute, used to describe the type of an element's content.</summary>
  public static readonly XmlQualifiedName xsiType = new XmlQualifiedName("type", XmlSchemaInstance);

  /* Type Names */
  /// <summary>The <c>http://microsoft.com/wsdl/types/:guid</c> type, which represents <see cref="Guid"/> data.</summary>
  public static readonly XmlQualifiedName msGuid = new XmlQualifiedName("guid", MSWSDLTypes);
  /// <summary>The <c>xs:boolean</c> type, which represents boolean data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsBoolean = new XmlQualifiedName("boolean", XmlSchema);
  /// <summary>The <c>xs:string</c> type, which represents text data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsString = new XmlQualifiedName("string", XmlSchema);
  /// <summary>The <c>xs:decimal</c> type, which represents general numeric data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsDecimal = new XmlQualifiedName("decimal", XmlSchema);
  /// <summary>The <c>xs:float</c> type, which represents IEEE754 single-precision floating point data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsFloat = new XmlQualifiedName("float", XmlSchema);
  /// <summary>The <c>xs:double</c> type, which represents IEEE754 double-precision floating point data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsDouble = new XmlQualifiedName("double", XmlSchema);
  /// <summary>The <c>xs:duration</c> type, which represents <see cref="XmlDuration"/> data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsDuration = new XmlQualifiedName("duration", XmlSchema);
  /// <summary>The <c>xs:dateTime</c> type, which represents <see cref="DateTime"/> or <see cref="DateTimeOffset"/>data, where <c>xs</c>
  /// refers to the <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsDateTime = new XmlQualifiedName("dateTime", XmlSchema);
  /// <summary>The <c>xs:date</c> type, which represents <see cref="DateTime"/> data having no time component, where <c>xs</c> refers to
  /// the <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsDate = new XmlQualifiedName("date", XmlSchema);
  /// <summary>The <c>xs:base64Binary</c> type, which represents binary data encoded as base64, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsB64Binary = new XmlQualifiedName("base64Binary", XmlSchema);
  /// <summary>The <c>xs:hexBinary</c> type, which represents binary data encoded as hexadecimal, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsHexBinary = new XmlQualifiedName("hexBinary", XmlSchema);
  /// <summary>The <c>xs:anyURI</c> type, which represents <see cref="Uri"/> data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsUri = new XmlQualifiedName("anyURI", XmlSchema);
  /// <summary>The <c>xs:int</c> type, which represents 32-bit signed integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsInt = new XmlQualifiedName("int", XmlSchema);
  /// <summary>The <c>xs:long</c> type, which represents 64-bit signed integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsLong = new XmlQualifiedName("long", XmlSchema);
  /// <summary>The <c>xs:QName</c> type, which represents a qualified node name, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsQName = new XmlQualifiedName("QName", XmlSchema);
  /// <summary>The <c>xs:short</c> type, which represents 16-bit signed integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsShort = new XmlQualifiedName("short", XmlSchema);
  /// <summary>The <c>xs:byte</c> type, which represents 8-bit signed integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsSByte = new XmlQualifiedName("byte", XmlSchema);
  /// <summary>The <c>xs:unsignedInt</c> type, which represents 32-bit unsigned integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsUInt = new XmlQualifiedName("unsignedInt", XmlSchema);
  /// <summary>The <c>xs:unsignedLong</c> type, which represents 64-bit unsigned integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsULong = new XmlQualifiedName("unsignedLong", XmlSchema);
  /// <summary>The <c>xs:unsignedShort</c> type, which represents 16-bit unsigned integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsUShort = new XmlQualifiedName("unsignedShort", XmlSchema);
  /// <summary>The <c>xs:unsignedByte</c> type, which represents 8-bit unsigned integer data, where <c>xs</c> refers to the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace.
  /// </summary>
  public static readonly XmlQualifiedName xsUByte = new XmlQualifiedName("unsignedByte", XmlSchema);
}
#endregion

#region MultiStatusResponse
/// <summary>Implements a wrapper around an <see cref="XmlWriter"/> that assists in the creation of 207 Multi-Status responses.</summary>
/// <seealso cref="WebDAVContext.OpenMultiStatusResponse"/>
public sealed class MultiStatusResponse : IDisposable
{
  internal MultiStatusResponse(WebDAVContext context, XmlWriter writer, HashSet<string> namespaces)
  {
    Writer = writer;

    // the WebDAV mini-redirector client used by Windows Explorer can't handle responses that use the default xml namespace. it requires
    // prefixes for the elements, so we must respond like <a:multistatus xmlns:a="DAV:"> rather than <multistatus xmlns="DAV:">
    string davPrefix = context.UseExplorerHacks() ? MakeNamespacePrefix() : null;

    // write the start element using the fully qualified name so the writer knows the namespace, since we haven't defined it yet with
    // an xmlns attribute. if we don't define the namespace, we'll get an error when we try to create the xmlns attribute
    writer.WriteStartElement(davPrefix, DAVNames.multistatus.Name, DAVNames.multistatus.Namespace);
    if(davPrefix == null) writer.WriteAttributeString("xmlns", DAVNames.DAV); // now add the xmlns attribute for the DAV: namespace
    else writer.WriteAttributeString("xmlns", davPrefix, null, DAVNames.DAV);

    if(namespaces != null)
    {
      // add xmlns attributes to define each of the other namespaces used within the response
      foreach(string ns in namespaces)
      {
        // select a prefix name for the namespace. XmlSchemaInstance and XmlSchema get their conventional prefixes xsi and xs. also, use
        // ms for the MS WSDL types. this isn't strictly necessary, but makes the output more readable and increases interoperability with
        // poorly written clients that make assumptions about namespace prefixes
        string prefix = ns.OrdinalEquals(DAVNames.DAV) ? null :
                        ns.OrdinalEquals(DAVNames.XmlSchemaInstance) ? "xsi" :
                        ns.OrdinalEquals(DAVNames.XmlSchema) ? "xs" : 
                        ns.OrdinalEquals(DAVNames.MSWSDLTypes) ? "ms" : MakeNamespacePrefix();
        if(prefix != null) writer.WriteAttributeString("xmlns", prefix, null, ns);
      }
    }
  }

  /// <summary>Gets the <see cref="XmlWriter"/> used to write XML into the response stream.</summary>
  public XmlWriter Writer { get; private set; }

  /// <summary>Writes the <c>&lt;status&gt;</c>, <c>&lt;error&gt;</c>, and/or <c>&lt;responsedescription&gt;</c> elements based on a
  /// <see cref="ConditionCode"/>.
  /// </summary>
  public void WriteStatus(ConditionCode code)
  {
    if(code == null) throw new ArgumentNullException();
    Writer.WriteElementString(DAVNames.status, code.DAVStatusText); // write the DAV:status element
    code.WriteErrorXml(Writer); // write the DAV:error element, if any
    if(!string.IsNullOrEmpty(code.Message)) Writer.WriteElementString(DAVNames.responsedescription, code.Message);
  }

  /// <summary>Finishes writing the multi-status response and disposes the underlying <see cref="XmlWriter"/>.</summary>
  public void Dispose()
  {
    if(!disposed)
    {
      Writer.WriteEndElement(); // close the <multistatus> tag
      Utility.Dispose(Writer);
      disposed = true;
    }
  }

  internal string MakeNamespacePrefix()
  {
    return MakeNamespacePrefix(namespaceNameCount++);
  }

  uint namespaceNameCount;
  bool disposed;

  /// <summary>Creates a unique namespace prefix for a non-negative integer.</summary>
  static string MakeNamespacePrefix(uint i)
  {
    const string letters = "abcdefghijklmnopqrstuvwxyz"; // first use letters a-z and then switch to names like ns26, ns27, etc.
    return i < letters.Length ? new string(letters[(int)i], 1) : "ns" + i.ToStringInvariant();
  }
}
#endregion

#region XmlExtensions
static class XmlExtensions
{
  public static void AssertElement(this XmlNode node)
  {
    if(node == null) throw new ArgumentNullException();
    if(node.NodeType != XmlNodeType.Element) throw Exceptions.BadRequest("Expected element but found " + node.NodeType.ToString());
  }

  public static void AssertName(this XmlNode node, XmlQualifiedName qname)
  {
    if(qname == null) throw new ArgumentNullException();
    node.AssertName(qname.Name, qname.Namespace);
  }

  public static void AssertName(this XmlNode node, string localName, string namespaceUri)
  {
    if(!node.HasName(localName, namespaceUri))
    {
      throw Exceptions.BadRequest("Expected to find " + namespaceUri + ":" + localName + ", but instead found " + node.NamespaceURI + ":" +
                                  node.LocalName);
    }
  }

  public static IEnumerable<XmlElement> EnumerateChildElements(this XmlNode node)
  {
    if(node == null) throw new ArgumentNullException();
    return EnumerateChildElementsCore(node); // put the generator in its own method so we can do argument validation eagerly
  }

  public static XmlElement GetChild(this XmlElement element, XmlQualifiedName qname)
  {
    for(XmlNode child = element.FirstChild; child != null; child = child.NextSibling)
    {
      if(child.NodeType == XmlNodeType.Element && child.HasName(qname)) return (XmlElement)child;
    }
    throw Exceptions.BadRequest("Expected to find a child element named " + qname.ToString() + " in " +
                                element.GetQualifiedName().ToString());
  }

  public static bool SetFlagOnce(this XmlNode node, XmlQualifiedName qname, ref bool flag)
  {
    if(node.HasName(qname))
    {
      if(flag) throw Exceptions.DuplicateElement(qname);
      flag = true;
      return true;
    }
    else
    {
      return false;
    }
  }

  static IEnumerable<XmlElement> EnumerateChildElementsCore(XmlNode node)
  {
    for(XmlNode child = node.FirstChild; child != null; child = child.NextSibling)
    {
      child.AssertElement();
      yield return (XmlElement)child;
    }
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
