using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

// TODO: add dead properties, xml data types, etc.

namespace HiA.WebDAV
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

#region EntityTag
/// <summary>Represents an HTTP entity tag. (See the description of entity tags in RFC 2616 for more details.)</summary>
public sealed class EntityTag : IElementValue
{
  /// <summary>Initializes a new <see cref="EntityTag"/>.</summary>
  /// <param name="tag">The entity tag. This is an arbitrary string value that represents the state of a resource's content, such that
  /// identical tag values represent either identical or equivalent content, depending on the value of the <paramref name="isWeak"/>
  /// parameter.
  /// </param>
  /// <param name="isWeak">If false, this represents a strong entity tag, where entities may have the same tag only if they are
  /// byte-for-byte identical. If true, this represents a weak entity tag, where entities may have the same tag as long as they could be
  /// swapped with no significant change in semantics.
  /// </param>
  public EntityTag(string tag, bool isWeak)
  {
    if(tag == null) throw new ArgumentNullException();
    Tag    = tag;
    IsWeak = isWeak;
  }

  /// <summary>Gets the entity tag string.</summary>
  public string Tag { get; private set; }
  /// <summary>If true, this represents a weak entity tag. If false, it represents a strong entity tag.</summary>
  public bool IsWeak { get; private set; }

  IEnumerable<string> IElementValue.GetNamespaces()
  {
    return null;
  }

