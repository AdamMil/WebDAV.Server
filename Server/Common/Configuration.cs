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
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using AdamMil.Collections;
using AdamMil.Utilities;

// TODO: perhaps we should add per-location media and compression maps

namespace AdamMil.WebDAV.Server.Configuration
{

#region AuthorizationFilterCollection
/// <summary>Implements a collection of <see cref="AuthorizationFilterElement"/> objects.</summary>
public sealed class AuthorizationFilterCollection : CustomElementCollection<AuthorizationFilterElement>
{
  /// <inheritdoc/>
  protected override AuthorizationFilterElement CreateElement()
  {
    return new AuthorizationFilterElement();
  }

  /// <inheritdoc/>
  protected override object GetElementKey(AuthorizationFilterElement element)
  {
    if(element == null) throw new ArgumentNullException();
    return element.Type;
  }
}
#endregion

#region AuthorizationFilterElement
/// <summary>Implements a <see cref="ConfigurationElement"/> representing an <see cref="IAuthorizationFilter"/>.</summary>
public sealed class AuthorizationFilterElement : TypeElementBase<IAuthorizationFilter>
{
  /// <summary>Gets the type implementing the <see cref="IAuthorizationFilter"/> interface, used restrict requests to resources.</summary>
  [ConfigurationProperty("type"), TypeConverter(typeof(TypeNameConverter)), SubclassTypeValidator(typeof(IAuthorizationFilter))]
  public Type Type
  {
    get { return InnerType; }
  }
}
#endregion

#region CompressionMapCollection
/// <summary>Implements a collection of <see cref="CompressionMapElement"/> objects.</summary>
public sealed class CompressionMapCollection : CustomElementCollection<CompressionMapElement>
{
  /// <summary>Gets the name of the file from which the default elements should be taken. If null or empty, elements will be taken from an
  /// internal media type compression map.
  /// </summary>
  [ConfigurationProperty("defaultFile")]
  public string DefaultFile
  {
    get { return (string)this["defaultFile"]; }
  }

  /// <inheritdoc/>
  protected override CompressionMapElement CreateElement()
  {
    return new CompressionMapElement();
  }

  /// <inheritdoc/>
  protected override object GetElementKey(CompressionMapElement element)
  {
    if(element == null) throw new ArgumentNullException();
    return element.MediaTypePattern;
  }

  /// <inheritdoc/>
  protected override bool ThrowOnDuplicate
  {
    get { return false; } // allow the user to overwrite default mappings without removing them first
  }

  /// <inheritdoc/>
  protected override void Init()
  {
    base.Init();

    using(Stream schemaStream = DAVUtility.GetManifestResourceStream("Resources/Compression.xsd"))
    {
      XmlSchema schema = XmlSchema.Read(schemaStream, (o, e) =>
      {
        throw new ConfigurationErrorsException("Error reading default media type compression map. " + e.Message, e.Exception);
      });
      Stream defaultStream = null;
      try
      {
        if(!string.IsNullOrEmpty(DefaultFile)) defaultStream = File.OpenRead(DefaultFile);
        else defaultStream = DAVUtility.GetManifestResourceStream("Resources/Compression.xml");

        XmlReaderSettings settings = new XmlReaderSettings()
        {
          CloseInput = true, IgnoreComments = true, IgnoreWhitespace = true, ValidationType = ValidationType.Schema
        };
        settings.Schemas.Add(schema);
        using(XmlReader reader = XmlReader.Create(defaultStream, settings))
        {
          if(reader.Read()) // if there's a root element...
          {
            if(reader.NodeType == XmlNodeType.XmlDeclaration) reader.Read();
            while(reader.Read() && reader.NodeType == XmlNodeType.Element) // for each 'entry' element...
            {
              BaseAdd(new CompressionMapElement()
              {
                MediaTypePattern = reader.GetAttribute("mediaType"), Compress = reader.GetBoolAttribute("compress", true),
              });
              if(!reader.IsEmptyElement) reader.Read();
            }
          }
        }
      }
      finally
      {
        Utility.Dispose(defaultStream);
      }
    }

    ResetModified(); // mark the default elements as not part of the changes that would need to be serialized
  }
}
#endregion

#region CompressionMapElement
/// <summary>Implements a <see cref="ConfigurationElement"/> that specifies whether resources matching a media type pattern should be
/// compressed.
/// </summary>
public sealed class CompressionMapElement : ConfigurationElement
{
  /// <summary>Gets whether the extension is considered the canonical extension for the media type.</summary>
  [ConfigurationProperty("compress", DefaultValue=true), TypeConverter(typeof(BooleanConverter))]
  public bool Compress
  {
    get { return (bool)this["compress"]; }
    internal set { this["compress"] = value; }
  }

