/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2016-2016 by Adam Milazzo.

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
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.Utilities;

// although it's not the cleanest solution, this file demonstrates how to implement custom HTTP authentication using the Basic and Digest
// schemes. it also demonstrates how to associate arbitrary data with authenticated users. in this example, each user has a path that
// they're allowed to access and a flag that indicates whether they have write access to that path. there is also an admin user that has
// the ability to delete other users' locks.
//
// to use this sample, make the following changes to your web.config file. first, disable the built-in IIS authentication to prevent it
// from conflicting:
//   <system.web>
//     <authentication mode="None" />
//   </system.web>
// second, reference the CustomAuthDAVModule rather than the base WebDAVModule:
//   <system.webServer>
//     <!-- If you're using the Classic pipeline, you would add the module to system.web/httpModules instead. -->
//     <modules>
//       <add name="AdamMil.WebDAV" type="AdamMil.WebDAV.Server.Examples.CustomAuthDAVModule, AdamMil.WebDAV.Server.Examples" />
//     </modules>
//   </system.webServer>
// finally, add the custom authentication filter to one of the DAV locations. this step is not necessary if you just want to allow users to
// to authenticate themselves (and thereby expose data about themselves in WebDAVContext.User), but it is necessary if you want to deny
// them access based on their identities.
//   <locations>
//     <add match="/" ...>
//       <authorization>
//         <add type="AdamMil.WebDAV.Server.Examples.CustomAuthFilter, AdamMil.WebDAV.Server.Examples" />
//       </authorization>
//     </add>
//   </locations>

namespace AdamMil.WebDAV.Server.Examples
{

#region CustomAuthDAVModule
// this class extends the WebDAVModule to add custom HTTP authentication using the Basic and Digest schemes
public sealed class CustomAuthDAVModule : WebDAVModule
{
  // NOTE: you might want to get these from a configuration setting
  const string Realm = "MyHouse"; // the name of the HTTP authentication realm
  const string Secret = "8497e0dad33bb04a"; // this helps prevent nonce values from being forged

  protected override void Initialize(HttpApplication context)
  {
    base.Initialize(context);
    context.AuthenticateRequest += AuthenticateRequest;
    context.EndRequest          += EndRequest;
  }

  void AuthenticateRequest(object obj, EventArgs e)
  {
    HttpApplication app = (HttpApplication)obj;
    string authHeader = app.Request.Headers[DAVHeaders.Authorization];
    if(string.IsNullOrEmpty(authHeader)) return; // anonymous access

    string userName;
    IPrincipal principal;
    if(authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) // handle Basic authentication
    {
      string[] bits = iso8859.GetString(Convert.FromBase64String(authHeader.Substring(6).Trim())).Split(':', 2);
      userName = bits[0];
      if(bits.Length != 2 || GetUserData(userName, out principal) != bits[1]) goto badCredentials;
    }
    else if(authHeader.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)) // handle Digest authentication
    {
      // parse the authentication parameters
      Dictionary<string, string> authParams = new Dictionary<string, string>();
      for(int i=7; i<authHeader.Length; )
      {
        Match m = authRe.Match(authHeader, i);
        if(!m.Success) goto badHeader;
        string value = m.Groups["value"].Success ? m.Groups["value"].Value : null;
        if(!string.IsNullOrEmpty(value) && value[0] == '"') value = DAVUtility.UnquoteDecode(value);
        authParams[m.Groups["key"].Value.ToLowerInvariant()] = value;
        i = m.Index + m.Length;
        if(i < authHeader.Length)
        {
          if(authHeader[i] == ',') i++;
          else goto badHeader;
        }
      }

      // check the authorization parameters
      string algo = authParams.TryGetValue("algorithm"), qop = authParams.TryGetValue("qop");
      string nonce = authParams.TryGetValue("nonce"), realm = authParams.TryGetValue("realm");
      userName = authParams.TryGetValue("username");
      // if the realm is wrong, then apparently another module issued the authentication request. otherwise, if the parameters are
      // wrong, we can't handle it. in either case, the user gets anonymous access
      if(realm != Realm || !string.IsNullOrEmpty(algo) && algo != "MD5" || !string.IsNullOrEmpty(qop) && qop != "auth") return;
      if(userName == null || nonce == null) goto badHeader; // user name and nonce are required

      string password = GetUserData(userName, out principal);
      if(password == null) goto badCredentials; // if the user name is not recognized, don't bother calculating the digest

      bool isStale;
      if(!IsValidNonce(nonce, out isStale))
      {
        // if the nonce value was expired, remember that so we can let the client know
        if(isStale) app.Context.Items["AWSE.Auth.StaleNonce"] = true;
        goto badCredentials; // either way, the credentials are invalid
      }

      // see RFC 2617 for the calculation of the digest value
      string a1 = Hash(userName + ":" + realm + ":" + password), a2 = Hash(app.Request.HttpMethod + ":" + authParams.TryGetValue("uri"));
      string digest;
      if(string.IsNullOrEmpty(qop)) digest = Hash(a1 + ":" + nonce + ":" + a2);
      else digest = Hash(a1 + ":" + nonce + ":" + authParams.TryGetValue("nc") + ":" + authParams.TryGetValue("cnonce") + ":" + qop + ":" + a2);
      if(digest != authParams.TryGetValue("response")) goto badCredentials;
    }
    else // unknown authentication scheme
    {
      return; // let some other module handle it
    }

