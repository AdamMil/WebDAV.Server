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
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("AdamMil.net WebDAV Server")]
[assembly: AssemblyDescription("A generic WebDAV server for serving arbitrary hierarchical data.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
[assembly: AssemblyCompany("Webmedia")]
[assembly: AssemblyProduct("AdamMil.net")]
[assembly: AssemblyCopyright("Copyright © Nortal 2012-2013")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
