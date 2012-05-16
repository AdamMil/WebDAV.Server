using System;
using System.Collections.Generic;
using System.Xml;

namespace HiA.WebDAV
{

// TODO: clean this up and make it public so others can use the same response-generation tools we do
#region Names
/// <summary>Contains the names of XML elements, attributes, etc. commonly used in generating WebDAV responses.</summary>
public static class Names
{
  /* Namespaces */
  /// <summary>The <c>DAV:</c> namespace, in which all standard WebDAV names are defined.</summary>
  public static readonly string DAV = "DAV:";
  /// <summary>The <c>http://www.w3.org/2001/XMLSchema</c> namespace, in which data type names (among other things) are defined.</summary>
  public static readonly string XmlSchema = "http://www.w3.org/2001/XMLSchema";
  /// <summary>The <c>http://www.w3.org/2001/XMLSchema-instance</c> namespace, in which the <c>xsi:type</c> name (among other things) is
  /// defined.
  /// </summary>
  internal static readonly string XmlSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

  /* WebDAV Element Names */
  /// <summary>The <c>DAV:allprop</c> element, used within <c>PROPFIND</c> requests to indicate that all elements are desired.</summary>
  internal static readonly XmlQualifiedName allprop = new XmlQualifiedName("allprop", DAV);
  /// <summary>The <c>DAV:collection</c> element, used within <see cref="resourcetype"/> to identify a collection resource.</summary>
  internal static readonly XmlQualifiedName collection = new XmlQualifiedName("collection", DAV);
  /// <summary>The <c>DAV:error</c> element, used to return additional information about errors.</summary>
  public static readonly XmlQualifiedName error = new XmlQualifiedName("error", DAV);
  internal static readonly XmlQualifiedName href = new XmlQualifiedName("href", DAV);
  internal static readonly XmlQualifiedName include = new XmlQualifiedName("include", DAV);
  internal static readonly XmlQualifiedName multistatus = new XmlQualifiedName("multistatus", DAV);
  internal static readonly XmlQualifiedName prop = new XmlQualifiedName("prop", DAV);
  internal static readonly XmlQualifiedName propertyupdate = new XmlQualifiedName("propertyupdate", DAV);
  internal static readonly XmlQualifiedName propfind = new XmlQualifiedName("propfind", DAV);
  internal static readonly XmlQualifiedName propname = new XmlQualifiedName("propname", DAV);
  internal static readonly XmlQualifiedName propstat = new XmlQualifiedName("propstat", DAV);
  internal static readonly XmlQualifiedName remove = new XmlQualifiedName("remove", DAV);
  internal static readonly XmlQualifiedName response = new XmlQualifiedName("response", DAV);
  internal static readonly XmlQualifiedName responsedescription = new XmlQualifiedName("responsedescription", DAV);
  internal static readonly XmlQualifiedName set = new XmlQualifiedName("set", DAV);
  internal static readonly XmlQualifiedName status = new XmlQualifiedName("status", DAV);

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
  /// the resource was created. The value must be in the format defined in section 14.12 of RFC 2616 (sample: <c>en, es</c>). The property
  /// should not be protected and must be defined on any resource that returns a <c>Content-Language</c> header in response to a <c>GET</c>
  /// request.
  /// </summary>
  public static readonly XmlQualifiedName getcontentlanguage = new XmlQualifiedName("getcontentlanguage", DAV);
  /// <summary>The <c>DAV:getcontentlength</c> property, which is an unsigned integer (i.e. <see cref="uint"/> or <see cref="ulong"/>)
  /// property that contains the value of the length of the resource's content. The property should be protected and must be defined on
  /// any DAV-compliant resource that returns a <c>Content-Length</c> header in response to a <c>GET</c> request.
  /// </summary>
  public static readonly XmlQualifiedName getcontentlength = new XmlQualifiedName("getcontentlength", DAV);
  /// <summary>The <c>DAV:getcontenttype</c> property, which is a <see cref="string"/> property that contains the media type of the
  /// resource. The value must be in the format defined in section 14.17 of RFC 2616. The property may be protected and must be defined on
  /// any DAV-compliant resource that returns a <c>Content-Type</c> header in response to a <c>GET</c> request.
  /// </summary>
  public static readonly XmlQualifiedName getcontenttype = new XmlQualifiedName("getcontenttype", DAV);
  /// <summary>The <c>DAV:getetag</c> property, which is an <see cref="EntityTag"/> property that represents the state of a resource's
  /// content. (See the description of entity tags in RFC 2616 for more details.) The property must be protected and must be defined on any
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
  internal static readonly XmlQualifiedName xsiType = new XmlQualifiedName("type", XmlSchemaInstance);

  /* Other Names */
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

  public static IEnumerable<XmlElement> EnumerateElements(this XmlNode node)
  {
    if(node == null) throw new ArgumentNullException();
    foreach(XmlNode child in node.ChildNodes)
    {
      child.AssertElement();
      yield return (XmlElement)child;
    }
  }

  public static XmlElement GetChild(this XmlElement element, XmlQualifiedName qname)
  {
    foreach(XmlNode node in element.ChildNodes)
    {
      if(node.NodeType == XmlNodeType.Element && node.HasName(qname)) return (XmlElement)node;
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
}
#endregion

} // namespace HiA.WebDAV