    // at this point we have valid credentials, so pass the principal to ASP.NET
    app.Context.User = principal;
    return;

    // if the credentials were invalid, we end up here
    badCredentials:
    app.Response.SetStatus(ConditionCodes.Unauthorized); // request authorization again
    app.CompleteRequest();
    return;

    // if the header was invalid we end up here
    badHeader:
    app.Response.SetStatus(ConditionCodes.BadRequest);
    app.CompleteRequest();
    return;
  }

  void EndRequest(object obj, EventArgs e)
  {
    // we'll respond to 401 Unauthorized regardless of where it came from. this is normally fine, but if you have other authentication
    // mechanisms you may want to limit our response if the 401 Unauthorized status didn't come from us
    HttpApplication app = (HttpApplication)obj;
    if(app.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
    {
      // NOTE: use this line instead if you want Basic authentication. this may be necessary if you don't have access to plaintext
      // passwords on the server. although Basic authentication is generally insecure, it can be secured by using SSL
      //app.Response.AppendHeader(DAVHeaders.WWWAuthenticate, "Basic realm=" + DAVUtility.QuoteString(Realm));

      object wasNonceStale = app.Context.Items["AWSE.Auth.StaleNonce"];
      StringBuilder sb = new StringBuilder();
      sb.Append("Digest realm=").Append(DAVUtility.QuoteString(Realm));
      sb.Append(", nonce=").Append(DAVUtility.QuoteString(CreateNonce()));
      sb.Append(", opaque=\"\", stale=").Append(wasNonceStale is bool && (bool)wasNonceStale ? "true" : "false");
      sb.Append(", algorithm=MD5, qop=\"auth\"");
      app.Response.AppendHeader(DAVHeaders.WWWAuthenticate, sb.ToString());
    }
  }

  static string CreateNonce()
  {
    // make the nonce expire after one minute. this forces the client to reauthenticate, but it shouldn't inconvenience the user because
    // the client software should handle it transparently
    string expirationTime = DateTime.UtcNow.AddMinutes(1).Ticks.ToStringInvariant();
    return expirationTime + " " + Hash(expirationTime + ":" + Secret); // hash the expiration time with a secret value to prevent forgery
  }

  // NOTE: this is where you would look up user data. if you don't have access to plaintext passwords, you can modify this to return a
  // password hash, but then you'll be limited to using Basic authentication. Basic authentication is secure over SSL, however.
  /// <summary>Retrieves information about the named user, and returns the user's password.</summary>
  /// <param name="userName">The login name of the user.</param>
  /// <param name="principal">A variable that receives a <see cref="CustomPrincipal"/> describing the user. This should be null if the
  /// user name does not exist.
  /// </param>
  /// <returns>Returns the user's password.</returns>
  static string GetUserData(string userName, out IPrincipal principal)
  {
    if(userName == "user1")
    {
      principal = new CustomPrincipal(userName, "user1/", false); // user 1 has read-only access to user1/
      return "pass1";
    }
    else if(userName == "user2")
    {
      principal = new CustomPrincipal(userName, "user2/", true); // user 2 has write access to user2/
      return "pass2";
    }
    else if(userName == "admin")
    {
      principal = new CustomPrincipal(userName, "", true); // the admin has write access to all directories
      return "badmin";
    }
    else // the user is unknown
    {
      principal = null;
      return null;
    }
  }

  static string Hash(string value)
  {
    using(MD5 md5 = MD5.Create()) return BinaryUtility.ToHex(md5.ComputeHash(iso8859.GetBytes(value)), true);
  }

  static bool IsValidNonce(string nonce, out bool isStale)
  {
    isStale = false;
    long expiration;
    string[] bits = nonce.Split(' ', 2);
    if(bits.Length != 2 || !InvariantCultureUtility.TryParse(bits[0], out expiration)) return false;
    isStale = DateTime.UtcNow.Ticks > expiration;
    return !isStale && bits[1] == Hash(bits[0] + ":" + Secret);
  }

  static readonly Regex authRe = new Regex(@"\s*(?<key>[^=\s]+)(=(?<value>""(\\.|[^""])*""|[^,\s]*))?",
                                           RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);
  static readonly Encoding iso8859 = Encoding.GetEncoding("iso-8859-1");
}
#endregion

#region CustomAuthFilter
// this class implements an authorization filter to enforce our custom access rules and demand authentication from unauthenticated users
public sealed class CustomAuthFilter : AuthorizationFilter
{
  public override bool? CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
  {
    CustomPrincipal user = context.User as CustomPrincipal;
    if(user != null && user.Name == "admin") return true; // the admin user can delete other users' locks
    else return null;
  }

