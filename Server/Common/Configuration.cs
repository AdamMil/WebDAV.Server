/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2016 by Adam Milazzo.

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
using AdamMil.Configuration;
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

  /// <summary>Gets a string matching request URIs, of the form [[scheme://]hostname[:port]][/path/]</summary>
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

  /// <summary>Gets whether the service object should be recreated when an internal error occurs.</summary>
  [ConfigurationProperty("resetOnError", DefaultValue=true), TypeConverter(typeof(BooleanConverter))]
  public bool ResetOnError
  {
    get { return (bool)this["resetOnError"]; }
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

  internal const string MatchPattern = @"^(?:(?:(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*)://)?(?:(?<hostname>[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9]))*))(?::(?<port>\d{1,5}))?)?(?:/(?<path>[^/]+(?:/[^/]+)*/?)?)?$";
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
/// <summary>Implements a <see cref="ConfigurationSection"/> that contains the WebDAV server configuration.</summary>
public sealed class WebDAVServerSection : ConfigurationSection
{
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
