using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

#region PropertyPatch
/// <summary>Represents a set of changes to be made to a resource's properties, either deleting properties or setting them.</summary>
public sealed class PropertyPatch
{
  internal PropertyPatch(bool remove)
  {
    Remove = remove ? new PropertyRemovalSet() : PropertyRemovalSet.Empty;
    Set    = remove ? PropertyPatchSet.Empty : new PropertyPatchSet();
  }

  #region PropertyPatchSet
  /// <summary>A dictionary mapping property names to <see cref="PropertyPatchValue"/> objects representing the new values for the
  /// named properties.
  /// </summary>
  public sealed class PropertyPatchSet : AccessLimitedDictionaryBase<XmlQualifiedName, PropertyPatchValue>
  {
    internal PropertyPatchSet() { }

    /// <inheritdoc/>
    public override bool IsReadOnly
    {
      get { return true; }
    }

    internal void Add(XmlQualifiedName name, PropertyPatchValue value)
    {
      Items.Add(name, value);
    }

    internal static readonly PropertyPatchSet Empty = new PropertyPatchSet();
  }
  #endregion

  #region PropertyRemovalSet
  /// <summary>A collection of <see cref="PropertyRemoval"/> objects representing properties to be removed from a resource.</summary>
  public sealed class PropertyRemovalSet : AccessLimitedCollectionBase<PropertyRemoval>
  {
    internal PropertyRemovalSet() { }

    /// <inheritdoc/>
    public override bool IsReadOnly
    {
      get { return true; }
    }

    /// <summary>Determines whether the collection contains a <see cref="PropertyRemoval"/> referring to a property with the given name.</summary>
    public bool Contains(XmlQualifiedName qname)
    {
      return qname != null && names != null && names.Contains(qname);
    }

    internal void Add(XmlQualifiedName qname)
    {
      if(qname == null || qname.IsEmpty) throw new ArgumentException("The name must not be null or empty.");
      if(names == null) names = new HashSet<XmlQualifiedName>();
      if(!names.Add(qname)) throw Exceptions.BadRequest("Duplicate property name " + qname.ToString());
      Items.Add(new PropertyRemoval(qname));
    }

    internal void AddRange(IEnumerable<XmlQualifiedName> names)
    {
      foreach(XmlQualifiedName name in names) Add(name);
    }

    internal static readonly PropertyRemovalSet Empty = new PropertyRemovalSet();

    HashSet<XmlQualifiedName> names;
  }
  #endregion

  /// <summary>Gets a collection containing the names of properties that should be deleted by this property patch.</summary>
  /// <remarks>Only one of <see cref="Remove"/> or <see cref="Set"/> will contain items. The other will be empty.</remarks>
  public PropertyRemovalSet Remove { get; private set; }

  /// <summary>Gets a dictionary containing the names and values of properties that should be set by this property patch.</summary>
  /// <remarks>Only one of <see cref="Remove"/> or <see cref="Set"/> will contain items. The other will be empty.</remarks>
  public PropertyPatchSet Set { get; private set; }
}
#endregion

#region PropertyPatchValue
/// <summary>Represents a value submitted by a client in a <c>PROPPATCH</c> request and the status of the property setting.</summary>
public sealed class PropertyPatchValue
{
  internal PropertyPatchValue(XmlElement propertyElement, XmlQualifiedName type, object parsedValue, bool hasParsedValue, string language)
  {
    Element     = propertyElement;
    Type        = type;
    ParsedValue = parsedValue;
    Language    = language;
    HasValue    = hasParsedValue;
  }

  /// <summary>Gets the <see cref="XmlElement"/> of the property sent within the request body.</summary>
  public XmlElement Element { get; private set; }

  /// <summary>Gets the <c>xsi:type</c> of the property element, or null if no type was declared.</summary>
  public XmlQualifiedName Type { get; private set; }

  /// <summary>Gets the parsed value of the element. This property is only valid if <see cref="HasValue"/> is true.</summary>
  public object ParsedValue { get; private set; }

  /// <summary>Gets the value of the <c>xml:lang</c> attribute as inherited by the <see cref="Element"/>, or null if no language has been
  /// defined.
  /// </summary>
  public string Language { get; private set; }