  /// <summary>Gets the media type pattern.</summary>
  [ConfigurationProperty("mediaType", IsKey=true, IsRequired=true, DefaultValue="*")]
  [RegexStringValidator(@"^[a-zA-Z0-9!#\$%&'\*\+\-\.\^_`\|~]+(?:/[a-zA-Z0-9!#\$%&'\*\+\-\.\^_`\|~]+)?$")]
  public string MediaTypePattern
  {
    get { return (string)this["mediaType"]; }
    internal set { this["mediaType"] = value; }
  }
}
#endregion

#region CustomElementCollection
/// <summary>Implements a base class for custom <see cref="ConfigurationElement"/> objects.</summary>
public abstract class CustomElementCollection<T> : ConfigurationElementCollection, IEnumerable<T> where T : ConfigurationElement
{
  /// <summary>Initializes a new <see cref="CustomElementCollection{T}"/> with the default key comparer.</summary>
  protected CustomElementCollection() : base() { }

  /// <summary>Initializes a new <see cref="CustomElementCollection{T}"/> with the given key comparer.</summary>
  protected CustomElementCollection(System.Collections.IComparer keyComparer) : base(keyComparer) { }

  /// <inheritdoc/>
  public override ConfigurationElementCollectionType CollectionType
  {
    get { return ConfigurationElementCollectionType.AddRemoveClearMap; }
  }

  /// <summary>Gets or sets the <see cref="ConfigurationElement"/> at the given index.</summary>
  public T this[int index]
  {
    get { return (T)BaseGet(index); }
    set
    {
      if(value == null) throw new ArgumentNullException();
      BaseRemoveAt(index);
      BaseAdd(index, value);
    }
  }

  /// <summary>Adds a <see cref="ConfigurationElement"/> to the collection.</summary>
  public void Add(T element)
  {
    if(element == null) throw new ArgumentNullException();
    BaseAdd(element);
  }

  /// <summary>Clears the collection.</summary>
  public void Clear()
  {
    BaseClear();
  }

  /// <summary>Inserts a <see cref="ConfigurationElement"/> into the collection at the given index.</summary>
  public void Insert(int index, T element)
  {
    if(element == null) throw new ArgumentNullException();
    BaseAdd(index, element);
  }

  /// <summary>Removes a <see cref="ConfigurationElement"/> from the collection.</summary>
  public void Remove(T element)
  {
    if(element == null) return;
    BaseRemove(GetElementKey(element));
  }

  /// <summary>Removes the configuration element at the given index.</summary>
  public void RemoveAt(int index)
  {
    BaseRemoveAt(index);
  }

  #region IEnumerable<T> Members
  /// <summary>Returns an enumerator that iterates through the configuration elements in the collection.</summary>
  public new IEnumerator<T> GetEnumerator()
  {
    foreach(T element in (ConfigurationElementCollection)this) yield return element;
  }
  #endregion

  /// <summary>Creates and returns a new instance of the configuration element.</summary>
  protected abstract T CreateElement();
  /// <summary>Returns a value that acts as the key for the given configuration element.</summary>
  protected abstract object GetElementKey(T element);

  /// <inheritdoc/>
  protected sealed override ConfigurationElement CreateNewElement()
  {
    return (T)CreateElement();
  }

