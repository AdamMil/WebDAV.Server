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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;
using AdamMil.Collections;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;
using BinaryReader = AdamMil.IO.BinaryReader;
using BinaryWriter = AdamMil.IO.BinaryWriter;

namespace AdamMil.WebDAV.Server
{

#region XmlProperty
/// <summary>Represents a WebDAV property value, which is usually a simple type but is capable of having an arbitrary XML element value.</summary>
/// <remarks>An <see cref="XmlProperty"/> can store arbitrary XML along with simple values, where a simple value is considered to be any
/// non-pointer primitive type (i.e. all basic numeric types), string, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
/// <see cref="DBNull"/>, <see cref="Decimal"/>, <see cref="Guid"/>, <see cref="TimeSpan"/>, <see cref="XmlDuration"/>, or
/// <see cref="XmlQualifiedName"/>, or any one-dimensional array of the preceding, or a null value.
/// </remarks>
public sealed class XmlProperty
{
  /// <summary>Initializes a new <see cref="XmlProperty"/> with a name and value. The type of the value will be inferred either from the
  /// value, or from the name, if the name matches a built-in WebDAV property, in which case the value will be validated against the type.
  /// </summary>
  public XmlProperty(XmlQualifiedName name, object value) : this(name, value, null, null) { }

  /// <summary>Initializes a new <see cref="XmlProperty"/> with a name, value, and type (specified as a legal xsi:type value). If the name
  /// matches a built-in WebDAV property, the known type of that property will be used instead of the given type. If the type (possibly
  /// inferred from the name) is not null, the value will be validated against the type. Otherwise, the type will be inferred from the
  /// value.
  /// </summary>
  public XmlProperty(XmlQualifiedName name, object value, XmlQualifiedName type) : this(name, value, type, null) { }

  /// <summary>Initializes a new <see cref="XmlProperty"/> with a name, value, and language (specified as a legal xml:lang value).
  /// The type of the value will be inferred either from the value, or from the name, if the name matches a built-in WebDAV property,
  /// in which case the value will be validated against the type.
  /// </summary>
  public XmlProperty(XmlQualifiedName name, object value, string language) : this(name, value, null, language) { }

  /// <summary>Initializes a new <see cref="XmlProperty"/> with a name, value, type (specified as a legal xsi:type value), and language
  /// (specified as a legal xml:lang value). If the name matches a built-in WebDAV property, the known type of that property will be used
  /// instead of the given type. If the type (possibly inferred from the name) is not null, the value will be validated against the type.
  /// Otherwise, the type will be inferred from the value.
  /// </summary>
  public XmlProperty(XmlQualifiedName name, object value, XmlQualifiedName type, string language)
  {
    if(name == null) throw new ArgumentNullException();

    XmlQualifiedName builtInType;
    if(DAVUtility.IsDAVName(name) && builtInTypes.TryGetValue(name, out builtInType)) type = builtInType;

    if(type == null) type = DAVUtility.GetXsiType(value);
    else DAVUtility.ValidatePropertyValue(name, value, type);

    if(value != null && !DAVUtility.IsStorablePropertyType(value))
    {
      throw new ArgumentException(value.GetType().FullName + " is not a type that can be stored by an XmlProperty.");
    }

    Name     = name;
    Value    = value;
    Type     = type;
    Language = language;
  }

  /// <summary>Initializes a new <see cref="XmlProperty"/> with the given XML element value. The type will be set to the value of the
  /// xsi:type attribute, if any, and the language will be set to the value of the xml:lang attribute, if any. The element content will
  /// also be validated against the type.
  /// </summary>
  public XmlProperty(XmlElement element) : this(element, null) { }

  /// <summary>Initializes a new <see cref="XmlProperty"/> with the given XML element value. The type will be set to the value of the
  /// xsi:type attribute, if any. If <paramref name="language"/> is null, the language will be set to the value of the xml:lang attribute,
  /// if any. The element content will also be validated against the type.
  /// </summary>
  public XmlProperty(XmlElement element, string language)
  {
    if(element == null) throw new ArgumentNullException();
    InitializeFromElement(element, language);
  }

