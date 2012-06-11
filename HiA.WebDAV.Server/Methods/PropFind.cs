using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

// TODO: add dead properties, etc.
// TODO: add or find xml types for guid and other common values that aren't in xs:
// TODO: other stuff from DAV RFC section 4.3
// TODO: add a .Status property or something so that processors don't have to throw exceptions if they don't want to handle a request

namespace HiA.WebDAV.Server
{

#region IElementValue
/// <summary>Represents a value that can be rendered into XML. Property values that derive from this class will use it to render
/// themselves.
/// </summary>
public interface IElementValue
{
  /// <summary>Returns the XML namespaces used by the property value, or null if the value does not use any namespaces.</summary>
  IEnumerable<string> GetNamespaces();
  /// <summary>Writes the value into XML. Any namespaces used by the value will already have been defined in the enclosing context, so no
  /// new <c>xmlns</c> attributes should be added.
  /// </summary>
  void WriteValue(XmlWriter writer);
}
#endregion

// TODO: implement ActiveLock
#region ActiveLock
/// <summary>Describes an active lock on a resource. This object is used with the <c>DAV:lockdiscovery</c>.</summary>
public class ActiveLock
{
  public ActiveLock()
  {
    throw new NotImplementedException();
  }
}
#endregion

// TODO: implement LockType
#region LockType
/// <summary>Represents a type of lock that can be used with a resource. This object is used with the <c>DAV:supportedlock</c> property.</summary>
public class LockType
{
  public LockType()
  {
    throw new NotImplementedException();
  }
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
  public virtual void WriteValue(XmlWriter writer)
  {
    writer.WriteEmptyElement(Name);
  }