  public override bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, XmlQualifiedName access,
                                        out ConditionCode response)
  {
    bool accessDenied = true; // deny access by default
    CustomPrincipal user = context.User as CustomPrincipal;
    // if there's a user authenticated by our custom module who has write access or doesn't need it...
    if(user != null && (user.WriteAccess || access != DAVNames.write))
    {
      // make sure they're accessing their own directory
      string resourcePath = DAVUtility.WithTrailingSlash(resource != null ? resource.CanonicalPath : context.RequestPath);
      StringComparison comparison = context.Settings.CaseSensitivePaths ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      if(resourcePath.StartsWith(user.RootPath, comparison)) accessDenied = false;
    }
    response = accessDenied ? ConditionCodes.Unauthorized : null; // request authorization if access is denied
    return accessDenied;
  }
}
#endregion

#region CustomPrincipal
// this class implements a custom IIdentity and IPrincpal to hold information about the users authenticated by our authentication module
sealed class CustomPrincipal : IIdentity, IPrincipal
{
  /// <summary>Initializes a new <see cref="CustomPrincipal"/>.</summary>
  /// <param name="userName">The login name of the user.</param>
  /// <param name="rootPath">The relative path to the root of the URI space that the user can access. The path should be minimally escaped,
  /// as by <see cref="DAVUtility.CanonicalPathEncode"/> or  <see cref="DAVUtility.CanonicalSegmentEncode"/>.
  /// </param>
  /// <param name="writeAccess">True if the user has write access to the directory named by <paramref name="rootPath"/>.</param>
  public CustomPrincipal(string userName, string rootPath, bool writeAccess)
  {
    Name        = userName;
    RootPath    = DAVUtility.WithTrailingSlash(rootPath);
    WriteAccess = writeAccess;
  }

  public readonly string Name, RootPath;
  public readonly bool WriteAccess;

  #region IIdentity Members
  string IIdentity.AuthenticationType
  {
    get { return "AWSE.Custom"; }
  }

  bool IIdentity.IsAuthenticated
  {
    get { return true; }
  }
  #endregion

  #region IPrincipal Members
  IIdentity IPrincipal.Identity
  {
    get { return this; }
  }

  bool IPrincipal.IsInRole(string role)
  {
    return false;
  }
  #endregion
}
#endregion

} // namespace AdamMil.WebDAV.Server.Examples