  /// <inheritdoc/>
  protected sealed override object GetElementKey(ConfigurationElement element)
  {
    return GetElementKey((T)element);
  }
}
#endregion

#region LocationCollection
/// <summary>Implements a collection of <see cref="LocationElement"/> objects.</summary>
public sealed class LocationCollection : CustomElementCollection<LocationElement>
{
  /// <inheritdoc/>
  protected override LocationElement CreateElement()
  {
    return new LocationElement();
  }

  /// <inheritdoc/>
  protected override object GetElementKey(LocationElement element)
  {
    if(element == null) throw new ArgumentNullException();
    return element.Match;
  }
}
#endregion

#region LocationElement
/// <summary>Implements a <see cref="ConfigurationElement"/> representing a location.</summary>
public sealed class LocationElement : ConfigurationElement
{
  /// <summary>Initializes a new <see cref="LocationElement"/>.</summary>
  public LocationElement()
  {
    Parameters = new ParameterCollection();
  }

  /// <summary>Gets a collection of <see cref="AuthorizationFilterElement"/> that represent the authorization filters for the location.</summary>
  [ConfigurationProperty("authorization"), ConfigurationCollection(typeof(AuthorizationFilterCollection))]
  public AuthorizationFilterCollection AuthorizationFilters
  {
    get { return (AuthorizationFilterCollection)this["authorization"]; }
  }

  /// <summary>Gets whether path matching should be sensitive to case.</summary>
  [ConfigurationProperty("caseSensitive", DefaultValue=false), TypeConverter(typeof(BooleanConverter))]
  public bool CaseSensitive
  {
    get { return (bool)this["caseSensitive"]; }
  }

  /// <summary>Gets whether the location should process WebDAV requests.</summary>
  [ConfigurationProperty("enabled", DefaultValue=true), TypeConverter(typeof(BooleanConverter))]
  public bool Enabled
  {
    get { return (bool)this["enabled"]; }
  }

  /// <summary>Gets the unique, case-insensitive ID corresponding to this location. If null or empty, the ID should be computed based on
  /// the <see cref="Match"/> pattern.
  /// </summary>
  [ConfigurationProperty("id")]
  public string ID
  {
    get { return (string)this["id"]; }
  }

  /// <summary>Gets the <see cref="LockManagerElement"/> describing the default <see cref="ILockManager"/> to be used by the location.</summary>
  [ConfigurationProperty("davLockManager")] // I wanted to call this "lockManager", but apparently names starting with "lock" are reserved
  public LockManagerElement LockManager
  {
    get { return (LockManagerElement)this["davLockManager"]; }
  }

  /// <summary>Gets a string matching request URIs, of the form [[scheme://](hostname|IP)[:port]][/path/]</summary>
  [ConfigurationProperty("match", IsKey=true, IsRequired=true), RegexStringValidator(MatchPattern)]
  public string Match
  {
    get { return (string)this["match"]; }
  }

  /// <summary>Gets a collection of additional parameters for the <see cref="IWebDAVService"/>.</summary>
  public ParameterCollection Parameters { get; private set; }

  /// <summary>Gets the <see cref="PropertyStoreElement"/> describing the default <see cref="IPropertyStore"/> to be used by the location.</summary>
  [ConfigurationProperty("propertyStore")]
  public PropertyStoreElement PropertyStore
  {
    get { return (PropertyStoreElement)this["propertyStore"]; }
  }

  /// <summary>Gets whether the location should serve OPTIONS requests to the root of the server even if it would not normally serve the
  /// root. This option is provided as a workaround for WebDAV clients that incorrectly submit OPTIONS requests to the root of the server.
  /// </summary>
  [ConfigurationProperty("serveRootOptions", DefaultValue=false), TypeConverter(typeof(BooleanConverter))]
  public bool ServeRootOptions
  {
    get { return (bool)this["serveRootOptions"]; }
  }

  /// <summary>Gets the type implementing the <see cref="IWebDAVService"/> interface, used to service requests for the location.</summary>
  [ConfigurationProperty("type"), TypeConverter(typeof(TypeNameConverter)), SubclassTypeValidator(typeof(IWebDAVService))]
  public Type Type
  {
    get { return (Type)this["type"]; }
  }

