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
using System.Linq;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;

namespace AdamMil.WebDAV.Server
{

#region IElementValue
/// <summary>Represents a value that can be rendered into XML. Property values that derive from this class will use it to render
/// themselves.
/// </summary>
public interface IElementValue
{
  /// <include file="documentation.xml" path="/DAV/IElementValue/GetNamespaces/node()" />
  IEnumerable<string> GetNamespaces();
  /// <include file="documentation.xml" path="/DAV/IElementValue/WriteValue/node()" />
  void WriteValue(XmlWriter writer, WebDAVContext context);
}
#endregion

#region ResourceType
/// <summary>Represents a resource type for use with the <c>DAV:resourcetype</c> property.</summary>
/// <remarks>You can derive new resource types from this class if you need to create resource types that contain more than a simple
/// element name.
/// </remarks>
public class ResourceType : IElementValue
{
  /// <summary>Initializes a new <see cref="ResourceType"/> based on the name of an element to render within the <c>DAV:resourcetype</c>
  /// property value.
  /// </summary>
  public ResourceType(XmlQualifiedName qname)
  {
    if(qname == null) throw new ArgumentNullException();
    if(qname.IsEmpty) throw new ArgumentException("The name cannot be empty.");
    Name = qname;
  }

  /// <summary>Returns the XML namespaces used by the resource type.</summary>
  /// <remarks>The default implementation returns the namespace of <see cref="Name"/>.</remarks>
  public virtual IEnumerable<string> GetNamespaces()
  {
    return new string[] { Name.Namespace };
  }

  /// <summary>Writes the resource type XML. The XML namespaces needed by the value will have already been added to an enclosing tag, so
  /// no new <c>xmlns</c> attributes should be added.
  /// </summary>
  /// <remarks>The default implementation writes an empty element named <see cref="Name"/>.</remarks>
  public virtual void WriteValue(XmlWriter writer, WebDAVContext context)
  {
    writer.WriteEmptyElement(Name);
  }

  /// <summary>Represents the <c>DAV:collection</c> resource type.</summary>
  public static readonly ResourceType Collection = new ResourceType(DAVNames.collection);

  /// <summary>Gets the qualified name of the root element of the resource type XML.</summary>
  protected XmlQualifiedName Name { get; private set; }
}
#endregion

#region PropFindFlags
/// <summary>Specifies flags that influence how a <see cref="PropFindRequest"/> should be processed.</summary>
[Flags]
public enum PropFindFlags
{
  /// <summary>The values of the properties listed in <see cref="PropFindRequest.Properties"/> should be returned.</summary>
  None=0,
  /// <summary>If used in conjunction with <see cref="NamesOnly"/>, all property names should be returned. Otherwise, the values of the
  /// the properties listed in <see cref="PropFindRequest.Properties"/> should be returned, along with all dead properties, plus all live
  /// properties that are not too expensive to compute or transmit.
  /// </summary>
  IncludeAll=1,
  /// <summary>Indicates that only the names of properties (and their data types, if known) are to be returned. In particular, property
  /// values must not be returned. If this flag is set, <see cref="PropFindRequest.Properties"/> is guaranteed to be empty.
  /// </summary>
  NamesOnly=2
}
#endregion

// TODO: add processing examples and documentation
#region PropFindRequest
/// <summary>Represents a <c>PROPFIND</c> request.</summary>
/// <remarks>The <c>PROPFIND</c> request is described in section 9.1 of RFC 4918.</remarks>
public class PropFindRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="PropFindRequest"/> based on a new WebDAV request.</summary>
  public PropFindRequest(WebDAVContext context) : base(context)
  {
    Properties = new PropertyNameSet();
    Resources  = new PropFindResourceCollection();
  }

  #region PropFindResourceCollection
  /// <summary>A collection containing the resources (and their properties) added by the <see cref="IWebDAVService"/> that processes the
  /// request.
  /// </summary>
  public sealed class PropFindResourceCollection : CollectionBase<PropFindResource>
  {
    internal PropFindResourceCollection() { }
  }
  #endregion

  /// <summary>Gets the <see cref="PropFindFlags"/> that influence how the request should be processed.</summary>
  public PropFindFlags Flags { get; private set; }

  /// <summary>Gets a collection containing the names of properties specifically requested by the client.</summary>
  public PropertyNameSet Properties { get; private set; }