  void IElementValue.WriteValue(XmlWriter writer)
  {
    string value = DAVUtility.QuoteString(Tag);
    if(IsWeak) value = "W/" + value;
    writer.WriteString(value);
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
/// <summary>Represents a standard <c>PROPFIND</c> request.</summary>
public class PropFindRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="PropFindRequest"/>.</summary>
  public PropFindRequest(WebDAVContext context) : base(context)
  {
    Properties = new RequestedPropertyCollection();
    Resources  = new PropFindResourceCollection();
  }

  #region RequestedPropertyCollection
  /// <summary>A collection containing the properties specifically request by the client.</summary>
  public sealed class RequestedPropertyCollection : AccessLimitedCollectionBase<XmlQualifiedName>
  {
    internal RequestedPropertyCollection() { }

    /// <inheritdoc/>
    public new bool Contains(XmlQualifiedName qname)
    {
      return qname != null && names != null && names.Contains(qname);
    }

    internal void Add(XmlQualifiedName qname)
    {
      if(qname == null || qname.IsEmpty) throw new ArgumentException("The name must not be null or empty.");
      if(names == null) names = new HashSet<XmlQualifiedName>();
      if(!names.Add(qname)) throw Exceptions.BadRequest("Duplicate property name " + qname.ToString());
      Items.Add(qname);
    }

    HashSet<XmlQualifiedName> names;
  }
  #endregion

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
  public RequestedPropertyCollection Properties { get; private set; }
  /// <summary>Gets a collection into which the <see cref="IWebDAVService"/> should place the resources (and their properties) when
  /// servicing the request.
  /// </summary>
  public PropFindResourceCollection Resources { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // RFC4918 section 9.1 says PROPFIND should treat unspecified Depths as though infinity was specified
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants;

    // the body of the request must either be empty (in which case we default to an allprop request) or an XML fragment describing the
    // properties desired
    XmlDocument xml = Context.LoadBodyXml();
    if(xml == null) // if the body was empty...
    {
      Flags = PropFindFlags.IncludeAll; // default to an allprops match
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
    HashSet<string> namespaces = new HashSet<string>();
    namespaces.Add(Names.DAV);
    namespaces.Add(Names.XmlSchemaInstance);
    foreach(PropFindResource resource in Resources) resource.Validate(this, namespaces);

    Context.Response.StatusCode        = 207;
    Context.Response.StatusDescription = DAVUtility.GetStatusCodeMessage(207);
    Context.Response.ContentEncoding   = System.Text.Encoding.UTF8;
    XmlWriterSettings settings = new XmlWriterSettings()
    {
      CloseOutput = false, Encoding = Context.Response.ContentEncoding, Indent = true, IndentChars = "  ", OmitXmlDeclaration = true
    };
    using(XmlWriter writer = XmlWriter.Create(Context.Response.OutputStream, settings))
    {
      writer.WriteStartElement(Names.multistatus);
      int index = 0;
      foreach(string ns in namespaces)
      {
        string prefix = ns.OrdinalEquals(Names.DAV) ? null :
                        ns.OrdinalEquals(Names.XmlSchemaInstance) ? "xsi" :
                        ns.OrdinalEquals(Names.XmlSchema) ? "xs" : MakeNamespaceName(index++);
        if(prefix == null) writer.WriteAttributeString("xmlns", ns);
        else writer.WriteAttributeString("xmlns", prefix, null, ns);
      }

      var valuesByStatus = new MultiValuedDictionary<ConditionCode, KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>();
      foreach(PropFindResource resource in Resources)
      {
        writer.WriteStartElement(Names.response.Name);
        writer.WriteElementString(Names.href.Name, Context.ServiceRoot + resource.RelativePath);

        // collect the properties by status
        valuesByStatus.Clear();
        foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> pair in resource.properties)
        {
          valuesByStatus.Add(pair.Value == null ? ConditionCodes.OK : pair.Value.Code ?? ConditionCodes.OK, pair);
        }

        // then, output a propstat element for each status
        foreach(KeyValuePair<ConditionCode, List<KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue>>> spair in valuesByStatus)
        {
          writer.WriteStartElement(Names.propstat.Name);

          writer.WriteStartElement(Names.prop);
          foreach(KeyValuePair<XmlQualifiedName, PropFindResource.PropertyValue> ppair in spair.Value)
          {
            writer.WriteStartElement(ppair.Key);
            if(ppair.Value != null)
            {
              XmlQualifiedName type = ppair.Value.Type;
              if(type != null && type != Names.xsString)
              {
                writer.WriteAttributeString(Names.xsiType, StringUtility.Combine(":", writer.LookupPrefix(type.Namespace), type.Name));
              }

              object value = ppair.Value.Value;
              if(value != null)
              {
                IElementValue elementValue = value as IElementValue;
                System.Collections.IEnumerable elementValues = elementValue == null ? GetElementValuesEnumerable(value) : null;
                if(elementValue != null)
                {
                  elementValue.WriteValue(writer);
                }
                else if(elementValues != null)
                {
                  foreach(IElementValue elemValue in elementValues) elemValue.WriteValue(writer);
                }
                else if(type != null && value is byte[]) // if it's a byte array, write a base64 array or hex array depending on the type
                {
                  byte[] binaryValue = (byte[])value;
                  if(type == Names.xsHexBinary) writer.WriteString(BinaryUtility.ToHex(binaryValue));
                  else writer.WriteBase64(binaryValue, 0, binaryValue.Length);
                }
                else if(type == Names.xsDate)
                {
                  if(value is DateTime) writer.WriteDate((DateTime)value);
                  else if(value is DateTimeOffset) writer.WriteDate(((DateTimeOffset)value).Date);
                  else writer.WriteValue(value);
                }
                else
                {
                  writer.WriteValue(value);
                }
              }
            }
            writer.WriteEndElement(); // property name (i.e. ppair.Key)
          }

          writer.WriteEndElement(); // prop

          writer.WriteElementString(Names.status.Name, spair.Key.DAVStatusText);
          spair.Key.WriteErrorElement(writer);
          if(!string.IsNullOrEmpty(spair.Key.Message)) writer.WriteElementString(Names.responsedescription.Name, spair.Key.Message);

          writer.WriteEndElement(); // propstat
        }

        writer.WriteEndElement(); // response
      }
      writer.WriteEndElement(); // multistatus
    }
  }

  internal static System.Collections.IEnumerable GetElementValuesEnumerable(object value)
  {
    System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
    if(enumerable != null && !(value is string))
    {
      if(value is IEnumerable<IElementValue> || value is IEnumerable<ResourceType> || value is IEnumerable<ActiveLock> ||
         value is IEnumerable<LockType>) // if it's a known IEnumerable<T>...
      {
        return enumerable; // return it
      }
      else // otherwise...
      {
        Type type = value.GetType();
        if(type.IsGenericType) // see if it's an IEnumerable<T> where T implements IElementValue
        {
          type = type.GetGenericTypeDefinition();
          Type[] typeArgs = type.GetGenericArguments();
          if(typeArgs.Length == 1 && typeof(IElementValue).IsAssignableFrom(typeArgs[0])) return enumerable; // if so, return it
        }
      }
    }

    return null;
  }

  static string MakeNamespaceName(int i)
  {
    const string letters = "abcdefghijklmnopqrstuvwxyz";
    return i < letters.Length ? new string(letters[i], 1) : "ns" + i.ToInvariantString();
  }
}
#endregion

#region PropFindResource
/// <summary>Represents a resource whose properties will be returned in response to a <c>PROPFIND</c> request.</summary>
public sealed class PropFindResource
{
  /// <summary>Initializes a new <see cref="PropFindResource"/> given the canonical path to the resource, relative to the
  /// <see cref="WebDAVContext.ServiceRoot"/>.
  /// </summary>
  public PropFindResource(string canonicalRelativePath)
  {
    if(canonicalRelativePath == null) throw new ArgumentNullException();
    RelativePath = canonicalRelativePath;
  }