  /// <summary>Initializes a new <see cref="XmlProperty"/> parsed from a <see cref="BinaryReader"/>. The property is assumed to have been
  /// written with <see cref="Save"/>.
  /// </summary>
  public XmlProperty(BinaryReader reader)
  {
    if(reader == null) throw new ArgumentNullException();
    int version = reader.ReadByte();
    if(version != 0) throw new InvalidDataException("Unsupported property version: " + version.ToStringInvariant());
    if(reader.ReadBoolean())
    {
      XmlDocument doc = new XmlDocument();
      doc.LoadXml(reader.ReadStringWithLength());
      InitializeFromElement(doc.DocumentElement, null);
    }
    else
    {
      Language = reader.ReadStringWithLength();
      string typeName = reader.ReadStringWithLength();
      if(typeName != null) Type = new XmlQualifiedName(typeName, reader.ReadStringWithLength());
      Name  = new XmlQualifiedName(reader.ReadStringWithLength(), reader.ReadStringWithLength());
      Value = reader.ReadValueWithType();
    }
  }

  /// <summary>Gets the language of the property value, as an xml:lang value, or null if no language was specified.</summary>
  public string Language { get; private set; }
  /// <summary>Gets the qualified name of the property.</summary>
  public XmlQualifiedName Name { get; private set; }
  /// <summary>Gets the qualified name of the property type, as an xsi:type value, or null if no type was specified and the type could not
  /// be inferred from the value.
  /// </summary>
  public XmlQualifiedName Type { get; private set; }
  /// <summary>Gets the value of the property or null if the property has a null value, an empty element value, or a complex element value.</summary>
  public object Value { get; private set; }

  /// <summary>Returns a copy of the property's XML element value, or null if the value is represented by <see cref="Value"/>.</summary>
  /// <remarks>Note that this method may return null even if an element was passed to the constructor, if the element only represented a
  /// simple value.
  /// </remarks>
  public XmlElement GetElement()
  {
    return Element == null ? null : Element.Extract();
  }

  /// <summary>Saves the property to a <see cref="BinaryWriter"/>.</summary>
  public void Save(BinaryWriter writer)
  {
    writer.Write((byte)0); // version 0
    writer.Write(Element != null);
    if(Element != null)
    {
      StringWriter sw = new StringWriter(CultureInfo.InvariantCulture);
      Element.OwnerDocument.Save(sw);
      writer.WriteStringWithLength(sw.ToString());
    }
    else
    {
      writer.WriteStringWithLength(Language);
      if(Type == null)
      {
        writer.WriteStringWithLength(null);
      }
      else
      {
        writer.WriteStringWithLength(Type.Name);
        writer.WriteStringWithLength(Type.Namespace);
      }
      writer.WriteStringWithLength(Name.Name);
      writer.WriteStringWithLength(Name.Namespace);
      writer.WriteValueWithType(Value);
    }
  }

  internal XmlElement Element { get; private set; }

  /// <summary>The XML data types for the built-in WebDAV properties, or null if the properties have complex (element) values.</summary>
  internal static readonly Dictionary<XmlQualifiedName, XmlQualifiedName> builtInTypes = new Dictionary<XmlQualifiedName, XmlQualifiedName>()
  {
    { DAVNames.creationdate, DAVNames.xsDateTime }, { DAVNames.displayname, DAVNames.xsString },
    { DAVNames.getcontentlanguage, DAVNames.xsString }, { DAVNames.getcontentlength, DAVNames.xsULong },
    { DAVNames.getcontenttype, DAVNames.xsString }, { DAVNames.getetag, null }, { DAVNames.getlastmodified, DAVNames.xsDateTime },
    { DAVNames.lockdiscovery, null }, { DAVNames.resourcetype, null }, { DAVNames.supportedlock, null }
  };