  /// <summary>Gets a collection into which the <see cref="IWebDAVService"/> should place the resources (and their properties) when
  /// servicing the request.
  /// </summary>
  public PropFindResourceCollection Resources { get; private set; }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequest/node()" />
  /// <param name="properties">A dictionary containing the properties for the request resource. Live properties that are expensive
  /// to compute or transmit only need to be added if they are referenced by the <see cref="Properties"/> collection or if
  /// <see cref="Flags"/> contains <see cref="PropFindFlags.NamesOnly"/> (but in the latter case, the property values are ignored and can
  /// be null). Dead properties only need to be added if you want to override properties from the configured <see cref="IPropertyStore"/>.
  /// In addition to property values, the dictionary can also contain <c>Func&lt;object&gt;</c> delegates which will provide
  /// the values when executed. This allows you to add delegates that compute expensive properties only when needed.
  /// </param>
  public void ProcessStandardRequest(IDictionary<XmlQualifiedName, object> properties)
  {
    if(properties == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null) Status = precondition;
    else AddResource(Context.RequestPath, Context.CanonicalPathIfKnown, properties, SetObjectValue);
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequest/node()" />
  /// <param name="properties">A dictionary containing the properties for the request resource. Live properties that are expensive to
  /// compute or transmit only need to be added if they are referenced by the <see cref="Properties"/> collection or if
  /// <see cref="Flags"/> contains <see cref="PropFindFlags.NamesOnly"/> (but in the latter case, the property values are ignored and can
  /// be null). Dead properties only need to be added if you want to override properties from the configured <see cref="IPropertyStore"/>.
  /// </param>
  public void ProcessStandardRequest(IDictionary<XmlQualifiedName, PropFindValue> properties)
  {
    if(properties == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null) Status = precondition;
    else AddResource(Context.RequestPath, Context.CanonicalPathIfKnown, properties, SetPropFindValue);
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequestRec/node()" />
  /// <param name="getProperties">Given a value representing a resource and its path, returns a dictionary containing the resource's
  /// properties. Live properties that are expensive to compute or transmit only need to be returned if they are referenced by the
  /// <see cref="Properties"/> collection or if <see cref="Flags"/> contains <see cref="PropFindFlags.NamesOnly"/> (but in the latter case,
  /// the property values are ignored and can be null). Dead properties only need to be returned if you want to override properties from
  /// the configured <see cref="IPropertyStore"/>. In addition to property values, the dictionary can also contain
  /// <c>Func&lt;object&gt;</c> delegates which will provide the values when executed. This allows you to add delegates that compute
  /// expensive properties only when needed.
  /// </param>
  public void ProcessStandardRequest<T>(T rootValue, Func<T, string> getMemberName,
                                        Func<T, string, IDictionary<XmlQualifiedName, object>> getProperties,
                                        Func<T, string, IEnumerable<T>> getChildren)
  {
    if(getMemberName == null || getProperties == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      ProcessStandardRequest(rootValue, Context.RequestPath, Context.CanonicalPathIfKnown, getMemberName, getProperties, getChildren,
                             SetObjectValue, 0);
    }
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequestRec/node()" />
  /// <param name="getProperties">Given a value representing a resource and its path, returns a dictionary containing the resource's
  /// properties. Live Properties that are expensive to compute or transmit only need to be returned if they are referenced by the
  /// <see cref="Properties"/> collection or if <see cref="Flags"/> contains <see cref="PropFindFlags.NamesOnly"/> (but in the latter case,
  /// the property values are ignored and can be null). Dead properties only need to be added if you want to override properties from the
  /// configured <see cref="IPropertyStore"/>.
  /// </param>
  public void ProcessStandardRequest<T>(T rootValue, Func<T, string> getMemberName,
                                        Func<T, string, IDictionary<XmlQualifiedName, PropFindValue>> getProperties,
                                        Func<T, string, IEnumerable<T>> getChildren)
  {
    if(getMemberName == null || getProperties == null) throw new ArgumentNullException();
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null)
    {
      Status = precondition;
    }
    else
    {
      ProcessStandardRequest(rootValue, Context.RequestPath, Context.CanonicalPathIfKnown, getMemberName, getProperties, getChildren,
                             SetPropFindValue, 0);
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // RFC4918 section 9.1 says PROPFIND should treat unspecified Depths as though infinity was specified
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants;

    // the body of the request must either be empty (in which case we default to an allprop request) or an XML fragment describing the
    // properties desired
    XmlDocument xml = Context.LoadRequestXml();
    if(xml == null) // if the body was empty...
    {
      Flags = PropFindFlags.IncludeAll; // default to an allprops match (as required by RFC 4918 section 9.1)
    }
    else // the client included a body, which should be a DAV::propfind element
    {
      // parse the DAV::propfind element
      xml.DocumentElement.AssertName(DAVNames.propfind);
      bool allProp = false, include = false, prop = false, propName = false;
      foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements()) // examine the children of the root
      {
        // the DAV::allprop and DAV::propname elements are simple flags
        if(!child.SetFlagOnce(DAVNames.allprop, ref allProp) && !child.SetFlagOnce(DAVNames.propname, ref propName))
        {
          // the DAV::prop and DAV::include elements both contain lists of property names
          if(child.SetFlagOnce(DAVNames.prop, ref prop) || child.SetFlagOnce(DAVNames.include, ref include))
          {
            if(!allProp && child.HasName(DAVNames.include)) // include should come after allprop
            {
              throw Exceptions.BadRequest("The include element must follow the allprop element.");
            }
            // for each child in the list, add it to the list of requested properties
            foreach(XmlQualifiedName qname in child.EnumerateChildElements().Select(XmlNodeExtensions.GetQualifiedName)) Properties.Add(qname);
          }
        }
      }

      // make sure there was exactly one query type specified, and disallow requests for no properties
      if(!(allProp | prop | propName)) throw Exceptions.BadRequest("The type of query was not specified.");
      if((allProp ? 1 : 0) + (prop ? 1 : 0) + (propName ? 1 : 0) > 1) throw Exceptions.BadRequest("Multiple query types were specified.");

      // use the elements we saw to set Flags
      if(allProp) Flags |= PropFindFlags.IncludeAll;
      else if(propName) Flags |= PropFindFlags.NamesOnly | PropFindFlags.IncludeAll; // NamesOnly implies that we want all names
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal override void WriteResponse()
  {
    if(Status != null)
    {
      Context.WriteStatusResponse(Status);
      return;
    }

    // validate the request processing and collect the set of XML namespaces used in the response (DAV: is added automatically)
    HashSet<string> namespaces = new HashSet<string>();
    // add xs: even if we may not use it directly, to prevent custom XML elements getting xmlns:xs definitions if they use xsi:type with xs
    namespaces.Add(DAVNames.XmlSchema);
    namespaces.Add(DAVNames.XmlSchemaInstance); // we use xsi:, in xsi:type
    foreach(PropFindResource resource in Resources) resource.Validate(this, namespaces);

    using(MultiStatusResponse response = Context.OpenMultiStatusResponse(namespaces))
    {
      XmlWriter writer = response.Writer;

      // now output a <response> tag for each resource
      var valuesByStatus = new MultiValuedDictionary<ConditionCode, KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>();
      foreach(PropFindResource resource in Resources)
      {
        writer.WriteStartElement(DAVNames.response.Name);
        writer.WriteElementString(DAVNames.href.Name, Context.ServiceRoot + resource.RelativePath); // <href> required by RFC 4918 section 9.1

        if(resource.properties.Count == 0) // if the resource has no properties, quickly render an empty properties collection
        {
          writer.WriteStartElement(DAVNames.propstat.Name);
          writer.WriteEmptyElement(DAVNames.prop.Name);
          response.WriteStatus(ConditionCodes.OK);
          writer.WriteEndElement();
        }
        else
        {
          // group the properties by condition code. (unspecified condition codes are assumed to be 200 OK)
          valuesByStatus.Clear();
          foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> pair in resource.properties)
          {
            valuesByStatus.Add(pair.Value == null ? ConditionCodes.OK : pair.Value.Code ?? ConditionCodes.OK, pair);
          }

          // then, output a <propstat> element for each status, containing the properties having that status
          foreach(KeyValuePair<ConditionCode, List<KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>> spair in valuesByStatus)
          {
            writer.WriteStartElement(DAVNames.propstat.Name);

            // output the properties
            writer.WriteStartElement(DAVNames.prop.Name);
            foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> ppair in spair.Value) // for each property in the group
            {
              XmlElement element = ppair.Value == null ? null : ppair.Value.Value as XmlElement;
              if(element != null) // if we have to output a raw XML element...
              {
                WriteElement(response, element);
              }
              else // otherwise, we're outputting a value
              {
                writer.WriteStartElement(ppair.Key); // write the property name
                if(ppair.Value != null) // if the property has a type or value or language...
                {
                  // if the property has a type that should be reported, write it in an xsi:type attribute
                  XmlQualifiedName type = ppair.Value.Type;
                  if(type != null)
                  {
                    writer.WriteAttributeString(DAVNames.xsiType,
                                                StringUtility.Combine(":", writer.LookupPrefix(type.Namespace), type.Name));
                  }

                  // if the property has a language, write it in an xml:lang attribute
                  if(!string.IsNullOrEmpty(ppair.Value.Language)) writer.WriteAttributeString(DAVNames.xmlLang, ppair.Value.Language);

                  object value = ppair.Value.Value;
                  if(value != null) // if the property has a value...
                  {
                    // first check for values that implement IElementValue, or IEnumerable<T> values where T implements IElementValue
                    IElementValue elementValue = value as IElementValue;
                    System.Collections.IEnumerable elementValues = elementValue == null ? GetElementValuesEnumerable(value) : null;
                    if(elementValue != null) // if the value implements IElementValue...
                    {
                      elementValue.WriteValue(writer, Context); // let IElementValue do the writing
                    }
                    else if(elementValues != null) // if the value is IEnumerable<T> where T implements IElementValue...
                    {
                      foreach(IElementValue elemValue in elementValues) elemValue.WriteValue(writer, Context); // write them all out
                    }
                    else if(type != null && value is byte[]) // if it's a byte array, write a base64 array or hex array depending on the type
                    {
                      byte[] binaryValue = (byte[])value;
                      if(type == DAVNames.xsHexBinary) writer.WriteString(BinaryUtility.ToHex(binaryValue)); // hexBinary gets hex
                      else writer.WriteBase64(binaryValue, 0, binaryValue.Length); // and xsB64Binary and unknown binary types get base64
                    }
                    else if(type == DAVNames.xsDate) // if the type is xs:date, write only the date portions of any datetime values
                    {
                      if(value is DateTime) writer.WriteDate((DateTime)value);
                      else if(value is DateTimeOffset) writer.WriteDate(((DateTimeOffset)value).Date);
                      else writer.WriteValue(value); // if the value type is unrecognized, fall back on .WriteValue(object)
                    }
                    else if(value is XmlDuration || value is Guid)
                    {
                      writer.WriteString(value.ToString()); // XmlWriter.WriteValue() doesn't know about XmlDuration or Guid values
                    }
                    else // in the general case, just use .WriteValue(object) to write the value appropriately
                    {
                      writer.WriteValue(value);
                    }
                  }
                }
                writer.WriteEndElement(); // end property name (i.e. ppair.Key)
              }
            }
            writer.WriteEndElement(); // </prop>

            // now write the status for the aforementioned properties
            response.WriteStatus(spair.Key);

            writer.WriteEndElement(); // </propstat>
          }
        }

        writer.WriteEndElement(); // </response>
      }
    }
  }

  internal static System.Collections.IEnumerable GetElementValuesEnumerable(object value)
  {
    if(!(value is string)) // ignore string because it's a very common type that also implements IEnumerable
    {
      System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
      if(enumerable != null) // if the value implements IEnumerable (and therefore may implement IEnumerable<T>)
      {
        Type type = value.GetType();
        object isIElementValue = isIElementValuesEnumerable[type]; // Hashtable allows lock-free reads
        if(isIElementValue == null) // if we don't yet know whether the type implements IEnumerable<T> where T : IElementValue...
        {
          isIElementValue = @false;
          // get the interfaces implemented by the method. this includes base interfaces, so if it implements an interface derived from
          // IEnumerable<T>, then IEnumerable<T> will still be in the output of type.GetInterfaces()
          foreach(Type iface in type.GetInterfaces())
          {
            if(iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>)) // if it's IEnumerable<*> for some *...
            {
              Type[] typeArgs = iface.GetGenericArguments();
              if(typeArgs.Length == 1 && typeof(IElementValue).IsAssignableFrom(typeArgs[0])) // IEnumerable<T> where T : IElementValue...
              {
                // then we know this is a value that we should output using IElementValue. note that it's theoretically possible for the
                // type to implement IEnumerable<T> for multiple values of T, where the IEnumerable interface corresponds to the wrong one,
                // but we'll ignore that possibility
                isIElementValue = @true;
                break;
              }
            }
          }

          // remember whether the type implemented it so we can avoid doing this reflection work for future values
          lock(isIElementValuesEnumerable) isIElementValuesEnumerable[type] = isIElementValue;
        }

        if(isIElementValue == @true) return enumerable;
      }
    }

    return null;
  }

  /// <summary>Processes a standard request for a single resource and adds the corresponding <see cref="PropFindResource"/> to
  /// <see cref="Resources"/>.
  /// </summary>
  void AddResource<V>(string requestPath, string canonicalPath, IDictionary<XmlQualifiedName, V> properties,
                      Action<PropFindResource,XmlQualifiedName,V> setValue)
  {
    PropFindResource resource = new PropFindResource(requestPath);
    if((Flags & PropFindFlags.NamesOnly) != 0) // if the client requested all property names...
    {
      if(Context.PropertyStore != null) resource.SetNames(Context.PropertyStore.GetProperties(canonicalPath).Keys);
      resource.SetNames(properties.Keys);
    }
    else // otherwise, the client wants property values
    {
      // collect the dead properties for the resource
      IDictionary<XmlQualifiedName,XmlProperty> deadProperties =
        Context.PropertyStore == null ? null : Context.PropertyStore.GetProperties(canonicalPath);

      if(Properties.Count != 0) // if the client explicitly requested certain properties...
      {
        foreach(XmlQualifiedName name in Properties) // for each explicitly requested property name...
        {
          V value;
          XmlProperty deadProperty;
          if(properties.TryGetValue(name, out value))
          {
            setValue(resource, name, value);
          }
          else if(deadProperties != null && deadProperties.TryGetValue(name, out deadProperty))
          {
            resource.SetValue(name, GetPropFindValue(deadProperty));
          }
          else
          {
            resource.SetError(name, ConditionCodes.NotFound);
          }
        }
      }

      if((Flags & PropFindFlags.IncludeAll) != 0) // if the client requested all other properties too...
      {
        foreach(KeyValuePair<XmlQualifiedName, V> pair in properties) // first add service properties
        {
          if(!Properties.Contains(pair.Key)) setValue(resource, pair.Key, pair.Value); // set the property if we haven't already...
        }
        if(deadProperties != null) // if there are dead properties...
        {
          foreach(XmlProperty deadProperty in deadProperties.Values) // add them, giving priority to property values from the service
          {
            if(!resource.Contains(deadProperty.Name)) resource.SetValue(deadProperty.Name, GetPropFindValue(deadProperty));
          }
        }
      }
    }

    Resources.Add(resource);
  }

  /// <summary>Processes a standard request for a resource and possibly its children or descendants.</summary>
  void ProcessStandardRequest<T,V>(T resource, string requestPath, string canonicalPath, Func<T, string> getMemberName,
                                   Func<T, string, IDictionary<XmlQualifiedName, V>> getProperties,
                                   Func<T, string, IEnumerable<T>> getChildren,
                                   Action<PropFindResource,XmlQualifiedName,V> setValue, int depth)
  {
    // add the given resource, validating the return values from the delegates first
    IDictionary<XmlQualifiedName,V> properties = getProperties(resource, canonicalPath);
    if(properties == null) throw new ArgumentException("The properties dictionary for " + requestPath + " was null.");
    AddResource(requestPath, canonicalPath, properties, setValue);

    if(getChildren != null && (depth == 0 ? Depth != Depth.Self : Depth == Depth.SelfAndDescendants)) // if we should recurse...
    {
      IEnumerable<T> children = getChildren(resource, canonicalPath);
      if(children != null)
      {
        requestPath   = DAVUtility.WithTrailingSlash(requestPath);
        canonicalPath = DAVUtility.WithTrailingSlash(canonicalPath);
        foreach(T child in children)
        {
          string name = getMemberName(child);
          ProcessStandardRequest(child, requestPath + name, canonicalPath + name,
                                 getMemberName, getProperties, getChildren, setValue, depth+1);
        }
      }
    }
  }

  static PropFindValue GetPropFindValue(XmlProperty deadProperty)
  {
    return deadProperty.Element != null ? new PropFindValue(deadProperty.Element) :
      new PropFindValue(deadProperty.Value, deadProperty.Type, deadProperty.Language);
  }

  static void SetObjectValue(PropFindResource resource, XmlQualifiedName propertyName, object value)
  {
    resource.SetValue(propertyName, value);
  }

  static void SetPropFindValue(PropFindResource resource, XmlQualifiedName propertyName, PropFindValue value)
  {
    resource.SetValue(propertyName, value);
  }

  static void WriteElement(MultiStatusResponse response, XmlElement element)
  {
    XmlWriter writer = response.Writer;

    // begin writing the start tag. we'll assume that all the namespaces are already mapped to prefixes
    writer.WriteStartElement(element.LocalName, element.NamespaceURI);

    // write the original element attributes, skipping xmlns attributes
    foreach(XmlAttribute attr in element.Attributes)
    {
      if((attr.Prefix.Length == 0 ? attr.LocalName : attr.Prefix).OrdinalEquals("xmlns")) continue;

      // handle xsi:type attributes specially, because we need to translate the QName values from their original context to the context of
      // the writer. ideally we'd be able to do this for all QName-valued attributes, but we don't know which ones those are...
      if(attr.NamespaceURI.OrdinalEquals(DAVNames.XmlSchemaInstance) && attr.LocalName.OrdinalEquals("type"))
      {
        writer.WriteStartAttribute(attr.LocalName, attr.NamespaceURI);
        XmlQualifiedName qname = element.ParseQualifiedName(attr.Value);
        writer.WriteQualifiedName(qname.Name, qname.Namespace);
        writer.WriteEndAttribute();
      }
      else
      {
        writer.WriteAttributeString(attr.LocalName, attr.NamespaceURI, attr.Value);
      }
    }

    // now recursively write element content
    foreach(XmlNode node in element.ChildNodes)
    {
      switch(node.NodeType)
      {
        case XmlNodeType.CDATA: writer.WriteCData(node.Value); break;
        case XmlNodeType.Element: WriteElement(response, (XmlElement)node); break;
        case XmlNodeType.SignificantWhitespace: writer.WriteString(node.Value); break;
        case XmlNodeType.Text: writer.WriteString(node.Value); break;
        case XmlNodeType.Whitespace: writer.WriteWhitespace(node.Value); break;
      }
    }

    writer.WriteEndElement();
  }

  // use a System.Collections.Hashtable because it has a special implementation that allows lock-free reads
  static readonly System.Collections.Hashtable isIElementValuesEnumerable = new System.Collections.Hashtable();
  static readonly object @true = true, @false = false; // pre-boxed values stored within the hashtable
}
#endregion

#region PropFindResource
/// <summary>Represents a resource whose properties will be returned in response to a <c>PROPFIND</c> request.</summary>
public sealed class PropFindResource
{
  /// <summary>Initializes a new <see cref="PropFindResource"/> given the path to the resource, relative to the
  /// <see cref="WebDAVContext.ServiceRoot"/>. This is usually but not necessarily the canonical path.
  /// </summary>
  public PropFindResource(string relativePath)
  {
    if(relativePath == null) throw new ArgumentNullException();
    RelativePath = relativePath;
  }