  /// <inheritdoc/>
  protected override bool OnDeserializeUnrecognizedAttribute(string name, string value)
  {
    Parameters.Set(name, value); // save unrecognized attributes so we can pass them as parameters to objects
    return true;
  }

  internal const string MatchPattern = @"^(?:(?:(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*)://)?(?:(?<ipv4>(?:\d{1,3}\.){3}\d{1,3})|(?<hostname>[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9]))*)|\[(:ipv6(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{1,4})\])(?::(?<port>\d{1,5}))?)?(?:/(?<path>[^/]+(?:/[^/]+)*/?)?)?$";
}
#endregion

#region LockManagerElement
/// <summary>Implements a <see cref="ConfigurationElement"/> that specifies the <see cref="ILockManager"/> to use for the WebDAV server.</summary>
public sealed class LockManagerElement : TypeElementBase<ILockManager>
{
  /// <summary>Gets the type implementing the <see cref="ILockManager"/> interface.</summary>
  [ConfigurationProperty("type"), TypeConverter(typeof(TypeNameConverter)), SubclassTypeValidator(typeof(ILockManager))]
  public Type Type
  {
    get { return InnerType; }
  }
}
#endregion

#region MediaMapCollection
/// <summary>Implements a collection of <see cref="MediaMapElement"/> objects.</summary>
public sealed class MediaMapCollection : CustomElementCollection<MediaMapElement>
{
  /// <summary>Initializes a new <see cref="MediaMapCollection"/>.</summary>
  public MediaMapCollection() : base(new KeyComparer()) { }

  /// <summary>Gets the file extension to use when constructing a file name from an unknown media type, excluding the leading period, or
  /// null or empty to use the default from <see cref="DefaultFile"/>.
  /// </summary>
  [ConfigurationProperty("defaultExtension"), RegexStringValidator(MediaMapElement.ExtensionPattern + "|^$")]
  public string DefaultExtensionOverride
  {
    get { return (string)this["defaultExtension"]; }
  }

  /// <summary>Gets the name of the file from which the default elements should be taken. If null or empty, elements will be taken from an
  /// internal media type map.
  /// </summary>
  [ConfigurationProperty("defaultFile")]
  public string DefaultFile
  {
    get { return (string)this["defaultFile"]; }
  }

  /// <summary>Gets the media type to report when guessing a media type from an unknown file extension, or null or empty to use the default
  /// from <see cref="DefaultFile"/>.
  /// </summary>
  [ConfigurationProperty("defaultMediaType"), RegexStringValidator(MediaMapElement.MediaTypePattern + "|^$")]
  public string DefaultMediaTypeOverride
  {
    get { return (string)this["defaultMediaType"]; }
  }

  /// <summary>Gets the file extension to use when constructing a file name from an unknown media type, excluding the leading period, or
  /// null if there is no default.
  /// </summary>
  public string GetDefaultExtension()
  {
    return StringUtility.Coalesce(DefaultExtensionOverride, defaultExtension);
  }

  /// <summary>Gets the media type to report when guessing a media type from an unknown file extension, or null if there is no default.</summary>
  public string GetDefaultMediaType()
  {
    return StringUtility.Coalesce(DefaultMediaTypeOverride, defaultMediaType);
  }

  /// <inheritdoc/>
  protected override MediaMapElement CreateElement()
  {
    return new MediaMapElement();
  }

  /// <inheritdoc/>
  protected override object GetElementKey(MediaMapElement element)
  {
    if(element == null) throw new ArgumentNullException();
    string mediaType = element.MediaType, extension = element.Extension;
    return string.IsNullOrEmpty(mediaType) ? extension : string.IsNullOrEmpty(extension) ? mediaType : mediaType + "<" + extension;
  }

  /// <inheritdoc/>
  protected override bool ThrowOnDuplicate
  {
    get { return false; } // allow the user to overwrite default mappings without removing them first
  }