  /// <summary>Represents the <c>DAV:collection</c> resource type.</summary>
  public static readonly ResourceType Collection = new ResourceType(Names.collection);

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
  /// the properties listed in <see cref="PropFindRequest.Properties"/> should be returned, along with all dead properties and all live
  /// properties that are not too expensive to compute or transmit.
  /// </summary>
  IncludeAll=1,
  /// <summary>Indicates that only the names of properties (and their data types, if known) are to be returned. In particular, property
  /// values must not be returned. If this flag is set, <see cref="PropFindRequest.Properties"/> will is guaranteed to be empty.
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
  public void ProcessStandardRequest(IDictionary<XmlQualifiedName, object> properties)
  {
    if(properties == null) throw new ArgumentNullException();
    AddResource(Context.RequestPath, properties);
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequest/node()" />
  public void ProcessStandardRequest(IDictionary<XmlQualifiedName, PropFindValue> properties)
  {
    if(properties == null) throw new ArgumentNullException();
    AddResource(Context.RequestPath, properties);
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequestRec/node()" />
  public void ProcessStandardRequest<T>(T rootValue, Func<T, string> getMemberName,
                                        Func<T, IDictionary<XmlQualifiedName, object>> getProperties, Func<T, IEnumerable<T>> getChildren)
  {
    if(getMemberName == null || getProperties == null) throw new ArgumentNullException();
    ProcessStandardRequest(rootValue, getMemberName, getProperties, getChildren, Context.RequestPath, 0);
  }

  /// <include file="documentation.xml" path="/DAV/PropFindRequest/ProcessStandardRequestRec/node()" />
  public void ProcessStandardRequest<T>(T rootValue, Func<T, string> getMemberName,
                                        Func<T, IDictionary<XmlQualifiedName, PropFindValue>> getProperties,
                                        Func<T, IEnumerable<T>> getChildren)
  {
    if(getMemberName == null || getProperties == null) throw new ArgumentNullException();
    ProcessStandardRequest(rootValue, getMemberName, getProperties, getChildren, Context.RequestPath, 0);
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
      xml.DocumentElement.AssertName(Names.propfind);
      bool allProp = false, include = false, prop = false, propName = false;
      foreach(XmlElement child in xml.DocumentElement.EnumerateElements()) // examine the children of the root
      {
        // the DAV::allprop and DAV::propname elements are simple flags
        if(!child.SetFlagOnce(Names.allprop, ref allProp) && !child.SetFlagOnce(Names.propname, ref propName))
        {
          // the DAV::prop and DAV::include elements both contain lists of property names
          if(child.SetFlagOnce(Names.prop, ref prop) || child.SetFlagOnce(Names.include, ref include))
          {
            if(!allProp && child.HasName(Names.include)) // include should come after allprop
            {
              throw Exceptions.BadRequest("The include element must follow the allprop element.");
            }
            // for each child in the list, add it to the list of requested properties
            foreach(XmlQualifiedName qname in child.EnumerateElements().Select(XmlNodeExtensions.GetQualifiedName)) Properties.Add(qname);
          }
        }
      }

      // make sure there was exactly one query type specified
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
    namespaces.Add(Names.XmlSchemaInstance); // we use xsi:, in xsi:type
    foreach(PropFindResource resource in Resources) resource.Validate(this, namespaces);

    using(MultiStatusResponse response = Context.OpenMultiStatusResponse(namespaces))
    {
      XmlWriter writer = response.Writer;

      // now output a <response> tag for each resource
      var valuesByStatus = new MultiValuedDictionary<ConditionCode, KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>();
      foreach(PropFindResource resource in Resources)
      {
        writer.WriteStartElement(Names.response.Name);
        writer.WriteElementString(Names.href.Name, Context.ServiceRoot + resource.RelativePath); // <href> required by RFC 4918 section 9.1

        // group the properties by condition code. (unspecified condition codes are assumed to be 200 OK)
        valuesByStatus.Clear();
        foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> pair in resource.properties)
        {
          valuesByStatus.Add(pair.Value == null ? ConditionCodes.OK : pair.Value.Code ?? ConditionCodes.OK, pair);
        }

        // then, output a <propstat> element for each status, containing the properties having that status
        foreach(KeyValuePair<ConditionCode, List<KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>> spair in valuesByStatus)
        {
          writer.WriteStartElement(Names.propstat.Name);

          // output the properties
          writer.WriteStartElement(Names.prop.Name);
          foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> ppair in spair.Value) // for each property in the group
          {
            writer.WriteStartElement(ppair.Key); // write the property name
            if(ppair.Value != null) // if the property has a type or value or language...
            {
              // if the property has a type that should be reported, write it in an xsi:type attribute
              XmlQualifiedName type = ppair.Value.Type;
              if(type != null)
              {
                writer.WriteAttributeString(Names.xsiType, StringUtility.Combine(":", writer.LookupPrefix(type.Namespace), type.Name));
              }

              // if the property has a language, write it in an xml:lang attribute
              if(!string.IsNullOrEmpty(ppair.Value.Language)) writer.WriteAttributeString(Names.xmlLang, ppair.Value.Language);

              object value = ppair.Value.Value;
              if(value != null) // if the property has a value...
              {
                // first check for values that implement IElementValue, or IEnumerable<T> values where T implements IElementValue
                IElementValue elementValue = value as IElementValue;
                System.Collections.IEnumerable elementValues = elementValue == null ? GetElementValuesEnumerable(value) : null;
                if(elementValue != null) // if the value implements IElementValue...
                {
                  elementValue.WriteValue(writer); // let IElementValue do the writing
                }
                else if(elementValues != null) // if the value is IEnumerable<T> where T implements IElementValue...
                {
                  foreach(IElementValue elemValue in elementValues) elemValue.WriteValue(writer); // write them all out
                }
                else if(type != null && value is byte[]) // if it's a byte array, write a base64 array or hex array depending on the type
                {
                  byte[] binaryValue = (byte[])value;
                  if(type == Names.xsHexBinary) writer.WriteString(BinaryUtility.ToHex(binaryValue)); // hexBinary gets hex
                  else writer.WriteBase64(binaryValue, 0, binaryValue.Length); // and xsB64Binary and unknown binary types get base64
                }
                else if(type == Names.xsDate) // if the type is specified as xs:date, write only the date portions of any datetime values
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
                  writer.WriteValue(value); // TODO: test this with numeric types such as sbyte, uint, etc. (the WriteValue documentation doesn't mention them)
                }
              }
            }
            writer.WriteEndElement(); // end property name (i.e. ppair.Key)
          }
          writer.WriteEndElement(); // </prop>

          // now write the status for the aforementioned properties
          response.WriteStatus(spair.Key);

          writer.WriteEndElement(); // </propstat>
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
  void AddResource(string canonicalPath, IDictionary<XmlQualifiedName, object> properties)
  {
    PropFindResource resource = new PropFindResource(canonicalPath);
    if((Flags & PropFindFlags.NamesOnly) != 0) // if the client requested all property names...
    {
      resource.SetNames(properties.Keys); // add them
    }
    else // otherwise, the client wants property values
    {
      foreach(XmlQualifiedName name in Properties) // add the values of properties that the client explicitly requested
      {
        object value;
        if(properties.TryGetValue(name, out value)) resource.SetValue(name, value);
        else resource.SetError(name, ConditionCodes.NotFound);
      }

      if((Flags & PropFindFlags.IncludeAll) != 0) // if the client requested all other properties too...
      {
        foreach(KeyValuePair<XmlQualifiedName, object> pair in properties)
        {
          if(!Properties.Contains(pair.Key)) resource.SetValue(pair.Key, pair.Value); // add them if we haven't done so already
        }
      }
    }

    Resources.Add(resource);
  }

  /// <summary>Processes a standard request for a single resource and adds the corresponding <see cref="PropFindResource"/> to
  /// <see cref="Resources"/>.
  /// </summary>
  void AddResource(string canonicalPath, IDictionary<XmlQualifiedName, PropFindValue> properties)
  {
    PropFindResource resource = new PropFindResource(canonicalPath);
    if((Flags & PropFindFlags.NamesOnly) != 0) // if the client requested all property names...
    {
      resource.SetNames(properties.Keys); // add them
    }
    else // otherwise, the client wants property values
    {
      foreach(XmlQualifiedName name in Properties) // add the values of properties that the client explicitly requested
      {
        PropFindValue value;
        if(!properties.TryGetValue(name, out value)) resource.SetError(name, ConditionCodes.NotFound);
        else resource.SetValue(name, value);
      }

      if((Flags & PropFindFlags.IncludeAll) != 0) // if the client requested all other properties too...
      {
        foreach(KeyValuePair<XmlQualifiedName, PropFindValue> pair in properties)
        {
          if(!Properties.Contains(pair.Key)) resource.SetValue(pair.Key, pair.Value); // set the property if we haven't already...
        }
      }
    }

    Resources.Add(resource);
  }

  /// <summary>Processes a standard request for a resource and possibly its children or descendants.</summary>
  void ProcessStandardRequest<T>(T resource, Func<T, string> getCanonicalPath,
                                 Func<T, IDictionary<XmlQualifiedName, object>> getProperties, Func<T, IEnumerable<T>> getChildren,
                                 string davPath, int depth)
  {
    // add the given resource, validating the return values from the delegates first
    string name = getCanonicalPath(resource);
    if(name == null) throw new ArgumentException("A member name was null.");
    IDictionary<XmlQualifiedName,object> properties = getProperties(resource);
    if(properties == null) throw new ArgumentException("The properties dictionary for " + name + " was null.");
    davPath += name;
    AddResource(davPath, properties);

    if(getChildren != null && (depth == 0 ? Depth != Depth.Self : Depth == Depth.SelfAndDescendants)) // if we should recurse...
    {
      IEnumerable<T> children = getChildren(resource);
      if(children != null)
      {
        if(davPath.Length != 0 && davPath[davPath.Length-1] != '/') davPath += "/";
        foreach(T child in children) ProcessStandardRequest(child, getCanonicalPath, getProperties, getChildren, davPath, depth+1);
      }
    }
  }

  /// <summary>Processes a standard request for a resource and possibly its children or descendants.</summary>
  void ProcessStandardRequest<T>(T resource, Func<T, string> getMemberName,
                                 Func<T, IDictionary<XmlQualifiedName, PropFindValue>> getProperties, Func<T, IEnumerable<T>> getChildren,
                                 string davPath, int depth)
  {
    // add the given resource, validating the return values from the delegates first
    string name = getMemberName(resource);
    if(name == null) throw new ArgumentException("A member name was null.");
    IDictionary<XmlQualifiedName, PropFindValue> properties = getProperties(resource);
    if(properties == null) throw new ArgumentException("The properties dictionary for " + name + " was null.");
    davPath += name;
    AddResource(davPath, properties);

    if(getChildren != null && (depth == 0 ? Depth != Depth.Self : Depth == Depth.SelfAndDescendants)) // if we should recurse...
    {
      IEnumerable<T> children = getChildren(resource);
      if(children != null)
      {
        if(davPath.Length != 0 && davPath[davPath.Length-1] != '/') davPath += "/";
        foreach(T child in children) ProcessStandardRequest(child, getMemberName, getProperties, getChildren, davPath, depth+1);
      }
    }
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
    if(value != null && !builtInTypes.ContainsKey(property))
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Boolean: type = Names.xsBoolean; break;
        case TypeCode.Byte: type = Names.xsUByte; break;
        case TypeCode.Char: case TypeCode.String: type = Names.xsString; break;
        case TypeCode.DateTime:
        {
          DateTime dateTime = (DateTime)value;
          type = dateTime.Kind == DateTimeKind.Unspecified && dateTime.TimeOfDay.Ticks == 0 ? Names.xsDate : Names.xsDateTime;
          break;
        }
        case TypeCode.Decimal: type = Names.xsDecimal; break;
        case TypeCode.Double: type = Names.xsDouble; break;
        case TypeCode.Int16: type = Names.xsShort; break;
        case TypeCode.Int32: type = Names.xsInt; break;
        case TypeCode.Int64: type = Names.xsLong; break;
        case TypeCode.SByte: type = Names.xsSByte; break;
        case TypeCode.Single: type = Names.xsFloat; break;
        case TypeCode.UInt16: type = Names.xsUShort; break;
        case TypeCode.UInt32: type = Names.xsUInt; break;
        case TypeCode.UInt64: type = Names.xsULong; break;
        case TypeCode.Object:
          if(value is DateTimeOffset) type = Names.xsDateTime;
          else if(value is XmlDuration || value is TimeSpan) type = Names.xsDuration;
          else if(value is byte[]) type = Names.xsB64Binary;
          break;
      }
    }

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
    // if it's a type defined in xml schema (xs:), validate that the value is of that type
    if(value != null && type != null && type.Namespace.OrdinalEquals(Names.XmlSchema) && !builtInTypes.ContainsKey(property))
    {
      value = ValidateValueType(property, value, type);
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
    if(properties.Count == 0) throw new ContractViolationException("A PropFindResource must have at least one property.");

    // perform a more expensive check in debug mode to ensure that all requested properties were provided on all resources
    // TODO: should we remove the #if and do this check all the time?
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

  internal readonly Dictionary<XmlQualifiedName, PropertyValue> properties = new Dictionary<XmlQualifiedName, PropertyValue>();

  void SetValueCore(XmlQualifiedName property, object value, XmlQualifiedName type, string language)
  {
    if(property == null) throw new ArgumentNullException();

    // if the property is of a built-in type, validate that the value matches the expected type
    XmlQualifiedName expectedType;
    if(builtInTypes.TryGetValue(property, out expectedType))
    {
      // validate that the value matches the expected type
      if(expectedType != null)
      {
        value = ValidateValueType(property, value, expectedType);
      }
      else if(value != null)
      {
        if(property == Names.resourcetype)
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
        else if(property == Names.getetag)
        {
          if(!(value is EntityTag)) throw new ContractViolationException(property + " is expected to be an EntityTag.");
        }
        else if(property == Names.lockdiscovery)
        {
          if(!(value is ActiveLock) && !(value is IEnumerable<ActiveLock>))
          {
            throw new ContractViolationException(property + " is expected to be an ActiveLock or IEnumerable<ActiveLock>.");
          }
        }
        else if(property == Names.supportedlock)
        {
          if(!(value is LockType) && !(value is IEnumerable<LockType>))
          {
            throw new ContractViolationException(property + " is expected to be a LockType or IEnumerable<LockType>.");
          }
        }
      }

      type = null; // built-in properties shouldn't report their type (as per RFC 4316 section 5)
    }
    else if(type == Names.xsString)
    {
      type = null; // xs:string types should not be reported because that's the default (as per RFC 4316 section 5)
    }

    // save the property value. don't bother creating a PropertyValue object if it doesn't hold anything useful
    properties[property] = type == null && value == null && language == null ? null : new PropertyValue(type, value, language);
  }

  static bool IsInteger(object value)
  {
    switch(Type.GetTypeCode(value.GetType()))
    {
      case TypeCode.Byte: case TypeCode.Int16: case TypeCode.Int32: case TypeCode.Int64: case TypeCode.SByte: case TypeCode.UInt16:
      case TypeCode.UInt32: case TypeCode.UInt64:
        return true;
      case TypeCode.Decimal:
      {
        decimal d = (decimal)value;
        return d == decimal.Truncate(d);
      }
      case TypeCode.Double:
      {
        double d = (double)value;
        return d == Math.Truncate(d);
      }
      case TypeCode.Single:
      {
        float d = (float)value;
        return d == Math.Truncate(d);
      }
      default: return false;
    }
  }

  static long ValidateSignedInteger(XmlQualifiedName property, object value, long min, long max)
  {
    long intValue;
    try
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Decimal:
        {
          decimal d = (decimal)value, trunc = decimal.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = decimal.ToInt64(trunc);
          break;
        }
        case TypeCode.Double:
        {
          double d = (double)value, trunc = Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((long)trunc);
          break;
        }
        case TypeCode.Int16: intValue = (short)value; break;
        case TypeCode.Int32: intValue = (int)value; break;
        case TypeCode.Int64: intValue = (long)value; break;
        case TypeCode.SByte: intValue = (sbyte)value; break;
        case TypeCode.Single:
        {
          float d =  (float)value, trunc = (float)Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((long)trunc);
          break;
        }
        case TypeCode.UInt16: intValue = (ushort)value; break;
        case TypeCode.UInt32: intValue = (uint)value; break;
        case TypeCode.UInt64:
        {
          ulong v = (ulong)value;
          if(v > (ulong)long.MaxValue) goto failed;
          else intValue = (long)v;
          break;
        }
        default: goto failed;
      }

      if(intValue >= min && intValue <= max) return intValue;
    }
    catch(OverflowException)
    {
    }

    failed:
    throw new ContractViolationException(property.ToString() + " was expected to be an integer between " + min.ToInvariantString() +
                                         " and " + max.ToInvariantString() + " (inclusive), but was " + value.ToString());
  }

  static ulong ValidateUnsignedInteger(XmlQualifiedName property, object value, ulong max)
  {
    ulong intValue;
    try
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Decimal:
        {
          decimal d = (decimal)value, trunc = decimal.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = decimal.ToUInt64(trunc);
          break;
        }
        case TypeCode.Double:
        {
          double d = (double)value, trunc = Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((ulong)trunc);
          break;
        }
        case TypeCode.Int16: intValue = checked((ulong)(short)value); break;
        case TypeCode.Int32: intValue = checked((ulong)(int)value); break;
        case TypeCode.Int64: intValue = checked((ulong)(long)value); break;
        case TypeCode.SByte: intValue = checked((ulong)(sbyte)value); break;
        case TypeCode.Single:
        {
          float d =  (float)value, trunc = (float)Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((ulong)trunc);
          break;
        }
        case TypeCode.UInt16: intValue = (ushort)value; break;
        case TypeCode.UInt32: intValue = (uint)value; break;
        case TypeCode.UInt64: intValue = (ulong)value; break;
        default: goto failed;
      }

      if(intValue <= max) return intValue;
    }
    catch(OverflowException)
    {
    }