  /// <summary>Gets the path to the resource, relative to the <see cref="WebDAVContext.ServiceRoot"/>.</summary>
  public string RelativePath { get; private set; }

  /// <summary>Determines whether the given property has been defined on the resource.</summary>
  public bool Contains(XmlQualifiedName property)
  {
    if(property == null) throw new ArgumentNullException();
    return properties.ContainsKey(property);
  }

  /// <summary>Removes a property defined on the resource.</summary>
  public bool Remove(XmlQualifiedName property)
  {
    return properties.Remove(property);
  }

  /// <summary>Indicates that an error has occurred in servicing the named property for this resource.</summary>
  public void SetError(XmlQualifiedName property, ConditionCode errorCode)
  {
    if(property == null || errorCode == null) throw new ArgumentNullException();
    properties[property] = new PropertyValue() { Code = errorCode };
  }

  /// <summary>Adds the property name to the resource without a type or value. This method is generally used when servicing a request where
  /// <see cref="PropFindRequest.Flags"/> contains <see cref="PropFindFlags.NamesOnly"/>.
  /// </summary>
  public void SetName(XmlQualifiedName property)
  {
    if(property == null) throw new ArgumentNullException();
    properties[property] = null;
  }

  /// <summary>Adds the property name to the resource without a value, but indicating that the property is of the given data type. This
  /// method is generally used when servicing a request where <see cref="PropFindRequest.Flags"/> contains
  /// <see cref="PropFindFlags.NamesOnly"/>.
  /// </summary>
  public void SetName(XmlQualifiedName property, XmlQualifiedName type)
  {
    if(property == null) throw new ArgumentNullException();
    properties[property] = new PropertyValue() { Type = type };
  }