  /// <summary>Gets the canonical path to the resource, relative to the <see cref="WebDAVContext.ServiceRoot"/>.</summary>
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

  /// <summary>Sets the value of a property on the resource. If the value is not null and the property is not a built-in WebDAV property
  /// (i.e. one defined in RFC 4918), the property data type will be inferred from the value. If you do not want this inference to occur,
  /// call <see cref="SetValue(XmlQualifiedName,object,XmlQualifiedName)"/> and pass the correct type or null if you don't want any type
  /// information to be reported.
  /// </summary>
  public void SetValue(XmlQualifiedName property, object value)
  {
    XmlQualifiedName type = null;
    if(value != null && builtInTypes.ContainsKey(property))
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
          else if(value is TimeSpan) type = Names.xsDuration;
          break;
      }
    }

    SetValueCore(property, value, type);
  }

  /// <summary>Sets the value and type of a property on the resource.</summary>
  /// <remarks>
  /// If the property is a built-in WebDAV property (i.e. one defined in RFC 4918), the specified type will be ignored as per RFC 4316
  /// which states in section 5 that the property must not have a data type already defined in the WebDAV specification.
  /// If the value is not null and the property data type is known, the value will be validated against the the data type. Currently, the
  /// known property types are the types of built-in WebDAV properties as well as most types defined in the
  /// <c>http://www.w3.org/2001/XMLSchema</c> namespace (e.g. xs:boolean, xs:int, etc).
  /// </remarks>
  public void SetValue(XmlQualifiedName property, object value, XmlQualifiedName type)
  {
    if(property == null) throw new ArgumentNullException();
    // if it's a type defined in xml schema (xs:), validate that the value is of that type
    if(value != null && type != null && type.Namespace.OrdinalEquals(Names.XmlSchema) && !builtInTypes.ContainsKey(property))
    {
      value = ValidateValueType(property, value, type);
    }
    SetValueCore(property, value, type);
  }

  /// <summary>Represents the value, type, and status code of an attempt to retrieve a property on a resource.</summary>
  internal sealed class PropertyValue
  {
    public object Value;
    public XmlQualifiedName Type;
    public ConditionCode Code;
  }

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

  void SetValueCore(XmlQualifiedName property, object value, XmlQualifiedName type)
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

    properties[property] = new PropertyValue() { Code = null, Type = type, Value = value };
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
      if(value is TimeSpan) return value;
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

} // namespace HiA.WebDAV
