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
using System.Net;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;

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
/// <remarks>
/// <para>The <c>PROPPATCH</c> request is described in section 9.2 of RFC 4918. To service a <see cref="PropPatchRequest"/>, you can
/// normally just call <see cref="ProcessStandardRequest()"/> or one of its overrides.
/// </para>
/// <para>If you want to handle it yourself, you should examine the <see cref="Patches"/> collection to see which properties the client
/// wants to set and remove on the request resource, execute the operations in order, and set on each <see cref="PropertyPatchValue"/> and
/// <see cref="PropertyRemoval"/> object a <see cref="ConditionCode"/> representing the result of attempting to set or remove that
/// property. Note that the property patches must be applied atomically. Either all operations must succeed or none of them. This may
/// require undoing changes that have already been made if an error occurs. Alternately, if the entire request failed, set
/// <see cref="WebDAVRequest.Status"/> accordingly. In either case, <see cref="WriteResponse"/> will write the response to the client.
/// The list of expected status codes for the response follows.
/// </para>
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>207 <see cref="ConditionCodes.MultiStatus">Multi-Status</see> (default)</term>
///   <description>This status code should be used along with a <c>DAV:multistatus</c> XML body to report the names and statuses of the
///     properties within the property patches requested by the client. This is the default status code that will be used if
///     <see cref="WebDAVRequest.Status"/> is null.
///   </description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The user doesn't have permission to alter any resource properties, or the server refuses to alter the resource for some
///     other reason.
///   </description>
/// </item>
/// <item>
///   <term>412 <see cref="ConditionCodes.PreconditionFailed">Precondition Failed</see></term>
///   <description>A conditional request was not executed because the condition wasn't true.</description>
/// </item>
/// <item>
///   <term>423 <see cref="ConditionCodes.Locked"/></term>
///   <description>The request resource was locked and no valid lock token was submitted. The <c>DAV:lock-token-submitted</c> precondition
///     code should be included in the response.
///   </description>
/// </item>
/// <item>
///   <term>507 <see cref="ConditionCodes.InsufficientStorage">Insufficient Storage</see></term>
///   <description>The server did not have enough space to record the property changes.</description>
/// </item>
/// </list>
/// Status codes that might be used for individual properties are:
/// <list type="table">
/// <listheader>
///   <term>Status</term>
///   <description>Should be returned if...</description>
/// </listheader>
/// <item>
///   <term>200 <see cref="ConditionCodes.OK"/></term>
///   <description>The property set or removal succeeded. Note that if this appears for one property, then it must appear for all
///     properties, due to the atomicity of the <c>PROPPATCH</c> request.
///   </description>
/// </item>
/// <item>
///   <term>401 <see cref="ConditionCodes.Unauthorized"/></term>
///   <description>The property value cannot be changed without the appropriate authorization.</description>
/// </item>
/// <item>
///   <term>403 <see cref="ConditionCodes.Forbidden"/></term>
///   <description>The property value cannot be changed regardless of authorization. This may occur when a client attempts to change a
///     protected property, such as <c>DAV:getetag</c>, in which case the status should contain the
///     <c>DAV:cannot-modify-protected-property</c> precondition code.
///   </description>
/// </item>
/// <item>
///   <term>409 <see cref="ConditionCodes.Conflict"/></term>
///   <description>The client provided a property value whose semantics are not appropriate for the property.</description>
/// </item>
/// <item>
///   <term>422 <see cref="ConditionCodes.UnprocessableEntity">Unprocessable Entity</see></term>
///   <description>The property value was syntactically invalid for the type of the property. For example, if the client submitted the
///     property <c>&lt;myprop xsi:type="xs:int"&gt;hello&lt;/mpprop&gt;</c> (where <c>xs</c> and <c>xsi</c> are declared in the usual
///     way), you might return this status because the property is declared to be an integer but "hello" is not a valid integer. On the
///     other hand, you may wish to preserve the XML value from the client verbatim.
///   </description>
/// </item>
/// <item>
///   <term>424 <see cref="ConditionCodes.FailedDependency">Failed Dependency</see></term>
///   <description>The property could not be changed because another property change failed.</description>
/// </item>
/// <item>
///   <term>507 <see cref="ConditionCodes.InsufficientStorage">Insufficient Storage</see></term>
///   <description>The server did not have enough space to record the property change.</description>
/// </item>
/// </list>
/// If you derive from this class, you may want to override the following virtual members, in addition to those from the base class.
/// <list type="table">
/// <listheader>
///   <term>Member</term>
///   <description>Should be overridden if...</description>
/// </listheader>
/// <item>
///   <term><see cref="IsProtected(XmlQualifiedName)"/></term>
///   <description>You want to change which properties are never allowed to be changed by the client during standard processing.</description>
/// </item>
/// <item>
///   <term><see cref="ParseRequestXml"/></term>
///   <description>You want to change how the request XML body is parsed or validated.</description>
/// </item>
/// <item>
///   <term><see cref="TryParseValue"/></term>
///   <description>You want to change how an <see cref="XmlElement"/> from the request body is parsed into an <see cref="XmlProperty"/>.</description>
/// </item>
/// </list>
/// </remarks>
public class PropPatchRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="PropPatchRequest"/> based on a new WebDAV request.</summary>
  public PropPatchRequest(WebDAVContext context) : base(context)
  {
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

  /// <include file="documentation.xml" path="/DAV/PropPatchRequest/ProcessStandardRequest/*[not(@name='canonicalPath') and not(@name='protectedProperties') and not(@name='applyChanges')]" />
  public void ProcessStandardRequest(Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> canSetProperty,
                                     Func<XmlQualifiedName,ConditionCode> canRemoveProperty,
                                     Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> setProperty,
                                     Func<XmlQualifiedName,ConditionCode> removeProperty)
  {
    ProcessStandardRequest(null, null, canSetProperty, canRemoveProperty, setProperty, removeProperty, null);
  }

  /// <include file="documentation.xml" path="/DAV/PropPatchRequest/ProcessStandardRequest/node()" />
  public virtual void ProcessStandardRequest(string canonicalPath, HashSet<XmlQualifiedName> protectedProperties,
                                             Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> canSetProperty,
                                             Func<XmlQualifiedName,ConditionCode> canRemoveProperty,
                                             Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> setProperty,
                                             Func<XmlQualifiedName,ConditionCode> removeProperty, Action applyChanges)
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

    // first verify that all of the values are valid. if everything should be able to succeed, then apply the changes
    bool hadError = ValidatePatches(protectedProperties, canSetProperty, canRemoveProperty, setProperty, removeProperty);
    if(!hadError) ApplyPatches(canonicalPath, canSetProperty, canRemoveProperty, applyChanges);
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokens/node()" />
  /// <remarks><include file="documentation.xml" path="/DAV/WebDAVRequest/CheckSubmittedLockTokensRemarks/remarks/node()" />
  /// <note type="inherit">This implementation checks <c>DAV:write</c> locks on the resource and does not check descendant resources.</note>
  /// </remarks>
  protected override ConditionCode CheckSubmittedLockTokens(string canonicalPath)
  {
    return CheckSubmittedLockTokens(LockType.ExclusiveWrite, canonicalPath, false, false);
  }

  /// <summary>Determines whether the named WebDAV property is protected (i.e. whether it is not allowed to be changed by the client).</summary>
  protected virtual bool IsProtected(XmlQualifiedName propertyName)
  {
    if(propertyName == null) throw new ArgumentNullException();
    return propertyName.Namespace.OrdinalEquals(DAVNames.DAV) &&
           (propertyName.Name.OrdinalEquals(DAVNames.getcontentlength.Name) || propertyName.Name.OrdinalEquals(DAVNames.getetag.Name) ||
            propertyName.Name.OrdinalEquals(DAVNames.lockdiscovery.Name)); 
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    XmlDocument xml = Context.LoadRequestXml(); // an XML body is required by RFC 4918 section 9.2
    if(xml == null) Status = new ConditionCode(HttpStatusCode.BadRequest, "The request body was missing.");
    else ParseRequestXml(xml);
  }

  /// <summary>Called by <see cref="ParseRequest"/> to parse and validate the XML request body.</summary>
  /// <remarks>If the request body is invalid, this method should set <see cref="WebDAVRequest.Status"/> to an appropriate error code.</remarks>
  protected virtual void ParseRequestXml(XmlDocument xml)
  {
    if(xml == null) throw new ArgumentNullException();
    xml.DocumentElement.AssertName(DAVNames.propertyupdate); // and it has to be a <propertyupdate> body

    // parse all of the property patches
    foreach(XmlElement child in xml.DocumentElement.EnumerateChildElements())
    {
      if(child.HasName(DAVNames.set)) // if the patch should set new or existing properties...
      {
        PropertyPatch patch = new PropertyPatch(false);
        XmlElement props = child.GetChild(DAVNames.prop); // the element containing individual property elements
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
            Status = new ConditionCode(HttpStatusCode.BadRequest, "Duplicate property name " + prop.GetQualifiedName().ToString());
            return;
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
    if(Status != null && Status.StatusCode != 207) Context.WriteStatusResponse(Status); // if Status isn't the default 207 Multi-Status...
    else WriteResponseCore();
  }

  void ApplyPatches(string canonicalPath, Func<XmlQualifiedName,PropertyPatchValue, ConditionCode> setProperty,
                    Func<XmlQualifiedName,ConditionCode> removeProperty, Action applyChanges)
  {
    if(canonicalPath == null) canonicalPath = Context.RequestResource.CanonicalPath;
    foreach(PropertyPatch patch in Patches) // try to do the actual work
    {
      // first, remove properties
      List<XmlQualifiedName> deadPropsToRemove = null;
      foreach(PropertyRemoval removal in patch.Remove)
      {
        if(removal.Status != null && removal.Status.IsError) continue; // skip removals that failed validation
        removal.Status = removeProperty == null ? null : DAVUtility.TryExecute(removeProperty, removal.Name);
        if(removal.Status == null) // if this is a dead property...
        {
          if(deadPropsToRemove == null) deadPropsToRemove = new List<XmlQualifiedName>();
          deadPropsToRemove.Add(removal.Name);
        }
      }
      if(deadPropsToRemove != null && Context.PropertyStore != null) // remove dead properties if we can
      {
        Context.PropertyStore.RemoveProperties(canonicalPath, deadPropsToRemove);
      }

      // then set new ones (although only one of Remove or Set will contain elements in a given patch)
      List<XmlProperty> deadPropsToSet = null;
      foreach(KeyValuePair<XmlQualifiedName, PropertyPatchValue> pair in patch.Set)
      {
        if(pair.Value.Status != null && pair.Value.Status.IsError) continue; // skip changes that failed validation
        pair.Value.Status = setProperty == null ? null : DAVUtility.TryExecute(setProperty, pair.Key, pair.Value);
        if(pair.Value.Status == null) // if this is a dead property...
        {
          if(deadPropsToSet == null) deadPropsToSet = new List<XmlProperty>();
          deadPropsToSet.Add(pair.Value.Property); // .Property should have parsed okay. otherwise it would have had an error status
        }
      }
      if(deadPropsToSet != null) // set dead properties if there are any
      {
        if(Context.PropertyStore == null) // there should be a store because ValidatePatches already checked the result of canSetProperty,
        {                                 // but if canSetProperty and/or setProperty gave the wrong result, then there might not be
          throw new ContractViolationException("canSetProperty returned success but setProperty returned null (or was null)");
        }
        Context.PropertyStore.SetProperties(canonicalPath, deadPropsToSet, false);
      }

      if(applyChanges != null) applyChanges();
    }
  }

  /// <summary>Determines whether the named WebDAV property is protected (i.e. whether it is not allowed to be changed by the client).</summary>
  bool IsProtected(XmlQualifiedName propertyName, HashSet<XmlQualifiedName> protectedProperties)
  {
    return protectedProperties != null && protectedProperties.Contains(propertyName) || IsProtected(propertyName);
  }
  
  bool ValidatePatches(HashSet<XmlQualifiedName> protectedProperties,
                       Func<XmlQualifiedName,PropertyPatchValue,ConditionCode> canSetProperty,
                       Func<XmlQualifiedName,ConditionCode> canRemoveProperty,
                       Func<XmlQualifiedName,PropertyPatchValue, ConditionCode> setProperty,
                       Func<XmlQualifiedName,ConditionCode> removeProperty)
  {
    bool hadError = false;
    foreach(PropertyPatch patch in Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove)
      {
        ConditionCode status = IsProtected(removal.Name, protectedProperties) ? ConditionCodes.CannotModifyProtectedProperty
                                                                              : DAVUtility.TryExecute(canRemoveProperty, removal.Name);
        if(status != null)
        {
          if(status.IsError)
          {
            removal.Status = status;
            hadError       = true;
          }
          else if(removeProperty == null)
          {
            throw new ArgumentNullException("removeProperty must be specified if canRemoveProperty returns success.");
          }
        }
      }

      foreach(KeyValuePair<XmlQualifiedName, PropertyPatchValue> pair in patch.Set)
      {
        ConditionCode status = pair.Value.Status;
        if(status != null && status.IsError) // if it already had an error status (from parsing the request), use it...
        {
          hadError = true;
        }
        else // otherwise, make sure it's not protected or otherwise forbidden
        {
          status = IsProtected(pair.Key, protectedProperties) ? ConditionCodes.CannotModifyProtectedProperty
                                                              : DAVUtility.TryExecute(canSetProperty, pair.Key, pair.Value);
          if(status == null && Context.PropertyStore == null) status = ConditionCodes.Forbidden; // can't set dead properties with no store
          if(status != null)
          {
            if(status.IsError)
            {
              pair.Value.Status = status;
              hadError          = true;
            }
            else if(setProperty == null)
            {
              throw new ArgumentNullException("setProperty must be specified if canSetProperty returns success.");
            }
          }
        }
      }
    }
    return hadError;
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

    // collect the namespaces used in the response. while we're at it, also group the property names by status code. use a nested
    // dictionary rather than a list because properties can theoretically be named multiple times in the request, for instance being
    // set and then removed, or set and then set again. it's still possible for properties to be named multiple times if they have
    // different statuses, but i'm not sure what the proper output should be for that
    // also, RFC 4316 section 4 says that if we understood and accepted the xsi:type supplied by the client, we should echo that type back
    // in the response, but if we didn't understand the type then we shouldn't. therefore, we'll keep track of the xsi:type that we'll
    // supply on the output elements in the dictionary
    HashSet<string> namespaces = new HashSet<string>();
    var namesByStatus = new Dictionary<ConditionCode, Dictionary<XmlQualifiedName,XmlQualifiedName>>();
    ConditionCode defaultCode = hadFailure ? ConditionCodes.FailedDependency : ConditionCodes.OK;
    bool hadType = false;
    foreach(PropertyPatch patch in Patches)
    {
      foreach(PropertyRemoval removal in patch.Remove)
      {
        namespaces.Add(removal.Name.Namespace);
        ConditionCode status = removal.Status ?? defaultCode;
        Dictionary<XmlQualifiedName, XmlQualifiedName> dict = namesByStatus.TryGetValue(status);
        if(dict == null) namesByStatus[status] = dict = new Dictionary<XmlQualifiedName, XmlQualifiedName>();
        dict[removal.Name] = null; // removed properties have no xsi:type attribute
      }
      foreach(KeyValuePair<XmlQualifiedName,PropertyPatchValue> pair in patch.Set)
      {
        namespaces.Add(pair.Key.Namespace);
        ConditionCode status = pair.Value.Status ?? defaultCode;
        Dictionary<XmlQualifiedName, XmlQualifiedName> dict = namesByStatus.TryGetValue(status);
        if(dict == null) namesByStatus[status] = dict = new Dictionary<XmlQualifiedName, XmlQualifiedName>();
        // set properties get an xsi:type attribute if the set was successful, the client sent an xsi:type, and the type was well-known
        XmlQualifiedName type = status.IsSuccessful && pair.Value.Property != null ? pair.Value.Property.Type : null;
        dict[pair.Key] = type != null && DAVUtility.IsKnownXsiType(type) ? type : null;
        if(type != null) hadType = true;
      }
    }

    // add namespaces for any xsi:type attributes that we set
    if(hadType)
    {
      namespaces.Add(DAVNames.XmlSchemaInstance);
      foreach(Dictionary<XmlQualifiedName, XmlQualifiedName> dict in namesByStatus.Values)
      {
        foreach(XmlQualifiedName type in dict.Values)
        {
          if(type != null) namespaces.Add(type.Namespace);
        }
      }
    }

    using(MultiStatusResponse response = Context.OpenMultiStatusResponse(namespaces))
    {
      XmlWriter writer = response.Writer;
      writer.WriteStartElement(DAVNames.response);
      writer.WriteElementString(DAVNames.href, Context.ServiceRoot + DAVUtility.UriPathPartialEncode(Context.RequestPath));

      foreach(KeyValuePair<ConditionCode, Dictionary<XmlQualifiedName,XmlQualifiedName>> spair in namesByStatus)
      {
        writer.WriteStartElement(DAVNames.propstat);
        writer.WriteStartElement(DAVNames.prop);
        foreach(KeyValuePair<XmlQualifiedName, XmlQualifiedName> ppair in spair.Value)
        {
          writer.WriteStartElement(ppair.Key); // write the property name
          if(ppair.Value != null) // if the element should have an xsi:type attribute, write that
          {
            writer.WriteStartAttribute(DAVNames.xsiType);
            writer.WriteQualifiedName(ppair.Value);
            writer.WriteEndAttribute();
          }
          writer.WriteEndElement();
        }
        writer.WriteEndElement(); // </prop>
        response.WriteStatus(spair.Key);
        writer.WriteEndElement(); // </propstat>
      }

      writer.WriteEndElement(); // </response>
    }
  }

  // for unparsable values, use 422 Unprocessable Entity. 400 Bad Request might be more fitting, since 422 Unprocessable Entity is supposed
  // to be used for syntactically correct requests that are nonetheless unprocessable (as per RFC 4918 section 11.2), and an ill-formed
  // value isn't syntactically correct. (you could argue that it's syntactically correct XML, and only semantically incorrect, but i'd
  // respond that RFC 4918 uses 400 Bad Request for cases where XML is syntactically correct but wrong.) however, RFC 4316 section 4.2 uses
  // 422 Unprocessable Entity in an example reply to a PROPPATCH request with an unparsable value. while it doesn't specify in the text
  // that 422 should be used, clearly the example implies it. on the other hand, RFC 4316 seems to be a bit poorly designed and written
  // compared to RFC 4918, which seems to prefer 400 Bad Request. nonetheless, i will follow the example of RFC 4316 and use
  // 422 Unprocessable Entity, since that is the RFC that defines the type extensions
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