  /// <inheritdoc/>
  protected override void Init()
  {
    base.Init();

    using(Stream schemaStream = DAVUtility.GetManifestResourceStream("Resources/MediaTypes.xsd"))
    {
      XmlSchema schema = XmlSchema.Read(schemaStream, (o, e) =>
      {
        throw new ConfigurationErrorsException("Error reading default media type map. " + e.Message, e.Exception);
      });
      Stream defaultStream = null;
      try
      {
        if(!string.IsNullOrEmpty(DefaultFile)) defaultStream = File.OpenRead(DefaultFile);
        else defaultStream = DAVUtility.GetManifestResourceStream("Resources/MediaTypes.xml");

        XmlReaderSettings settings = new XmlReaderSettings()
        {
          CloseInput = true, IgnoreComments = true, IgnoreWhitespace = true, ValidationType = ValidationType.Schema
        };
        settings.Schemas.Add(schema);
        using(XmlReader reader = XmlReader.Create(defaultStream, settings))
        {
          if(reader.Read()) // if there's a root element...
          {
            if(reader.NodeType == XmlNodeType.XmlDeclaration) reader.Read();
            defaultExtension = reader.GetAttribute("defaultExtension");
            defaultMediaType  = reader.GetAttribute("defaultMediaType");
            while(reader.Read() && reader.NodeType == XmlNodeType.Element) // for each 'entry' element...
            {
              BaseAdd(new MediaMapElement()
              {
                MediaType = reader.GetAttribute("mediaType"), CanonicalMediaType = reader.GetBoolAttribute("canonicalMediaType", true),
                Extension = reader.GetAttribute("extension"), CanonicalExtension = reader.GetBoolAttribute("canonicalExtension")
              });
              if(!reader.IsEmptyElement) reader.Read();
            }
          }
        }
      }
      finally
      {
        Utility.Dispose(defaultStream);
      }
    }

    ResetModified(); // mark the default elements as not part of the changes that would need to be serialized
  }

  #region KeyComparer
  sealed class KeyComparer : System.Collections.IComparer
  {
    public int Compare(object ao, object bo)
    {
      string a = ao as string, b = bo as string;
      if(string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : -1;
      else if(string.IsNullOrEmpty(a)) return 1;

      int aMediaEnd, aExtensionStart, bMediaEnd, bExtensionStart;
      Parse(a, out aMediaEnd, out aExtensionStart);
      Parse(b, out bMediaEnd, out bExtensionStart);

      if(aMediaEnd == -1) // if A has only an extension...
      {
        return bExtensionStart == -1 ? 1 : // if B has only a media type, then there can be no match
          string.Compare(a, aExtensionStart, b, bExtensionStart, int.MaxValue, StringComparison.OrdinalIgnoreCase);
      }
      else if(aExtensionStart == -1) // if A has only a media type...
      {
        return bMediaEnd == -1 ? -1 : string.Compare(a, 0, b, 0, Math.Max(aMediaEnd, bMediaEnd), StringComparison.OrdinalIgnoreCase);
      }
      else // A has both a media type and an extension
      {
        int cmp = bMediaEnd == -1 ? 0 : string.Compare(a, 0, b, 0, Math.Max(aMediaEnd, bMediaEnd), StringComparison.OrdinalIgnoreCase);
        if(cmp == 0)
        {
          cmp = bExtensionStart == -1 ? 0 :
            string.Compare(a, aExtensionStart, b, bExtensionStart, int.MaxValue, StringComparison.OrdinalIgnoreCase);
        }
        return cmp;
      }
    }

    static void Parse(string key, out int mediaEnd, out int extensionStart)
    {
      int pipe = key.IndexOf('<');
      if(pipe == -1)
      {
        if(key.IndexOf('/') == -1) { mediaEnd = -1; extensionStart = 0; }
        else { mediaEnd = key.Length; extensionStart = -1; }
      }
      else
      {
        mediaEnd       = pipe;
        extensionStart = pipe+1;
      }
    }
  }
  #endregion

  string defaultExtension, defaultMediaType;
}
#endregion

#region MediaMapElement
/// <summary>Implements a <see cref="ConfigurationElement"/> mapping a media type to and/or from a file extension.</summary>
public sealed class MediaMapElement : ConfigurationElement
{
  /// <summary>Gets whether the extension is considered the canonical extension for the media type.</summary>
  [ConfigurationProperty("canonicalExtension", DefaultValue=false), TypeConverter(typeof(BooleanConverter))]
  public bool CanonicalExtension
  {
    get { return (bool)this["canonicalExtension"]; }
    internal set { this["canonicalExtension"] = value; }
  }