  /// <summary>Calls <see cref="SetName(XmlQualifiedName)"/> on each name in a set. This method is generally used when servicing a request
  /// where <see cref="PropFindRequest.Flags"/> contains <see cref="PropFindFlags.NamesOnly"/>.
  /// </summary>
  public void SetNames(IEnumerable<XmlQualifiedName> propertyNames)
  {
    if(propertyNames == null) throw new ArgumentNullException();
    foreach(XmlQualifiedName name in propertyNames) SetName(name);
  }

  /// <summary>Sets the value or error status of a property on the resource, based on the given <see cref="PropFindValue"/>.</summary>
  /// <param name="property">The name of the property to set.</param>
  /// <param name="value">The value to set. If null, the property will be set to a null value. Otherwise, if
  /// <see cref="PropFindValue.Status"/> is not null, the status will be set as the property's error status. Otherwise, the value,
  /// type, and language will be set, potentially with the type being inferred from the value (as determined by how the
  /// <see cref="PropFindValue"/> object was constructed).
  /// </param>
  public void SetValue(XmlQualifiedName property, PropFindValue value)
  {
    if(value == null) SetValue(property, null, null, null);
    else if(value.Status != null) SetError(property, value.Status);
    else if(value.InferType) SetValue(property, value.Value, value.Language);
    else SetValue(property, value.Value, value.Type, value.Language);
  }

