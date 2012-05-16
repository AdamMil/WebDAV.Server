using System.Linq;
using System.Xml;

namespace HiA.WebDAV
{

// TODO: add processing examples and documentation

#region PropertyPatch
public sealed class PropertyPatch
{
  internal PropertyPatch(bool remove)
  {
    Remove = remove ? new PropertyNameSet() : PropertyNameSet.Empty;
    Set    = remove ? PropertyPatchSet.Empty : new PropertyPatchSet();
  }

  #region PropertyPatchSet
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

  public PropertyNameSet Remove { get; private set; }
  public PropertyPatchSet Set { get; private set; }
}
#endregion

#region PropertyPatchValue
public sealed class PropertyPatchValue
{
  public PropertyPatchValue(object value, XmlQualifiedName type, string language)
  {
    Value    = value;
    Type     = type;
    Language = language;
  }

  public readonly object Value;
  public readonly XmlQualifiedName Type;
  public readonly string Language;
}
#endregion

#region PropPatchRequest
/// <summary>Represents a standard <c>PROPPATCH</c> request.</summary>
/// <remarks>The <c>PROPPATCH</c> request is described in section 9.2 of RFC 4918.</remarks>
public class PropPatchRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="PropPatchRequest"/>.</summary>
  public PropPatchRequest(WebDAVContext context) : base(context)
  {
    Patches = new PropertyPatchCollection();
  }

  #region PropertyPatchCollection
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

  public PropertyPatchCollection Patches { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // disallow recursive PROPPATCH requests. this behavior of PROPPATCH with respect to the Depth property doesn't seem to be specified
    // precisely in RFC 4918, but it does say in section 9.2 that it operates on "the resource identified by the Request-URI", as opposed
    // to what it says in section 9.1 for the PROPFIND method, which is that it operates on "the resource identified by the Request-URI
    // and potentially its member resources"
    if(Depth == Depth.SelfAndChildren || Depth == Depth.SelfAndDescendants)
    {
      throw Exceptions.BadRequest("The Depth header must be 0 or unspecified for PROPPATCH requests.");
    }

    XmlDocument xml = Context.LoadBodyXml();
    if(xml == null) throw Exceptions.BadRequest("The request body was missing."); // an XML body is required by RFC 4918 section 9.2
    xml.DocumentElement.AssertName(Names.propertyupdate); // and it has to be a <propertyupdate> body

    foreach(XmlElement child in xml.DocumentElement.EnumerateElements())
    {
      if(child.HasName(Names.set))
      {
        PropertyPatch patch = new PropertyPatch(false);
        foreach(XmlElement prop in child.GetChild(Names.prop).EnumerateElements())
        {
          // if a type was specified, parse the value as an instance of that type
          string typeName = prop.GetAttribute(Names.xsiType);
          XmlQualifiedName type = null;
          if(!string.IsNullOrEmpty(typeName)) // if a type name was specified...
          {
            type = prop.ParseQualifiedName(typeName);
          }
        }
        Patches.Add(patch);
      }
      else if(child.HasName(Names.remove))
      {
        PropertyPatch patch = new PropertyPatch(true);
        patch.Remove.AddRange(child.GetChild(Names.prop).EnumerateElements().Select(XmlNodeExtensions.GetQualifiedName));
        Patches.Add(patch);
      }
    }

    throw new System.NotImplementedException();
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal override void WriteResponse()
  {
    throw new System.NotImplementedException();
  }
}
#endregion

} // namespace HiA.WebDAV