  /// <summary>Gets whether the media type is considered the canonical media type for the extension.</summary>
  [ConfigurationProperty("canonicalMediaType", DefaultValue=true), TypeConverter(typeof(BooleanConverter))]
  public bool CanonicalMediaType
  {
    get { return (bool)this["canonicalMediaType"]; }
    internal set { this["canonicalMediaType"] = value; }
  }

  /// <summary>Gets the file extension, without a leading period.</summary>
  [ConfigurationProperty("extension", IsKey=true)]
  public string Extension
  {
    get { return (string)this["extension"]; }
    internal set { this["extension"] = value; }
  }

  /// <summary>Gets the media type.</summary>
  [ConfigurationProperty("mediaType", IsKey=true)]
  public string MediaType
  {
    get { return (string)this["mediaType"]; }
    internal set { this["mediaType"] = value; }
  }

  /// <inheritdoc/>
  protected override void DeserializeElement(XmlReader reader, bool serializeCollectionKey)
  {
    elementName = reader.LocalName;
    base.DeserializeElement(reader, serializeCollectionKey);
  }

  /// <inheritdoc/>
  protected override void PostDeserialize()
  {
    base.PostDeserialize();

    if(elementName != "clear")
    {
      bool extMatch = extensionRe.IsMatch(Extension), mediaMatch = mediaTypeRe.IsMatch(MediaType);
      if(elementName == "remove")
      {
        if(!extMatch && !mediaMatch)
        {
          throw new ConfigurationErrorsException("At least one of the extension or mediaType attributes is required.");
        }
      }
      else
      {
        if(!extMatch)
        {
          if(string.IsNullOrEmpty(Extension)) throw new ConfigurationErrorsException("The extension attribute is required.");
          else throw new ConfigurationErrorsException("Extension \"" + Extension + "\" doesn't match pattern " + ExtensionPattern);
        }
        if(!mediaMatch)
        {
          if(string.IsNullOrEmpty(MediaType)) throw new ConfigurationErrorsException("The mediaType attribute is required.");
          else throw new ConfigurationErrorsException("Media type \"" + MediaType + "\" doesn't match pattern " + MediaTypePattern);
        }
      }
    }
  }

  string elementName;

  // we can't use RegexStringValidator on the properties, unfortunately, because it seems to validate all properties when one property is
  // set programmatically, preventing us from initializing the element one property at a time
  internal const string ExtensionPattern = @"^[^\.].*$";
  internal const string MediaTypePattern = @"^[a-zA-Z0-9!#\$%&'\*\+\-\.\^_`\|~]+/[a-zA-Z0-9!#\$%&'\*\+\-\.\^_`\|~]+$";
  static readonly Regex extensionRe = new Regex(ExtensionPattern, RegexOptions.Compiled | RegexOptions.Singleline);
  static readonly Regex mediaTypeRe = new Regex(MediaTypePattern, RegexOptions.Compiled | RegexOptions.Singleline);
}
#endregion

#region ParameterCollection
/// <summary>Contains additional key/value pairs representing parameters to <see cref="IWebDAVService"/> and
/// <see cref="IAuthorizationFilter"/> objects that were specified in the application configuration file.
/// </summary>
[Serializable]
public sealed class ParameterCollection : AccessLimitedDictionaryBase<string, string>
{
  internal ParameterCollection() : base(new Dictionary<string,string>()) { }

  /// <inheritdoc/>
  public override bool IsReadOnly
  {
    get { return true; }
  }