  void InitializeFromElement(XmlElement element, string language)
  {
    Name     = element.GetQualifiedName();
    Language = language ?? element.GetInheritedAttributeValue(DAVNames.xmlLang);

    XmlQualifiedName type;
    // determine the element type. if it's a built-in DAV property, use the well-known type
    if(DAVUtility.IsDAVName(Name) && builtInTypes.TryGetValue(Name, out type))
    {
      Type = type;
    }
    else // otherwise, try parsing an xsi:type attribute, if any
    {
      string typeStr = element.GetAttribute(DAVNames.xsiType);
      if(!string.IsNullOrEmpty(typeStr)) Type = element.ParseQualifiedName(typeStr);
    }

    if(IsSimple(element))
    {
      string elementText = GetInnerTextIfSimple(element);
      if(elementText != null)
      {
        try
        {
          Value = Type == null ? elementText : DAVUtility.ParseXmlValue(elementText, Type, element);
        }
        catch(ArgumentException ex)
        {
          throw new ArgumentException(Name.ToString() + " is expected to be of type " + Type.ToString() + ". " + ex.Message);
        }
        catch(FormatException ex)
        {
          throw new ArgumentException(Name.ToString() + " is expected to be of type " + Type.ToString() + ". " + ex.Message);
        }
        catch(OverflowException ex)
        {
          throw new ArgumentException(Name.ToString() + " is expected to be of type " + Type.ToString() + ". " + ex.Message);
        }
      }
    }
    else
    {
      if(Type != null && Type.Namespace.OrdinalEquals(DAVNames.XmlSchema)) // if it's an xs: type...
      {
        throw new ArgumentException(Name.ToString() + " is expected to have a simple value."); // all xs: types are simple
      }
      // TODO: ideally, we'd validate complex built-in DAV properties as well
      Element = element.Extract(); // Extract gets the inherited language...
      if(language != null) Element.SetAttribute(DAVNames.xmlLang, language); // but if we're not using that, set the new language
    }
  }

  /// <summary>Returns the inner text of an element if it contains only text, and null if it is empty or contains complex content.</summary>
  static string GetInnerTextIfSimple(XmlElement element)
  {
    XmlNode firstChild = element.FirstChild;
    if(firstChild == null) return null;
    else if(firstChild.NextSibling == null) return firstChild.IsTextNode() ? firstChild.Value : null;

    StringBuilder sb = null;
    for(XmlNode child = firstChild; child != null; child = child.NextSibling)
    {
      if(!child.IsTextNode()) return null;
      if(sb == null) sb = new StringBuilder();
      sb.Append(child.Value);
    }
    return sb.ToString();
  }

  /// <summary>Determines whether the element has nothing more than content, language, and/or type.</summary>
  static bool IsSimple(XmlElement element)
  {
    if(element.HasComplexContent()) return false;

    foreach(XmlAttribute attr in element.Attributes)
    {
      // allow xmlns: attributes as well as xml:lang and xsi:type. any other attribute makes the element complex
      if(!attr.Prefix.OrdinalEquals("xmlns") && (attr.Prefix.Length != 0 || !attr.LocalName.OrdinalEquals("xmlns")) &&
         !attr.HasName(DAVNames.xmlLang) && !attr.HasName(DAVNames.xsiType))
      {
        return false;
      }
    }

    return true;
  }
}
#endregion

#region IPropertyStore
/// <summary>Defines a container for the dead properties of the resources within a WebDAV service.</summary>
public interface IPropertyStore
{
  /// <include file="documentation.xml" path="/DAV/IPropertyStore/ClearProperties/node()" />
  void ClearProperties(string canonicalPath, bool recursive);
  /// <include file="documentation.xml" path="/DAV/IPropertyStore/GetProperties/node()" />
  IDictionary<XmlQualifiedName,XmlProperty> GetProperties(string canonicalPath);
  /// <include file="documentation.xml" path="/DAV/IPropertyStore/RemoveProperties/node()" />
  void RemoveProperties(string canonicalPath, IEnumerable<XmlQualifiedName> propertyNames);
  /// <include file="documentation.xml" path="/DAV/IPropertyStore/SetProperties/node()" />
  void SetProperties(string canonicalPath, IEnumerable<XmlProperty> properties, bool removeExisting);
}
#endregion

#region PropertyStore
/// <summary>Provides a base class for implementing property stores. This class maintains an in-memory representation of the dead
/// properties for a WebDAV resource. Derived classes are responsible for saving and loading the properties to and from persistent storage.
/// </summary>
public abstract class PropertyStore : IDisposable, IPropertyStore
{
  /// <summary>Initializes a new <see cref="PropertyStore"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  protected PropertyStore(ParameterCollection parameters)
  {
    if(parameters == null) throw new ArgumentException();
  }