  /// <summary>Sets the value of a property on the resource. If the value is not null and the property is not a built-in WebDAV property
  /// (i.e. one defined in RFC 4918), the property data type will be inferred from the value. If you do not want this inference to occur,
  /// call <see cref="SetValue(XmlQualifiedName,object,XmlQualifiedName,string)"/> and pass the correct type, or null if you don't want any
  /// type information to be reported.
  /// </summary>
  public void SetValue(XmlQualifiedName property, object value)
  {
    SetValue(property, value, (string)null);
  }

  /// <summary>Sets the value and language of a property on the resource. If the value is not null and the property is not a built-in
  /// WebDAV property (i.e. one defined in RFC 4918), the property data type will be inferred from the value. If you do not want this
  /// inference to occur, call <see cref="SetValue(XmlQualifiedName,object,XmlQualifiedName,string)"/> and pass the correct type, or null
  /// if you don't want any type information to be reported. This method also accepts the language of the value. If not null or empty,
  /// the language will be reported in the value's <c>xml:lang</c> attribute and must be in the corresponding format.
  /// </summary>
  public void SetValue(XmlQualifiedName property, object value, string language)
  {
    XmlQualifiedName type = null;
    value = GetPropertyValue(value);
    if(value != null && !XmlProperty.builtInTypes.ContainsKey(property)) type = DAVUtility.GetXsiType(value);
    SetValueCore(property, value, type, language);
  }

