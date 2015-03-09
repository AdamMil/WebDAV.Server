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
namespace AdamMil.WebDAV.Server
{

// TODO: should an authorization filter be able to filter children/descendants on recursive queries as well?
/// <summary>Represents a filter that can deny the requesting user access to execute the request, even the user would otherwise be allowed.</summary>
/// <remarks>Note that the <see cref="WebDAVContext.RequestResource"/> property may be null when the filter is invoked, if the request was
/// made to an unmapped URL.
/// </remarks>
public interface IAuthorizationFilter
{
  /// <summary>Gets whether the authorization filter is safe for reuse across multiple requests. In order to be safe, a filter must be
  /// able to be accessed simultaneously by multiple threads, and should not fail in a way that would affect its operation on later
  /// requests.
  /// </summary>
  bool IsReusable { get; }

  /// <summary>Determines whether the user making a request should be denied access to a resource related to the request.</summary>
  /// <param name="context">The <see cref="WebDAVContext"/> in which the request is executing.</param>
  /// <param name="service">The <see cref="IWebDAVService"/> containing the resource to which access is being checked. This is not
  /// necessarily the same as <see cref="WebDAVContext.Service"/>.
  /// </param>
  /// <param name="resource">The <see cref="IWebDAVResource"/> to which access is being checked. This is not necessarily the same as
  /// <see cref="WebDAVContext.RequestResource"/>. However, if the value of this parameter is null, then access is being checked against
  /// the <see cref="WebDAVContext.RequestPath"/>.
  /// </param>
  /// <param name="denyExistence">If the user is to be denied access, this variable should receive a value that indicates whether the
  /// WebDAV service should deny the existence of the resource requested by the user by pretending that it was not found. If the user is
  /// allowed access, the value of this variable is ignored.
  /// </param>
  /// <returns>True if the user should be denied access, or false if the user may be permitted access. The user will be allowed access only
  /// if all calls to <c>ShouldDenyAccess</c> return false.
  /// </returns>
  bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, out bool denyExistence);
}

} // namespace AdamMil.WebDAV.Server