  /// <summary>Finalizes the <see cref="PropertyStore"/> by calling <see cref="Dispose(bool)"/>.</summary>
  ~PropertyStore()
  {
    Dispose(false);
    disposed = true;
  }

  /// <include file="documentation.xml" path="/DAV/IPropertyStore/ClearProperties/node()" />
  public void ClearProperties(string canonicalPath, bool recursive)
  {
    DAVUtility.ValidateRelativePath(canonicalPath);
    AssertNotDisposed();
    canonicalPath = DAVUtility.RemoveTrailingSlash(canonicalPath);
    lock(this)
    {
      if(propertiesByUrl.Remove(canonicalPath)) OnPropertiesChanged(canonicalPath, null);

      if(recursive)
      {
        canonicalPath = DAVUtility.WithTrailingSlash(canonicalPath);
        List<string> deadUrls = new List<string>();
        foreach(string url in propertiesByUrl.Keys)
        {
          if(url.StartsWith(canonicalPath, StringComparison.Ordinal)) deadUrls.Add(url);
        }
        propertiesByUrl.RemoveRange(deadUrls);
      }
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    Dispose(true);
    disposed = true;
    GC.SuppressFinalize(this);
  }

  /// <include file="documentation.xml" path="/DAV/IPropertyStore/GetProperties/node()" />
  public IDictionary<XmlQualifiedName, XmlProperty> GetProperties(string canonicalPath)
  {
    DAVUtility.ValidateRelativePath(canonicalPath);
    AssertNotDisposed();
    canonicalPath = DAVUtility.RemoveTrailingSlash(canonicalPath);
    Dictionary<XmlQualifiedName,XmlProperty> propDict;
    lock(this)
    {
      if(propertiesByUrl.TryGetValue(canonicalPath, out propDict)) propDict = new Dictionary<XmlQualifiedName, XmlProperty>(propDict);
    }
    return propDict ?? new Dictionary<XmlQualifiedName, XmlProperty>();
  }

  /// <include file="documentation.xml" path="/DAV/IPropertyStore/RemoveProperties/node()" />
  public void RemoveProperties(string canonicalPath, IEnumerable<XmlQualifiedName> propertyNames)
  {
    DAVUtility.ValidateRelativePath(canonicalPath);
    if(propertyNames == null) throw new ArgumentNullException();
    AssertNotDisposed();
    canonicalPath = DAVUtility.RemoveTrailingSlash(canonicalPath);
    lock(this)
    {
      Dictionary<XmlQualifiedName, XmlProperty> propDict;
      if(propertiesByUrl.TryGetValue(canonicalPath, out propDict))
      {
        int count = propDict.Count;
        propDict.RemoveRange(propertyNames);
        if(propDict.Count == 0) propertiesByUrl.Remove(canonicalPath);
        if(propDict.Count != count) OnPropertiesChanged(canonicalPath, propDict.Count == 0 ? null : propDict);
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/IPropertyStore/SetProperties/node()" />
  public void SetProperties(string canonicalPath, IEnumerable<XmlProperty> properties, bool removeExisting)
  {
    DAVUtility.ValidateRelativePath(canonicalPath);
    if(properties == null) throw new ArgumentNullException();
    AssertNotDisposed();
    canonicalPath = DAVUtility.RemoveTrailingSlash(canonicalPath);
    lock(this)
    {
      Dictionary<XmlQualifiedName,XmlProperty> propDict;
      bool newDict = !propertiesByUrl.TryGetValue(canonicalPath, out propDict);
      if(newDict) propDict = new Dictionary<XmlQualifiedName, XmlProperty>();
      else if(removeExisting) propDict.Clear();
      bool changed = !newDict & removeExisting; // true if we cleared the dictionary on the previous line
      foreach(XmlProperty property in properties)
      {
        if(property == null) throw new ArgumentException("A property object was null.");
        propDict[property.Name] = property;
        changed = true;
      }
      if(changed)
      {
        if(newDict) propertiesByUrl[canonicalPath] = propDict; // if changed & newDict is true, then propDict.Count != 0
        else if(propDict.Count == 0) propertiesByUrl.Remove(canonicalPath);
        OnPropertiesChanged(canonicalPath, propDict);
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/PropertyStore/Dispose/node()" />
  protected virtual void Dispose(bool manualDispose) { }

  /// <summary>Returns a <see cref="MultiValuedDictionary{K,V}"/> mapping resource paths to the lists of properties associated with the
  /// resources.
  /// </summary>
  protected MultiValuedDictionary<string, XmlProperty> GetAllProperties()
  {
    MultiValuedDictionary<string, XmlProperty> properties = new MultiValuedDictionary<string, XmlProperty>();
    lock(this)
    {
      foreach(KeyValuePair<string, Dictionary<XmlQualifiedName, XmlProperty>> pair in propertiesByUrl)
      {
        properties.Add(pair.Key, new List<XmlProperty>(pair.Value.Values));
      }
    }
    return properties;
  }

  /// <summary>Loads the given resources and their properties into the <see cref="PropertyStore"/>, clearing all existing properties
  /// first. The properties are not validated, and are assumed to have come from the <see cref="GetAllProperties"/> method.
  /// </summary>
  protected void LoadProperties(MultiValuedDictionary<string, XmlProperty> resources)
  {
    if(resources == null) throw new ArgumentNullException();
    AssertNotDisposed();
    lock(this)
    {
      propertiesByUrl.Clear();
      foreach(KeyValuePair<string, List<XmlProperty>> pair in resources)
      {
        Dictionary<XmlQualifiedName, XmlProperty> properties = new Dictionary<XmlQualifiedName, XmlProperty>(pair.Value.Count);
        foreach(XmlProperty property in pair.Value) properties.Add(property.Name, property);
        propertiesByUrl.Add(pair.Key, properties);
      }
    }
  }

  /// <include file="documentation.xml" path="/DAV/PropertyStore/OnPropertiesChanged/node()" />
  protected abstract void OnPropertiesChanged(string canonicalPath, Dictionary<XmlQualifiedName, XmlProperty> newProperties);

  /// <summary>Throws an exception if the property store has been disposed.</summary>
  void AssertNotDisposed()
  {
    if(disposed) throw new ObjectDisposedException(ToString());
  }

  readonly Dictionary<string, Dictionary<XmlQualifiedName,XmlProperty>> propertiesByUrl =
    new Dictionary<string, Dictionary<XmlQualifiedName, XmlProperty>>();
  bool disposed;
}
#endregion

#region FilePropertyStore
/// <summary>Implements a <see cref="PropertyStore"/> that stores properties in a file on disk.</summary>
public class FilePropertyStore : PropertyStore
{
  /// <summary>Initializes a new <see cref="FilePropertyStore"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  /// <remarks>In addition to the parameters accepted by <see cref="PropertyStore"/>, <see cref="FilePropertyStore"/> supports the following:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Parameter</term>
  ///     <description>Type</description>
  ///     <description>Description</description>
  ///   </listheader>
  ///   <item>
  ///     <term>propertyDir</term>
  ///     <description>xs:string</description>
  ///     <description>The full path to a directory in which the properties will be saved. This is only suitable for global property
  ///       stores. Files will be created in the directory with names based on the location.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>propertyFile</term>
  ///     <description>xs:string</description>
  ///     <description>The full path to the file in which the properties will be saved. This is only suitable for property stores
  ///       specified on a per-location basis.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>revertToSelf</term>
  ///     <description>xs:bool</description>
  ///     <description>Determines whether the property store will revert to the process identity before opening the file on disk. This
  ///     allows the file to be opened with the IIS process account, which is usually more privileged. The default is true.
  ///     </description>
  ///   </item>
  ///   <item>
  ///     <term>writeInterval</term>
  ///     <description>xs:positiveInteger</description>
  ///     <description>The number of seconds to wait before flushing pending changes to disk. The default is 60 and the maximum is 2147483.</description>
  ///   </item>
  /// </list>
  /// </remarks>
  public FilePropertyStore(string locationId, ParameterCollection parameters) : base(parameters)
  {
    if(locationId == null) throw new ArgumentNullException();
    string value = parameters.TryGetValue("revertToSelf");
    bool revertToSelf = string.IsNullOrEmpty(value) || XmlConvert.ToBoolean(value);

    writeInterval = (int)DAVUtility.ParseConfigParameter(parameters, "writeInterval", 60, 1, int.MaxValue/1000) * 1000;

    value = parameters.TryGetValue("propertyFile");
    if(string.IsNullOrEmpty(value))
    {
      value = parameters.TryGetValue("propertyDir");
      if(string.IsNullOrEmpty(value))
      {
        throw new ArgumentException("The propertyFile attribute is required for the FilePropertyStore.");
      }
      value = Path.Combine(value, DAVUtility.FileNameEncode(locationId) + "_props");
    }

    FileStream file = null;
    Action openFile = delegate { file = new FileStream(value, FileMode.OpenOrCreate, FileAccess.ReadWrite); };
    if(revertToSelf) Impersonation.RunWithImpersonation(Impersonation.RevertToSelf, false, openFile);
    else ElevatePrivileges(openFile);
    if(file == null) throw new ArgumentException("Unable to open file: " + value);
    this.file = file;
    LoadProperties(value);

    timer = new Timer(OnTimerTick);
  }

  /// <include file="documentation.xml" path="/DAV/PropertyStore/Dispose/node()" />
  protected override void Dispose(bool manualDispose)
  {
    if(!disposed)
    {
      Utility.Dispose(timer);
      lock(fileLock)
      {
        if(file != null)
        {
          try { WriteChanges(); }
          catch(ObjectDisposedException) { } // if the app domain was unloaded, the file may have been closed already...
          file.Close();
        }
        disposed = true;
      }
    }

    base.Dispose(manualDispose);
  }

  /// <summary>Called to execute the given action, such as opening the property file, in an elevated privilege context when the
  /// <c>revertToSelf</c> parameter is false.
  /// </summary>
  /// <remarks>The default implementation simply executes the action without altering privileges in any way.</remarks>
  protected virtual void ElevatePrivileges(Action action)
  {
    if(action == null) throw new ArgumentNullException();
    action();
  }

  /// <include file="documentation.xml" path="/DAV/PropertyStore/OnPropertiesChanged/node()" />
  protected override void OnPropertiesChanged(string canonicalPath, Dictionary<XmlQualifiedName, XmlProperty> newProperties)
  {
    // OnPropertiesChanged() is always called from within a lock, so we don't need any locking semantics here
    if(!pendingWrite)
    {
      timer.Change(writeInterval, Timeout.Infinite);
      pendingWrite = true;
    }
  }

  /// <summary>A magic number identifying the file as a property file.</summary>
  const uint MagicNumber = 0xced764fd;

  void LoadProperties(string fileName)
  {
    if(file.Length != 0)
    {
      try
      {
        if(file.Length > 5)
        {
          byte[] magic = file.Read(5);
          if(magic[0] == unchecked((byte)MagicNumber)     && magic[1] == unchecked((byte)(MagicNumber>>8)) &&
             magic[2] == unchecked((byte)MagicNumber>>16) && magic[3] == unchecked((byte)(MagicNumber>>24)))
          {
            int version = magic[4];
            if(version == 0)
            {
              using(BinaryReader reader = new BinaryReader(new GZipStream(file, CompressionMode.Decompress, true)))
              {
                int resourceCount = reader.ReadInt32();
                var resources = new MultiValuedDictionary<string, XmlProperty>();
                while(resourceCount-- != 0)
                {
                  string canonicalPath = reader.ReadStringWithLength();
                  int propertyCount = (int)reader.ReadEncodedUInt32();
                  List<XmlProperty> properties = new List<XmlProperty>(propertyCount);
                  do properties.Add(new XmlProperty(reader)); while(--propertyCount != 0);
                  resources.Add(canonicalPath, properties);
                }
                LoadProperties(resources);
                return;
              }
            }
          }
        }
      }
      catch(IOException) { }
      catch(OutOfMemoryException) { }

      throw new InvalidDataException(fileName +
                                     " is not a valid property file. If you want to use this file name, remove the file first.");
    }
  }

  void OnTimerTick(object state)
  {
    WriteChanges();
  }

  void WriteChanges()
  {
    if(pendingWrite && !disposed)
    {
      lock(fileLock)
      {
        MultiValuedDictionary<string, XmlProperty> resources = null;
        lock(this)
        {
          if(pendingWrite && !disposed)
          {
            resources = GetAllProperties();
            pendingWrite = false;
          }
        }

        if(resources != null)
        {
          file.Position = 0;
          byte[] magic = new byte[]
          {
            unchecked((byte)MagicNumber),     unchecked((byte)(MagicNumber>>8)),
            unchecked((byte)MagicNumber>>16), unchecked((byte)(MagicNumber>>24)), 0 // version 0
          };
          file.Write(magic);
          using(BinaryWriter writer = new BinaryWriter(new GZipStream(file, CompressionMode.Compress, true)))
          {
            writer.Write(resources.Count);
            foreach(KeyValuePair<string, List<XmlProperty>> pair in resources)
            {
              writer.WriteStringWithLength(pair.Key);
              writer.WriteEncoded((uint)pair.Value.Count);
              foreach(XmlProperty property in pair.Value) property.Save(writer);
            }
          }
          if(file.Position < file.Length) file.SetLength(file.Position);
          file.Flush();
        }
      }
    }
  }

  readonly FileStream file;
  readonly object fileLock = new object();
  readonly Timer timer;
  readonly int writeInterval;
  bool disposed, pendingWrite;
}
#endregion

#region MemoryPropertyStore
/// <summary>Implements a <see cref="PropertyStore"/> that only maintains properties in memory. All properties will be lost when the
/// WebDAV server terminates or is restarted.
/// </summary>
public class MemoryPropertyStore : PropertyStore
{
  /// <summary>Initializes a new <see cref="MemoryPropertyStore"/> that loads its configuration from a <see cref="ParameterCollection"/>.</summary>
  public MemoryPropertyStore(string serviceId, ParameterCollection parameters) : base(parameters) { }
  /// <include file="documentation.xml" path="/DAV/PropertyStore/OnPropertiesChanged/node()" />
  protected override void OnPropertiesChanged(string canonicalPath, Dictionary<XmlQualifiedName, XmlProperty> newProperties) { }
}
#endregion

#region DisablePropertyStore
/// <summary>Implements a <see cref="PropertyStore"/> that signals to the WebDAV server that setting dead properties should be disabled.
/// This may be on a location to override the server-wide default property store.
/// </summary>
/// <remarks>The WebDAV server will not use this property store. If you use it in your own code, it will behave identically to
/// <see cref="MemoryPropertyStore"/>.
/// </remarks>
public sealed class DisablePropertyStore : MemoryPropertyStore
{
  /// <summary>Initializes a new <see cref="DisablePropertyStore"/>.</summary>
  public DisablePropertyStore(string serviceId, ParameterCollection parameters) : base(serviceId, parameters) { }
}
#endregion

} // namespace AdamMil.WebDAV.Server