  /// <summary>Sets the value and type of a property on the resource.</summary>
  /// <include file="documentation.xml" path="/DAV/PropFindResource/SetValueNIRemarks/node()" />
  public void SetValue(XmlQualifiedName property, object value, XmlQualifiedName type)
  {
    SetValue(property, value, type, null);
  }

  /// <summary>Sets the value, type, and language of a property on the resource.</summary>
  /// <include file="documentation.xml" path="/DAV/PropFindResource/SetValueNIRemarks/node()" />
  public void SetValue(XmlQualifiedName property, object value, XmlQualifiedName type, string language)
  {
    if(property == null) throw new ArgumentNullException();
    value = GetPropertyValue(value);
    // if it's a type defined in xml schema (xs:), validate that the value is of that type
    if(value != null && type != null && type.Namespace.OrdinalEquals(DAVNames.XmlSchema) &&
       !XmlProperty.builtInTypes.ContainsKey(property))
    {
      value = ValidatePropertyValue(property, value, type);
    }
    SetValueCore(property, value, type, language);
  }

  #region PropertyValue
  /// <summary>Represents the value, type, and status code of an attempt to retrieve a property on a resource.</summary>
  internal sealed class PropertyValue
  {
    public PropertyValue() { }
    
    public PropertyValue(XmlQualifiedName type, object value, string language)
    {
      Type     = type;
      Value    = value;
      Language = language;
    }

