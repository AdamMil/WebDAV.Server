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
using System.Xml;

// TODO: should an authorization filter be able to filter children/descendants on recursive queries as well?

namespace AdamMil.WebDAV.Server
{

#region IAuthorizationFilter
/// <summary>Represents a filter that can deny the requesting user access to execute the request, even if the user would otherwise be
/// allowed. The filter can also perform authentication to determine the current user.
/// </summary>
/// <remarks>An authorization filter must be usable across multiple requests on multiple threads simultaneously.</remarks>
public interface IAuthorizationFilter
{
  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/CanDeleteLock/node()" />
  bool? CanDeleteLock(WebDAVContext context, ActiveLock lockObject);
  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/GetCurrentUserId/node()" />
  bool GetCurrentUserId(WebDAVContext context, out string currentUserId);
  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/ShouldDenyAccess/node()" />
  bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, XmlQualifiedName access,
                        out bool denyExistence);
}
#endregion

#region AuthorizationFilter
/// <summary>Provides a base class to simplify the implementation of <see cref="IAuthorizationFilter"/>.</summary>
public abstract class AuthorizationFilter : IAuthorizationFilter
{
  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/CanDeleteLock/node()" />
  /// <remarks><note type="inherit">The default implementation returns null.</note></remarks>
  public virtual bool? CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  {
    return null;
  }

  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/GetCurrentUserId/node()" />
  /// <remarks><note type="inherit">The default implementation returns false.</note></remarks>
  public virtual bool GetCurrentUserId(WebDAVContext context, out string currentUserId)
  {
    currentUserId = null;
    return false;
  }

  /// <include file="documentation.xml" path="/DAV/IAuthorizationFilter/ShouldDenyAccess/node()" />
  /// <remarks><note type="inherit">The default implementation returns false.</note></remarks>
  public virtual bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, XmlQualifiedName access,
                                       out bool denyExistence)
  {
    denyExistence = false;
    return false;
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