  internal void Set(string key, string value)
  {
    Items[key] = value;
  }
}
#endregion

#region PropertyStoreElement
/// <summary>Implements a <see cref="ConfigurationElement"/> representing an <see cref="IPropertyStore"/>.</summary>
public sealed class PropertyStoreElement : TypeElementBase<IPropertyStore>
{
  /// <summary>Gets the type implementing the <see cref="IPropertyStore"/> interface.</summary>
  [ConfigurationProperty("type"), TypeConverter(typeof(TypeNameConverter)), SubclassTypeValidator(typeof(IPropertyStore))]
  public Type Type
  {
    get { return InnerType; }
  }
}
#endregion

#region TypeElementBase
/// <summary>Provides a base class for implementing a <see cref="ConfigurationElement"/> representing an a type that can accept parameters.</summary>
public abstract class TypeElementBase<T> : ConfigurationElement
{
  /// <summary>Initializes a new <see cref="TypeElementBase{T}"/>.</summary>
  protected TypeElementBase()
  {
    Parameters = new ParameterCollection();
  }

  /// <summary>Gets a collection of additional parameters for the type.</summary>
  public ParameterCollection Parameters { get; private set; }

  /// <summary>Returns the configured type.</summary>
  protected internal Type InnerType
  {
    get { return (Type)this["type"]; }
  }

  /// <inheritdoc/>
  protected override bool OnDeserializeUnrecognizedAttribute(string name, string value)
  {
    Parameters.Set(name, value); // save unrecognized attributes so we can pass them as parameters
    return true;
  }
}
#endregion

#region WebDAVServerSection
/// <summary>Implements a <see cref="ConfigurationSection"/> that contains the FlairPoint configuration.</summary>
public sealed class WebDAVServerSection : ConfigurationSection
{
  /// <summary>Gets a collection of <see cref="CompressionMapElement"/> that represent the configured media type compression map.</summary>
  [ConfigurationProperty("compression"), ConfigurationCollection(typeof(CompressionMapCollection))]
  public CompressionMapCollection CompressionMap
  {
    get { return (CompressionMapCollection)this["compression"]; }
  }

  /// <summary>Gets whether the WebDAV service should be enabled.</summary>
  [ConfigurationProperty("enabled", DefaultValue=true), TypeConverter(typeof(BooleanConverter))]
  public bool Enabled
  {
    get { return (bool)this["enabled"]; }
  }

  /// <summary>Gets a collection of <see cref="LocationElement"/> that represent the configured locations.</summary>
  [ConfigurationProperty("locations"), ConfigurationCollection(typeof(LocationCollection))]
  public LocationCollection Locations
  {
    get { return (LocationCollection)this["locations"]; }
  }

  /// <summary>Gets the <see cref="LockManagerElement"/> describing the <see cref="ILockManager"/> to be used by the WebDAV server.</summary>
  [ConfigurationProperty("davLockManager")] // I wanted to call this "lockManager", but apparently names starting with "lock" are reserved
  public LockManagerElement LockManager
  {
    get { return (LockManagerElement)this["davLockManager"]; }
  }

  /// <summary>Gets a collection of <see cref="MediaMapElement"/> that represent the configured media type map.</summary>
  [ConfigurationProperty("mediaTypeMap"), ConfigurationCollection(typeof(MediaMapCollection))]
  public MediaMapCollection MediaTypeMap
  {
    get { return (MediaMapCollection)this["mediaTypeMap"]; }
  }

  /// <summary>Gets the <see cref="PropertyStoreElement"/> describing the default <see cref="IPropertyStore"/> to be used by the WebDAV
  /// server.
  /// </summary>
  [ConfigurationProperty("propertyStore")]
  public PropertyStoreElement PropertyStore
  {
    get { return (PropertyStoreElement)this["propertyStore"]; }
  }

  /// <summary>Gets whether to show potentially sensitive error messages when exceptions occur.</summary>
  [ConfigurationProperty("showSensitiveErrors", DefaultValue=false), TypeConverter(typeof(BooleanConverter))]
  public bool ShowSensitiveErrors
  {
    get { return (bool)this["showSensitiveErrors"]; }
  }

  /// <summary>Gets the <see cref="WebDAVServerSection"/> containing the WebDAV configuration, or null if no WebDAV configuration section
  /// exists.
  /// </summary>
  public static WebDAVServerSection Get()
  {
    return ConfigurationManager.GetSection("AdamMil.WebDAV/server") as WebDAVServerSection;
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server.Configuration
