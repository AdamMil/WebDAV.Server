/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2013 by Adam Milazzo.

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
namespace AdamMil.WebDAV.Server
{

/// <summary>Represents an object that can deny the requesting user access to execute the request.</summary>
public interface ISupportAuthorization
{
  /// <include file="documentation.xml" path="/DAV/ISupportAuthorization/ShouldDenyAccess/node()" />
  bool ShouldDenyAccess(WebDAVContext context, out bool denyExistence);
}

// TODO: should an authorization filter be able to filter children/descendants on recursive queries as well?
/// <summary>Represents a filter that can deny the requesting user access to execute the request, even the user would otherwise be allowed.</summary>
/// <remarks>Note that the <see cref="WebDAVContext.RequestResource"/> property may be null when the filter is invoked, if the request was
/// made to an unmapped URL.
/// </remarks>
public interface IAuthorizationFilter : ISupportAuthorization
{
  /// <summary>Gets whether the authorization filter is safe for reuse across multiple requests. In order to be safe, a filter must be
  /// able to be accessed simultaneously by multiple threads, and should not fail in a way that would affect its operation on later
  /// requests.
  /// </summary>
  bool IsReusable { get; }
}

} // namespace AdamMil.WebDAV.Server
