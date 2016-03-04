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

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents a <c>POST</c> request.</summary>
/// <remarks>The <c>POST</c> request is described in 4.3.3 of RFC 7231 and section 9.5 of RFC 4918.</remarks>
public class PostRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PostRequest"/> based on a new WebDAV request.</summary>
  public PostRequest(WebDAVContext context) : base(context) { }
}

} // namespace AdamMil.WebDAV.Server
