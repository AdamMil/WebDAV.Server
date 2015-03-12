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

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
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
      return qname != null && nameSet != null && nameSet.Contains(qname);
    }

    internal void Add(XmlQualifiedName qname)
    {
      if(qname == null || qname.IsEmpty) throw new ArgumentException("The name must not be null or empty.");
      if(nameSet == null) nameSet = new HashSet<XmlQualifiedName>();
      if(!nameSet.Add(qname)) throw Exceptions.BadRequest("Duplicate property name " + qname.ToString());
      Items.Add(new PropertyRemoval(qname));
    }

    internal void AddRange(IEnumerable<XmlQualifiedName> names)
    {
      foreach(XmlQualifiedName name in names) Add(name);
    }

    internal static readonly PropertyRemovalSet Empty = new PropertyRemovalSet();

    HashSet<XmlQualifiedName> nameSet;
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
  internal PropertyPatchValue(XmlElement propertyElement, XmlProperty property, ConditionCode status)
  {
    Element  = propertyElement;
    Property = property;
    Status   = status;
  }

  /// <summary>Gets the <see cref="XmlElement"/> of the property sent within the request body.</summary>
  public XmlElement Element { get; private set; }

  /// <summary>Gets an <see cref="XmlProperty"/> representing the parsed element value (if it could be parsed).</summary>
  public XmlProperty Property { get; private set; }

  /// <summary>Gets or sets the status representing the result of attempting to set the property. If null, the removal will be assumed to
  /// have been successful if no other errors occurred, and will be assumed to have failed if any other property changes failed. (RFC 4918
  /// section 9.2 requires that all property changes succeed or fail together.)
  /// </summary>
  public ConditionCode Status { get; set; }
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

  /// <summary>Performs standard processing of a <c>PROPPATCH</c> request. Only dead properties outside the WebDAV namespace can be set.</summary>
  public void ProcessStandardRequest()
  {
    ProcessStandardRequest(null);
  }

  /// <summary>Performs standard processing of a <c>PROPPATCH</c> request. Only dead properties outside the WebDAV namespace can be set.</summary>
  /// <param name="canonicalPath">The canonical, relative path of the resource whose properties will be modified. If null, the path to the
  /// <see cref="WebDAVContext.RequestResource"/> will be used, if it's available. If null and the request resource is not available, an
  /// exception will be thrown.
  /// </param>
  public void ProcessStandardRequest(string canonicalPath)
  {
    ProcessStandardRequest(canonicalPath, null, null, null, null, null, null);
  }

  /// <summary>Performs standard processing of a <c>PROPPATCH</c> request.</summary>
  /// <param name="canonicalPath">The canonical, relative path of the resource whose properties will be modified. If null, the path to the
  /// <see cref="WebDAVContext.RequestResource"/> will be used, if it's available. If null and the request resource is not available, an
  /// exception will be thrown.
  /// </param>
  /// <param name="protectedProperties">A set of properties that the client is not allowed to set, in addition to those that are required
  /// to be protected by the WebDAV protocol. If null, only the minimum set of properties will be protected (but you can also use
  /// <paramref name="canSetProperty"/> and <paramref name="canRemoveProperty"/> to restrict access to protected properties).
  /// </param>
  /// <param name="canSetProperty">A function that takes a property name and value, and determines whether the service can set the named
  /// property to that value. If the function returns a <see cref="ConditionCode"/> indicating an error, the property will not be set.
  /// If the function returns a <see cref="ConditionCode"/> indicating success (typically <see cref="ConditionCodes.OK"/>), that
  /// represents a promise that a subsequent call to <paramref name="setProperty"/> will succeed. If the function returns null, that
  /// indicates that the named property is not handled by the service, in which case if the name does not have the WebDAV namespace it
  /// will be considered to be a dead property. If the function is null, only dead properties can be set.
  /// </param>
  /// <param name="canRemoveProperty">A function that takes a property name and determines whether the service can removed the named
  /// property. If the function returns a <see cref="ConditionCode"/> indicating an error, the property will not be removed. If the
  /// function returns a <see cref="ConditionCode"/> indicating success (typically <see cref="ConditionCodes.OK"/>), that represents a
  /// promise that a subsequent call to <paramref name="removeProperty"/> will succeed. If the function returns null, that indicates that
  /// the named property is not handled by the service, in which case if the name does not have the WebDAV namespace it will be considered
  /// to be a dead property. If the function is null, only dead properties can be removed.
  /// </param>
  /// <param name="setProperty">A function that takes a property name and value, and sets the named property to the given value. If the
  /// value was set successfully, the function should return a <see cref="ConditionCode"/> indicating success (typically
  /// <see cref="ConditionCodes.OK"/>). If setting the property failed, the function should return a <see cref="ConditionCode"/> describing
  /// the failure. If the property is not handled by the service, the function should return null, in which case the property will be
  /// treated as a dead property if it's not in the WebDAV namespace. This function can only be null if <paramref name="canSetProperty"/>
  /// never returns success.
  /// </param>
  /// <param name="removeProperty">A function that takes a property name and removes the named property. If the property was removed
  /// successfully, the function should return a <see cref="ConditionCode"/> indicating success (typically
  /// <see cref="ConditionCodes.OK"/>). If removing the property failed, the function should return a <see cref="ConditionCode"/>
  /// describing the failure. If the property is not handled by the service, the function should return null, in which case the property
  /// will be treated as a dead property if it's not in the WebDAV namespace. This function can only be null if
  /// <paramref name="canRemoveProperty"/> never returns success.
  /// </param>
  /// <param name="applyChanges">A method that is called if all changes have been applied successfully. This should be used by services
  /// whose <paramref name="setProperty"/> and <paramref name="removeProperty"/> functions work transactionally, in which case
  /// <paramref name="applyChanges"/> provides the signal that the changes should be committed.
  /// </param>
  public void ProcessStandardRequest(string canonicalPath, HashSet<XmlQualifiedName> protectedProperties,
                                     Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> canSetProperty,
                                     Func<XmlQualifiedName,ConditionCode> canRemoveProperty,
                                     Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> setProperty,
                                     Func<XmlQualifiedName,ConditionCode> removeProperty,
                                     Action applyChanges)
  {
    if(canonicalPath == null && Context.RequestResource == null)
    {
      throw new ArgumentException("A path must be provided if there is no request resource.");
    }

    // first, check preconditions to see whether we're allowed to execute the request
    ConditionCode precondition = CheckPreconditions(null, canonicalPath);
    if(precondition != null) // if we shouldn't execute the request...
    {
      Status = precondition;
      return;
    }

    // by default, only allow setting properties outside the WebDAV namespace, which we'll treat as dead properties
    if(canSetProperty == null) canSetProperty = (name, prop) => DAVUtility.IsDAVName(name) ? ConditionCodes.Forbidden : null;
    if(canRemoveProperty == null) canRemoveProperty = name => DAVUtility.IsDAVName(name) ? ConditionCodes.Forbidden : null;

    // first verify that all of the values are valid
    bool hadError = false;
    foreach(PropertyPatch patch in Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove)
      {
        ConditionCode status = IsProtected(removal.Name, protectedProperties) ? ConditionCodes.CannotModifyProtectedProperty
                                                                              : canRemoveProperty(removal.Name);
        if(status != null && status.IsError)
        {
          removal.Status = status;
          hadError       = true;
        }
      }

      foreach(KeyValuePair<XmlQualifiedName,PropertyPatchValue> pair in patch.Set)
      {
        ConditionCode status = pair.Value.Status;
        if(status != null && status.IsError) // if it already had an error status (from parsing the request), use it...
        {
          hadError = true;
        }
        else // otherwise, make sure it's not protected or otherwise forbidden
        {
          status = IsProtected(pair.Key, protectedProperties) ? ConditionCodes.CannotModifyProtectedProperty
                                                              : canSetProperty(pair.Key, pair.Value);
          if(status == null && Context.PropertyStore == null) status = ConditionCodes.Forbidden; // can't set dead properties without a store
          if(status != null && status.IsError)
          {
            pair.Value.Status = status;
            hadError          = true;
          }
        }
      }
    }

    if(!hadError) // if the everything should be able to succeed...
    {
      if(canonicalPath == null) canonicalPath = Context.RequestResource.CanonicalPath;
      foreach(PropertyPatch patch in Patches) // try to do the actual work
      {
        // first, remove properties
        List<XmlQualifiedName> deadPropsToRemove = null;
        foreach(PropertyRemoval removal in patch.Remove)
        {
          if(removeProperty == null && canRemoveProperty(removal.Name) != null)
          {
            throw new ArgumentNullException("removeProperty must be specified if canRemoveProperty returns success.");
          }
          removal.Status = removeProperty == null ? null : removeProperty(removal.Name);
          if(removal.Status == null && Context.PropertyStore != null)
          {
            if(deadPropsToRemove == null) deadPropsToRemove = new List<XmlQualifiedName>();
            deadPropsToRemove.Add(removal.Name);
          }
        }
        if(deadPropsToRemove != null && Context.PropertyStore != null)
        {
          Context.PropertyStore.RemoveProperties(canonicalPath, deadPropsToRemove);
        }

        // then set new ones (although only one of Remove or Set will contain elements in a given patch)
        List<XmlProperty> deadPropsToSet = null;
        foreach(KeyValuePair<XmlQualifiedName, PropertyPatchValue> pair in patch.Set)
        {
          if(setProperty == null && canSetProperty(pair.Key, pair.Value) != null)
          {
            throw new ArgumentNullException("setProperty must be specified if canSetProperty returns success.");
          }
          pair.Value.Status = setProperty == null ? null : setProperty(pair.Key, pair.Value);
          if(pair.Value.Status == null)
          {
            if(deadPropsToSet == null) deadPropsToSet = new List<XmlProperty>();
            deadPropsToSet.Add(pair.Value.Property);
          }
        }
        if(deadPropsToSet != null && Context.PropertyStore != null) Context.PropertyStore.SetProperties(canonicalPath, deadPropsToSet);

        if(applyChanges != null) applyChanges();
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks>This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources.</remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, false, false);
  }

  /// <summary>Determines whether the named WebDAV property is protected (i.e. whether it is not allowed to be changed by the client).</summary>
  protected virtual bool IsProtected(XmlQualifiedName propertyName)
  {
    return propertyName == DAVNames.getcontentlength || propertyName == DAVNames.getetag || propertyName == DAVNames.lockdiscovery; 
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
    foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements())
    {
      if(child.HasName(DAVNames.set)) // if the patch should set new or existing properties...
      {
        PropertyPatch patch = new PropertyPatch(false);
        XmlElement props = child.GetChild(DAVNames.prop); // the element containing individual property elements
        string lang = props.GetInheritedAttributeValue(DAVNames.xmlLang); // get the default language for properties in the set
        foreach(XmlElement prop in props.EnumerateChildElements())
        {
          XmlProperty parsedProperty = TryParseValue(prop);
          try
          {
            patch.Set.Add(prop.GetQualifiedName(),
                          new PropertyPatchValue(prop, parsedProperty, parsedProperty == null ? BadValueStatus : null));
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
  }

  /// <summary>Attempts to parse a property value from XML into an <see cref="XmlProperty"/> object. If the value is invalid, null should
  /// be returned. This method should not throw an exception.
  /// </summary>
  protected virtual XmlProperty TryParseValue(XmlElement propertyElement)
  {
    try { return new XmlProperty(propertyElement); }
    catch(ArgumentException) { return null; }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal override void WriteResponse()
  {
    if(Status != null) Context.WriteStatusResponse(Status);
    else WriteResponseCore();
  }

  /// <summary>Determines whether the named WebDAV property is protected (i.e. whether it is not allowed to be changed by the client).</summary>
  bool IsProtected(XmlQualifiedName propertyName, HashSet<XmlQualifiedName> protectedProperties)
  {
    return protectedProperties != null && protectedProperties.Contains(propertyName) || IsProtected(propertyName);
  }

  void WriteResponseCore()
  {
    // check whether any statuses were explicitly indicated as succeeding or failing
    bool hadSuccess = false, hadFailure = false;
    foreach(PropertyPatch patch in Patches)
    {
      foreach(ConditionCode status in patch.Remove.Select(r => r.Status).Concat(patch.Set.Select(s => s.Value.Status)).WhereNotNull())
      {
        if(status.IsError) hadFailure = true;
        else hadSuccess = true;
      }
    }

    // validate that the changes were either all successful or all unsuccessful, as required by RFC 4918 section 9.2
    if(hadSuccess & hadFailure)
    {
      throw new ContractViolationException("PROPPATCH changes must either all succeed or all fail.");
    }

    // collect the namespaces used in the response. while we're at it, also group the property names by status code. use a hash set
    // dictionary rather than a list because properties can theoretically be named multiple times in the request, for instance being
    // set and then removed, or set and then set again. it's still possible for properties to be named multiple times if they have
    // different statuses, but i'm not sure what the proper output should be for that
    HashSet<string> namespaces = new HashSet<string>();
    var namesByStatus = new HashSetDictionary<ConditionCode, XmlQualifiedName>();
    ConditionCode defaultCode = hadFailure ? ConditionCodes.FailedDependency : ConditionCodes.OK;
    foreach(PropertyPatch patch in Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove)
      {
        namespaces.Add(removal.Name.Namespace);
        namesByStatus.Add(removal.Status ?? defaultCode, removal.Name);
      }
      foreach(KeyValuePair<XmlQualifiedName,PropertyPatchValue> pair in patch.Set)
      {
        namespaces.Add(pair.Key.Namespace);
        namesByStatus.Add(pair.Value.Status ?? defaultCode, pair.Key);
      }
    }

    using(MultiStatusResponse response = Context.OpenMultiStatusResponse(namespaces))
    {
      XmlWriter writer = response.Writer;
      writer.WriteStartElement(DAVNames.response);
      writer.WriteElementString(DAVNames.href, Context.ServiceRoot + Context.RequestPath);

      foreach(KeyValuePair<ConditionCode, HashSet<XmlQualifiedName>> pair in namesByStatus)
      {
        writer.WriteStartElement(DAVNames.propstat);
        writer.WriteStartElement(DAVNames.prop);
        foreach(XmlQualifiedName name in pair.Value) writer.WriteEmptyElement(name);
        writer.WriteEndElement(); // </prop>
        response.WriteStatus(pair.Key);
        writer.WriteEndElement(); // </propstat>
      }

      writer.WriteEndElement(); // </response>
    }
  }

  // for unparsable values, use 422 Unprocessable Entity. i tend to think that 400 Bad Request would be more fitting, since 422
  // Unprocessable Entity is supposed to be used for syntactically correct requests that are nonetheless unprocessable (as per RFC 4918
  // section 11.2), and an ill-formed value isn't syntactically correct. (you could argue that it's syntactically correct XML, and
  // only semantically incorrect, but i'd respond that RFC 4918 uses 400 Bad Request for cases where XML is syntactically correct but
  // wrong.) however, RFC 4316 section 4.2 uses 422 Unprocessable Entity in an example reply to a PROPPATCH request with an unparsable
  // value. while it doesn't specify in the text that 422 should be used, clearly the example implies it. on the other hand, RFC 4316
  // seems to be a bit poorly designed and written compared to RFC 4918, which seems to prefer 400 Bad Request. nonetheless, i will
  // follow the example of RFC 4316 and use 422 Unprocessable Entity, since that is the RFC that defines the type extensions
  static readonly ConditionCode BadValueStatus = new ConditionCode(422, "The value was not formatted correctly for its type.");
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

  /// <summary>Gets or sets the status of the removal as a <see cref="ConditionCode"/>. If null, the removal will be assumed to have been
  /// successful if no other errors occurred, and will be assumed to have failed if any other property changes failed. (RFC 4918 section
  /// 9.2 requires that all property changes succeed or fail together.)
  /// </summary>
  public ConditionCode Status { get; set; }
}
#endregion

} // namespace AdamMil.WebDAV.Server