  /// <summary>Gets or sets the status representing the result of attempting to set the property. If null, the operation will be assumed
  /// to be successful and <see cref="ConditionCodes.OK"/> will be used.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <summary>Gets whether <see cref="ParsedValue"/> is valid and usable.</summary>
  public bool HasValue { get; private set; }
}
#endregion

#region PropPatchRequest
/// <summary>Represents a <c>PROPPATCH</c> request.</summary>
/// <remarks>The <c>PROPPATCH</c> request is described in section 9.2 of RFC 4918.</remarks>
public class PropPatchRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="PropPatchRequest"/> based on a new WebDAV request.</summary>
  public PropPatchRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.Self; // see ParseRequest() for details
    Patches = new PropertyPatchCollection();
  }

  #region PropertyPatchCollection
  /// <summary>A list of <see cref="PropertyPatch"/> objects representing the patches that should be applied to a resource's properties.
  /// The patches must be applied in the order in which they exist in the list.
  /// </summary>
  public sealed class PropertyPatchCollection : AccessLimitedCollectionBase<PropertyPatch>
  {
    internal PropertyPatchCollection() { }

    /// <inheritdoc/>
    public override bool IsReadOnly
    {
      get { return true; }
    }

    internal void Add(PropertyPatch patch)
    {
      Items.Add(patch);
    }
  }
  #endregion

  /// <summary>Gets a collection containing the property patches that should be applied, in the order in which they must be applied.</summary>
  public PropertyPatchCollection Patches { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources.</remarks>
  protected override ConditionCode CheckSubmittedLockTokens()
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, false, false);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // disallow recursive PROPPATCH requests. this behavior of PROPPATCH with respect to the Depth property doesn't seem to be specified
    // precisely in RFC 4918, but it does say in section 9.2 that it operates on "the resource identified by the Request-URI", as opposed
    // to what it says in section 9.1 for the PROPFIND method, which is that it operates on "the resource identified by the Request-URI
    // and potentially its member resources", so we'll assume that means it never operates recursively
    if(Depth != Depth.Self) throw Exceptions.BadRequest("The Depth header must be 0 or unspecified for PROPPATCH requests.");

    XmlDocument xml = Context.LoadRequestXml();
    if(xml == null) throw Exceptions.BadRequest("The request body was missing."); // an XML body is required by RFC 4918 section 9.2
    xml.DocumentElement.AssertName(DAVNames.propertyupdate); // and it has to be a <propertyupdate> body

    // parse all of the property patches
    bool hasBadValue = false; // whether we encountered a property with an unparsable value
    foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements())
    {
      if(child.HasName(DAVNames.set)) // if the patch should set new or existing properties...
      {
        PropertyPatch patch = new PropertyPatch(false);
        XmlElement props = child.GetChild(DAVNames.prop); // the element containing individual property elements
        string lang = props.GetInheritedAttributeValue(DAVNames.xmlLang); // get the default language for properties in the set
        foreach(XmlElement prop in props.EnumerateChildElements())
        {
          // if a type was specified, parse the value as an instance of that type
          string typeName = prop.GetAttribute(DAVNames.xsiType);
          XmlQualifiedName type = null;
          object parsedValue = null;
          bool hasParsedValue = false;
          if(!string.IsNullOrEmpty(typeName)) // if a type name was specified...
          {
            type = prop.ParseQualifiedName(typeName);
            // if the property has a text value and a type that we may recognize (i.e. one in the xs: namespace), try to parse it...
            if(prop.ChildNodes.Count == 1 && prop.ChildNodes[0].NodeType == XmlNodeType.Text &&
               type.Namespace.OrdinalEquals(DAVNames.XmlSchema))
            {
              bool knownType;
              hasParsedValue = TryParseValue(prop.ChildNodes[0].Value, type, out parsedValue, out knownType);
              if(!hasParsedValue && knownType) // if we couldn't parse the value but we knew how it should be parsed...
              {
                parsedValue = BadValue; // mark this property as having a bad value
                hasBadValue = true;     // and remember that a value was unparsable so we can emit an error response
              }
            }
          }
          else if(!prop.HasChildNodes) // if the node was empty...
          {
            hasParsedValue = true; // then it always has the value null
          }

          try
          {
            patch.Set.Add(prop.GetQualifiedName(), new PropertyPatchValue(prop, type, parsedValue, hasParsedValue,
                                                                          prop.GetAttributeValue("xml:lang", lang)));
          }
          catch(ArgumentException) // an ArgumentException will be thrown if the property name already exists
          {
            throw Exceptions.BadRequest("Duplicate property name " + prop.GetQualifiedName().ToString());
          }
        }
        Patches.Add(patch);
      }
      else if(child.HasName(DAVNames.remove)) // if the patch should remove properties...
      {
        PropertyPatch patch = new PropertyPatch(true);
        patch.Remove.AddRange(child.GetChild(DAVNames.prop).EnumerateChildElements().Select(XmlNodeExtensions.GetQualifiedName));
        Patches.Add(patch);
      }
    }

    // now we've parsed the request. if an unparsable value was encountered, issue an error result immediately
    if(hasBadValue)
    {
      // for unparsable values, use 422 Unprocessable Entity. i tend to think that 400 Bad Request would be more fitting, since 422
      // Unprocessable Entity is supposed to be used for syntactically correct requests that are nonetheless unprocessable (as per RFC 4918
      // section 11.2), and an ill-formed value isn't syntactically correct. (you could argue that it's syntactically correct XML, and
      // only semantically incorrect, but i'd respond that RFC 4918 uses 400 Bad Request for cases where XML is syntactically correct but
      // wrong.) however, RFC 4316 section 4.2 uses 422 Unprocessable Entity in an example reply to a PROPPATCH request with an unparsable
      // value. while it doesn't specify in the text that 422 should be used, clearly the example implies it. on the other hand, RFC 4316
      // seems to be a bit poorly designed and written compared to RFC 4918, which seems to prefer 400 Bad Request. nonetheless, i will
      // follow the example of RFC 4316 and use 422 Unprocessable Entity, since that is the RFC that defines the type extensions
      ConditionCode badValueCode = new ConditionCode(422, "The values were not formatted correctly for their stated types.");

      // simply set the Status codes and call WriteResponseCore to write the response as usual
      foreach(PropertyPatch patch in Patches)
      {
        foreach(PropertyRemoval removal in patch.Remove) removal.Status = ConditionCodes.FailedDependency;
        foreach(PropertyPatchValue value in patch.Set.Values)
        {
          value.Status = value.ParsedValue == BadValue ? badValueCode : ConditionCodes.FailedDependency;
        }
      }

      WriteResponseCore();
      Context.Response.End(); // prevent the service from being invoked to process the request
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal override void WriteResponse()
  {
    if(Status != null) Context.WriteStatusResponse(Status);
    else WriteResponseCore();
  }

  void WriteResponseCore()
  {
    // collect the namespaces used in the response. while we're at it, also group the property names by status code
    HashSet<string> namespaces = new HashSet<string>();
    var namesByStatus = new MultiValuedDictionary<ConditionCode, XmlQualifiedName>();
    foreach(PropertyPatch patch in Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove)
      {
        namespaces.Add(removal.Name.Namespace);
        namesByStatus.Add(removal.Status ?? ConditionCodes.OK, removal.Name);
      }
      foreach(KeyValuePair<XmlQualifiedName,PropertyPatchValue> pair in patch.Set)
      {
        namespaces.Add(pair.Key.Namespace);
        namesByStatus.Add(pair.Value.Status ?? ConditionCodes.OK, pair.Key);
      }
    }

    using(MultiStatusResponse response = Context.OpenMultiStatusResponse(namespaces))
    {
      XmlWriter writer = response.Writer;
      writer.WriteStartElement(DAVNames.response.Name);
      writer.WriteElementString(DAVNames.href.Name, Context.ServiceRoot + Context.RequestPath);

      foreach(KeyValuePair<ConditionCode, List<XmlQualifiedName>> pair in namesByStatus)
      {
        writer.WriteStartElement(DAVNames.propstat.Name);
        writer.WriteStartElement(DAVNames.prop.Name);
        foreach(XmlQualifiedName name in pair.Value) writer.WriteEmptyElement(name);
        writer.WriteEndElement(); // </prop>
        response.WriteStatus(pair.Key);
        writer.WriteEndElement(); // </propstat>
      }

      writer.WriteEndElement(); // </response>
    }
  }

  static bool TryParseValue(string str, XmlQualifiedName expectedType, out object value, out bool knownType)
  {
    value     = null;
    knownType = true;

    if(string.IsNullOrEmpty(str))
    {
      knownType = false; // if we didn't examine the type, then we can't say that it's known
      return true;
    }
    else if(expectedType == DAVNames.xsString)
    {
      value = str;
      return true;
    }
    if(expectedType == DAVNames.xsDateTime || expectedType == DAVNames.xsDate)
    {
      return XmlUtility.TryParseDateTime(str, out value);
    }
    else if(expectedType == DAVNames.xsInt)
    {
      int intValue;
      if(!InvariantCultureUtility.TryParse(str, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsULong)
    {
      ulong intValue;
      if(!InvariantCultureUtility.TryParse(str, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsLong)
    {
      long intValue;
      if(!InvariantCultureUtility.TryParse(str, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsBoolean)
    {
      bool boolValue;
      if(!XmlUtility.TryParse(str, out boolValue)) return false;
      value = boolValue;
      return true;
    }
    else if(expectedType == DAVNames.xsUri)
    {
      Uri uri;
      if(!Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) return false;
      value = uri;
      return true;
    }
    else if(expectedType == DAVNames.xsDouble)
    {
      double doubleValue;
      if(!InvariantCultureUtility.TryParse(str, out doubleValue)) return false;
      value = doubleValue;
      return true;
    }
    else if(expectedType == DAVNames.xsFloat)
    {
      float floatValue;
      if(!InvariantCultureUtility.TryParse(str, out floatValue)) return false;
      value = floatValue;
      return true;
    }
    else if(expectedType == DAVNames.xsDecimal)
    {
      decimal decimalValue;
      if(!decimal.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out decimalValue)) return false;
      value = decimalValue;
      return true;
    }
    else if(expectedType == DAVNames.xsUInt)
    {
      uint intValue;
      if(!InvariantCultureUtility.TryParse(str, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsShort)
    {
      short intValue;
      if(!short.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsUShort)
    {
      ushort intValue;
      if(!ushort.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsUByte)
    {
      byte intValue;
      if(!byte.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsSByte)
    {
      sbyte intValue;
      if(!sbyte.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == DAVNames.xsDuration)
    {
      XmlDuration duration;
      if(!XmlDuration.TryParse(str, out duration)) return false;
      value = duration;
      return true;
    }
    else if(expectedType == DAVNames.xsB64Binary)
    {
      try { value = Convert.FromBase64String(str); }
      catch(FormatException) { return false; }
      return true;
    }
    else if(expectedType == DAVNames.xsHexBinary)
    {
      byte[] binary;
      if(!BinaryUtility.TryParseHex(str, out binary)) return false;
      value = binary;
      return true;
    }
    else
    {
      knownType = false;
    }

    return false;
  }

  static readonly object BadValue = new object(); // a singleton representing a value that couldn't be parsed
}
#endregion

#region PropertyRemoval
/// <summary>Represents a property that should be removed by a <see cref="PropertyPatch"/>, and the resulting status of the removal.</summary>
public sealed class PropertyRemoval
{
  internal PropertyRemoval(XmlQualifiedName name)
  {
    Name = name;
  }

  /// <summary>Gets the name of the property to be removed from the resource.</summary>
  public XmlQualifiedName Name { get; private set; }

  /// <summary>Gets or sets the status of the removal as a <see cref="ConditionCode"/>. If null, the removal will be assumed to be
  /// successful and <see cref="ConditionCodes.OK"/> will be used.
  /// </summary>
  public ConditionCode Status { get; set; }
}
#endregion

} // namespace HiA.WebDAV.Server