    public object Value;
    public XmlQualifiedName Type;
    public ConditionCode Code;
    public string Language;
  }
  #endregion

  /// <summary>Ensures that this <see cref="PropFindResource"/> passes basic validity checks, and adds the XML namespaces needed for the
  /// response to the <paramref name="namespaces"/> set.
  /// </summary>
  internal void Validate(PropFindRequest request, HashSet<string> namespaces)
  {
    // perform a more expensive check in debug mode to ensure that all requested properties were provided on all resources. (this helps
    // check whether the service is implemented correctly)
    #if DEBUG
    foreach(XmlQualifiedName requestedProperty in request.Properties)
    {
      if(!properties.ContainsKey(requestedProperty))
      {
        throw new ContractViolationException("The " + requestedProperty.ToString() + " property was specifically requested, but was not " +
                                             "handled on resource " + RelativePath);
      }
    }
    #endif

    // add the XML namespaces used by the this resource
    foreach(KeyValuePair<XmlQualifiedName, PropertyValue> pair in properties)
    {
      namespaces.Add(pair.Key.Namespace);
      if(pair.Value != null)
      {
        if(pair.Value.Type != null) namespaces.Add(pair.Value.Type.Namespace);

        object value = pair.Value.Value;
        if(value != null)
        {
          IElementValue elementValue = value as IElementValue;
          if(elementValue != null)
          {
            IEnumerable<string> ns = elementValue.GetNamespaces();
            if(ns != null) namespaces.UnionWith(ns);
          }
          else
          {
            XmlElement element = value as XmlElement;
            if(element != null)
            {
              AddElementNamespaces(element, namespaces);
            }
            else
            {
              System.Collections.IEnumerable enumerable = PropFindRequest.GetElementValuesEnumerable(value);
              if(enumerable != null)
              {
                foreach(IElementValue elemValue in enumerable)
                {
                  IEnumerable<string> ns = elemValue.GetNamespaces();
                  if(ns != null) namespaces.UnionWith(ns);
                }
              }
            }
          }
        }
      }
    }
  }

  internal readonly Dictionary<XmlQualifiedName, PropertyValue> properties = new Dictionary<XmlQualifiedName, PropertyValue>();

  void SetValueCore(XmlQualifiedName property, object value, XmlQualifiedName type, string language)
  {
    if(property == null) throw new ArgumentNullException();

    // if the property is of a built-in type, validate that the value matches the expected type
    XmlQualifiedName expectedType;
    if(XmlProperty.builtInTypes.TryGetValue(property, out expectedType))
    {
      // validate that the value matches the expected type
      if(expectedType != null)
      {
        value = ValidatePropertyValue(property, value, expectedType);
      }
      else if(value != null)
      {
        if(property == DAVNames.resourcetype)
        {
          if(value is XmlQualifiedName)
          {
            value = new ResourceType((XmlQualifiedName)value);
          }
          else if(value is IEnumerable<XmlQualifiedName>)
          {
            value = ((IEnumerable<XmlQualifiedName>)value).Select(n => new ResourceType(n)).ToList();
          }
          else if(!(value is ResourceType) && !(value is IEnumerable<ResourceType>))
          {
            throw new ContractViolationException(property + " is expected to be a ResourceType, an XmlQualifiedName representing a " +
                                                 "resource type, or an IEnumerable<T> of ResourceType or XmlQualifiedName.");
          }
        }
        else if(property == DAVNames.getetag)
        {
          if(!(value is EntityTag)) throw new ContractViolationException(property + " is expected to be an EntityTag.");
        }
        else if(property == DAVNames.lockdiscovery)
        {
          if(!(value is ActiveLock) && !(value is IEnumerable<ActiveLock>))
          {
            throw new ContractViolationException(property + " is expected to be an ActiveLock or IEnumerable<ActiveLock>.");
          }
        }
        else if(property == DAVNames.supportedlock)
        {
          if(!(value is LockType) && !(value is IEnumerable<LockType>))
          {
            throw new ContractViolationException(property + " is expected to be a LockType or IEnumerable<LockType>.");
          }
        }
      }

      type = null; // built-in properties shouldn't report their type (as per RFC 4316 section 5)
    }
    else if(type == DAVNames.xsString)
    {
      type = null; // xs:string types should not be reported because that's the default (as per RFC 4316 section 5)
    }

    // save the property value. don't bother creating a PropertyValue object if it doesn't hold anything useful
    properties[property] = type == null && value == null && language == null ? null : new PropertyValue(type, value, language);
  }