    failed:
    throw new ContractViolationException(property.ToString() + " was expected to be an integer between 0 and " + max.ToInvariantString() +
                                         " (inclusive), but was " + value.ToString());
  }

  static object ValidateValueType(XmlQualifiedName property, object value, XmlQualifiedName expectedType)
  {
    if(value == null)
    {
      return value;
    }
    else if(expectedType == Names.xsString)
    {
      if(!(value is string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
      return value;
    }
    if(expectedType == Names.xsDateTime || expectedType == Names.xsDate)
    {
      if(value is DateTime || value is DateTimeOffset) return value;
    }
    else if(expectedType == Names.xsInt)
    {
      return (int)ValidateSignedInteger(property, value, int.MinValue, int.MaxValue);
    }
    else if(expectedType == Names.xsULong)
    {
      return ValidateUnsignedInteger(property, value, ulong.MaxValue);
    }
    else if(expectedType == Names.xsLong)
    {
      return ValidateSignedInteger(property, value, long.MinValue, long.MaxValue);
    }
    else if(expectedType == Names.xsBoolean)
    {
      if(value is bool) return value;
    }
    else if(expectedType == Names.xsUri)
    {
      if(value is Uri) return value;
      Uri uri;
      if(value is string && Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) return uri;
    }
    else if(expectedType == Names.xsDouble)
    {
      if(value is double || value is float || IsInteger(value)) return Convert.ToDouble(value);
    }
    else if(expectedType == Names.xsFloat)
    {
      if(value is float || IsInteger(value)) return Convert.ToSingle(value);
    }
    else if(expectedType == Names.xsDecimal)
    {
      if(value is double || value is float || value is decimal || IsInteger(value)) return Convert.ToDecimal(value);
    }
    else if(expectedType == Names.xsUInt)
    {
      return (uint)ValidateUnsignedInteger(property, value, uint.MaxValue);
    }
    else if(expectedType == Names.xsShort)
    {
      return (short)ValidateSignedInteger(property, value, short.MinValue, short.MaxValue);
    }
    else if(expectedType == Names.xsUShort)
    {
      return (ushort)ValidateUnsignedInteger(property, value, ushort.MaxValue);
    }
    else if(expectedType == Names.xsUByte)
    {
      return (byte)ValidateUnsignedInteger(property, value, byte.MaxValue);
    }
    else if(expectedType == Names.xsSByte)
    {
      return (sbyte)ValidateSignedInteger(property, value, sbyte.MinValue, sbyte.MaxValue);
    }
    else if(expectedType == Names.xsDuration)
    {
      if(value is XmlDuration || value is TimeSpan) return value;
    }
    else if(expectedType == Names.xsB64Binary || expectedType == Names.xsHexBinary)
    {
      if(value is byte[]) return value;
    }
    else
    {
      return value; // we don't know how to validate it, so assume it's valid
    }

    throw new ContractViolationException(property + " is expected to be of type " + expectedType.ToString() + " but was of type " +
                                         value.GetType().FullName);
  }

  static readonly Dictionary<XmlQualifiedName, XmlQualifiedName> builtInTypes = new Dictionary<XmlQualifiedName, XmlQualifiedName>()
  {
    { Names.creationdate, Names.xsDateTime }, { Names.displayname, Names.xsString }, { Names.getcontentlanguage, Names.xsString },
    { Names.getcontentlength, Names.xsULong }, { Names.getcontenttype, Names.xsString }, { Names.getetag, null },
    { Names.getlastmodified, Names.xsDateTime }, { Names.lockdiscovery, null }, { Names.resourcetype, null }, { Names.supportedlock, null }
  };
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
    Language    = string.IsNullOrEmpty(language) ? null : language;
  }

  /// <summary>Gets the language of the value in <c>xml:lang</c> format, or null if no language is defined.</summary>
  public string Language { get; private set; }

  /// <summary>Gets the status representing the result of attempting to set the property. If null, the operation will be assumed
  /// to be successful and <see cref="ConditionCodes.OK"/> will be used.
  /// </summary>
  public ConditionCode Status { get; private set; }

  /// <summary>Gets the <c>xsi:type</c> of the property element, or null if no type was declared.</summary>
  public XmlQualifiedName Type { get; private set; }

  /// <summary>Gets the value of the </summary>
  public object Value { get; private set; }

  internal bool InferType { get; private set; }
}
#endregion

} // namespace HiA.WebDAV.Server