  static void AddElementNamespaces(XmlElement element, HashSet<string> namespaces)
  {
    if(!string.IsNullOrEmpty(element.NamespaceURI)) namespaces.Add(element.NamespaceURI);

    foreach(XmlAttribute attr in element.Attributes)
    {
      if(!string.IsNullOrEmpty(attr.NamespaceURI) && !attr.Prefix.OrdinalEquals("xml") &&
         !(attr.Prefix.Length == 0 ? attr.LocalName : attr.Prefix).OrdinalEquals("xmlns"))
      {
        namespaces.Add(attr.NamespaceURI);
      }
    }

    foreach(XmlNode child in element.ChildNodes)
    {
      if(child.NodeType == XmlNodeType.Element) AddElementNamespaces((XmlElement)child, namespaces);
    }
  }

  static object GetPropertyValue(object value)
  {
    if(value != null)
    {
      Func<object> getter = value as Func<object>;
      if(getter != null) value = getter();
    }
    return value;
  }

  static object ValidatePropertyValue(XmlQualifiedName property, object value, XmlQualifiedName expectedType)
  {
    try { return DAVUtility.ValidatePropertyValue(property, value, expectedType); }
    catch(ArgumentException ex) { throw new ContractViolationException(ex.Message); }
  }
}
#endregion

#region PropFindValue
/// <summary>Represents a property value and status to be sent to a client in response to a <c>PROPFIND</c> request.</summary>
public sealed class PropFindValue
{
  /// <summary>Initializes a new <see cref="PropFindValue"/> based on the property's error status.</summary>
  public PropFindValue(ConditionCode errorStatus)
  {
    Status = errorStatus;
  }

  /// <summary>Initializes a new <see cref="PropFindValue"/> with the XML element to be sent to the client. Note that the XML element
  /// may be rewritten when output to avoid conflicting namespace prefixes with the rest of the response.
  /// </summary>
  public PropFindValue(XmlElement element)
  {
    if(element == null) throw new ArgumentNullException();
    Value = element;
  }

  /// <summary>Initializes a new <see cref="PropFindValue"/> based on the property's value. The type of the value will be inferred. If you
  /// do not want the type to be inferred, use the <see cref="PropFindValue(object,XmlQualifiedName)"/> constructor and pass null for the
  /// type.
  /// </summary>
  public PropFindValue(object value) : this(value, null, null)
  {
    InferType = true;
  }

  /// <summary>Initializes a new <see cref="PropFindValue"/> based on the property's value and language. The type of the value will be
  /// inferred. If you do not want the type to be inferred, use the <see cref="PropFindValue(object,XmlQualifiedName,string)"/> constructor
  /// and pass null for the type.
  /// </summary>
  public PropFindValue(object value, string language) : this(value, null, language)
  {
    InferType = true;
  }

  /// <summary>Initializes a new <see cref="PropFindValue"/> based on the property's type and value.</summary>
  /// <include file="documentation.xml" path="/DAV/PropFindResource/SetValueNIRemarks/node()" />
  public PropFindValue(object value, XmlQualifiedName type) : this(value, type, null) { }

  /// <summary>Initializes a new <see cref="PropFindValue"/> based on the property's type, value, and language.</summary>
  /// <include file="documentation.xml" path="/DAV/PropFindResource/SetValueNIRemarks/node()" />
  public PropFindValue(object value, XmlQualifiedName type, string language)
  {
    Type        = type;
    Value       = value;
    Language    = StringUtility.MakeNullIfEmpty(language);
  }

  /// <summary>Gets the language of the value in <c>xml:lang</c> format, or null if no language is defined.</summary>
  public string Language { get; private set; }

  /// <summary>Gets the status representing the result of attempting to set the property. If null, the operation will be assumed
  /// to be successful and <see cref="ConditionCodes.OK"/> will be used.
  /// </summary>
  public ConditionCode Status { get; private set; }

  /// <summary>Gets the <c>xsi:type</c> of the property element, or null if no type was declared.</summary>
  public XmlQualifiedName Type { get; private set; }

  /// <summary>Gets the value of the property.</summary>
  public object Value { get; private set; }

  internal bool InferType { get; private set; }
}
#endregion

} // namespace AdamMil.WebDAV.Server
