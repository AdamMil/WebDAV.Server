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
using System;
using System.Collections.Generic;
using System.Text;
using AdamMil.Collections;

// TODO: add processing examples and documentation

namespace AdamMil.WebDAV.Server
{

/// <summary>Represents an <c>OPTIONS</c> request.</summary>
/// <remarks>The <c>OPTIONS</c> request is described in section 9.2 of RFC 2616 and various sections (e.g. section 10.1) of RFC 4918.</remarks>
public class OptionsRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="OptionsRequest"/> based on a new WebDAV request.</summary>
  public OptionsRequest(WebDAVContext context) : base(context)
  {
    AllowedMethods      = new AllowedMethodCollection(DefaultMethods);
    AllowPartialGet     = true;
    IsDAVCompliant      = true;
    IsServerQuery       = string.Equals(context.Request.RawUrl, "*", StringComparison.Ordinal); // see RFC 2616 section 9.2
    SupportedExtensions = new SupportedExtensionCollection();
  }

  #region AllowedMethodCollection
  /// <summary>A collection containing the names of supported HTTP methods.</summary>
  public sealed class AllowedMethodCollection : NonEmptyStringSet
  {
    internal AllowedMethodCollection(string[] items) : base(items) { }
  }
  #endregion

  #region SupportedExtensionCollection
  /// <summary>A collection containing the supported WebDAV extensions.</summary>
  public sealed class SupportedExtensionCollection : SetBase<string>
  {
    internal SupportedExtensionCollection() { }

    /// <summary>Adds an extension described by an absolute URI to the list of extensions.</summary>
    public bool Add(Uri uri)
    {
      if(uri == null) throw new ArgumentNullException();
      return Add("<" + uri.ToString() + ">");
    }

    /// <inheritdoc/>
    protected override bool AddItem(string item)
    {
      if(item == null) throw new ArgumentNullException();
      if(!IsToken(item) && !IsCodedURL(item)) throw new ArgumentException("Extension strings must be HTTP token values or coded URLs.");
      return base.AddItem(item);
    }

    /// <summary>Determines whether the value is a coded URL as defined by section 10.1 of RFC 4918.</summary>
    static bool IsCodedURL(string value)
    {
      if(value.Length >= 3 && value[0] == '<' && value[value.Length-1] == '>' &&
         !char.IsWhiteSpace(value[1]) && !char.IsWhiteSpace(value[value.Length-2]))
      {
        Uri uri;
        if(Uri.TryCreate(value.Substring(1, value.Length-2), UriKind.Absolute, out uri)) return true;
      }
      return false;
    }

    /// <summary>Determines whether the value is a token as defined by section 2.2 of RFC 2616.</summary>
    static bool IsToken(string value)
    {
      for(int i=0; i<value.Length; i++)
      {
        char c = value[i];
        if(c <= 32 || c >= 0x7f || Array.BinarySearch(illegalTokenChars, c) >= 0) return false;
      }
      return true;
    }

    /// <summary>A list of printable characters that are not legal in tokens, sorted by ASCII value.</summary>
    static readonly char[] illegalTokenChars = { '"', '(', ')', ',', '/', ':', ';', '<', '=', '>', '?', '@', '[', '\\', ']', '{', '}', };
  }
  #endregion

  /// <summary>Gets a collection that should be filled with the list of supported HTTP methods for the request URL. By default, the
  /// collection contains <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c>, and <c>TRACE</c>. In addition, <c>PROPFIND</c>, <c>PROPPATCH</c>,
  /// <c>COPY</c>, <c>MOVE</c>, and <c>MKCOL</c> will be treated as effectively present for all WebDAV-compliant resources, as required by
  /// RFC 4918 sections 9.1, 9.2, 9.3, 9.8, and 9.9. Similarly, <c>LOCK</c> and <c>UNLOCK</c> will advertised automatically if
  /// <see cref="SupportsLocking"/> is true. Generally, this default and automatic behavior is sufficient for read-only resources.
  /// Writable resources should implement <see cref="IWebDAVResource.Options"/> to add additional methods.
  /// </summary>
  public AllowedMethodCollection AllowedMethods { get; private set; }

  /// <summary>Gets or sets whether the resource or service supports partial <c>GET</c> responses using the HTTP <c>Range</c> header. Since
  /// the <see cref="GetOrHeadRequest.WriteStandardResponse(System.IO.Stream)"/> method supports partial <c>GET</c> responses and it is
  /// assumed that <see cref="GetOrHeadRequest.WriteStandardResponse(System.IO.Stream)"/> will be used in most cases, the default is true.
  /// </summary>
  /// <remarks>This property controls the value of the <c>Accept-Ranges</c> header sent in the response.</remarks>
  public bool AllowPartialGet { get; private set; }

  /// <summary>Gets or sets whether the resource or service is DAV-compliant, as defined by RFC 4918. The default is true.</summary>
  /// <remarks>This property determines whether a <c>DAV</c> header will be sent to the client to advertise WebDAV support.</remarks>
  public bool IsDAVCompliant { get; set; }

  /// <summary>Gets whether the <c>OPTIONS</c> request should pertain to the entire service, as opposed to just the request URI.</summary>
  /// <remarks>This corresponds to a client sending an <c>OPTIONS *</c> request.</remarks>
  public bool IsServerQuery { get; private set; }

  /// <summary>Gets a collection that can be filled with additional WebDAV extensions supported by the resource or service.</summary>
  /// <remarks>The extensions will be reported in the <c>DAV</c> header. This property is ignored if <see cref="IsDAVCompliant"/> is set to
  /// false.
  /// </remarks>
  public SupportedExtensionCollection SupportedExtensions { get; private set; }

  /// <summary>Gets or sets whether the resource or service provides a minimal set of WebDAV locking support. The default is false.</summary>
  /// <remarks>
  /// At a minimum, a service or resource that supports locking must support the including the <c>LOCK</c> and <c>UNLOCK</c> methods, the
  /// <c>DAV:supportedlock</c> and <c>DAV:lockdiscovery</c> properties, the <c>Timeout</c> header, and the <c>Lock-Token</c> header. It
  /// should also support the <c>DAV:owner</c> element. Locking of existing resources can be accomplished by implementing the
  /// <see cref="IWebDAVResource.Lock"/> and <see cref="IWebDAVResource.Unlock"/> methods (using
  /// <see cref="LockRequest.ProcessStandardRequest(IEnumerable{LockType},bool)"/> and <see cref="UnlockRequest.ProcessStandardRequest"/>)
  /// and providing values for the <c>DAV:supportedlock</c> and <c>DAV:lockdiscovery</c> properties. Locking new resources can be supported
  /// by implementing the <see cref="IWebDAVService.CreateAndLock"/> method.
  /// <para>
  /// Lock support will be reported in the <c>DAV</c> header. This property is ignored if <see cref="IsDAVCompliant"/> is set to false.
  /// </para>
  /// </remarks>
  public bool SupportsLocking { get; set; }

  /// <summary>Gets whether this <c>OPTIONS</c> request is out of the service's scope. This can only happen if the <c>serveRootOptions</c>
  /// option is enabled for the service (in web.config). When an <c>OPTIONS</c> request is out of scope, the service should not write the
  /// response itself. It should only add headers to customize the web server's default processing, and it should exclude resource-specific
  /// headers. (For instance, the <see cref="AllowPartialGet"/> property will be ignored and the <c>Accept-Ranges</c> header will not be
  /// added to out-of-scope requests because the DAV service does not have the authority to declare that whichever service handles the
  /// out-of-scope resource supports partial <c>GET</c> requests.)
  /// </summary>
  protected bool OutOfScope { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>The default implementation </remarks>
  protected internal override void WriteResponse()
  {
    // report support for encoded bodies and partial transfers (but only if the request is in the WebDAV service's scope)
    if(!OutOfScope)
    {
      Context.Response.Headers[HttpHeaders.AcceptEncoding] = "gzip, deflate"; // we support gzip and deflate encodings by default
      Context.Response.Headers[HttpHeaders.AcceptRanges] = AllowPartialGet ? "bytes" : "none"; // see RFC 2616 section 14.5
    }

    bool useEmptyResponseHack = false;
    if(IsDAVCompliant || AllowedMethods.Count != 0)
    {
      StringBuilder sb = new StringBuilder();

      // if the request was in scope, allow it to write the Allow header
      if(!OutOfScope)
      {
        // get the set of support HTTP methods
        IEnumerable<string> methods = AllowedMethods;
        if(IsDAVCompliant) // if the resource or service is DAV-compliant, report PROPFIND, PROPPATCH, COPY, MOVE, and MKCOL as well, as
        {                  // required by RFC 4918 (sections 9.1, 9.2, 9.3, 9.8, and 9.9)
          HashSet<string> set = new HashSet<string>(methods);
          set.Add(HttpMethods.PropFind);
          set.Add(HttpMethods.PropPatch);
          set.Add(HttpMethods.Copy);
          set.Add(HttpMethods.Move);
          set.Add(HttpMethods.MkCol);
          if(SupportsLocking) // if the resource or service claims to support locking, then it must support LOCK and UNLOCK
          {
            set.Add(HttpMethods.Lock);
            set.Add(HttpMethods.Unlock);
          }
          methods = set;
        }

        // add the supported methods to the Allow header
        foreach(string method in methods)
        {
          if(sb.Length != 0) sb.Append(", ");
          sb.Append(method);
        }
        Context.Response.Headers[HttpHeaders.Allow] = sb.ToString();
      }

      // get the level of DAV compliance and write the DAV header. we'll do this even if the request is out of scope. (in fact, writing
      // this header is the whole reason we support out-of-scope OPTIONS requests.)
      if(IsDAVCompliant)
      {
        sb.Length = 0;
        sb.Append("1"); // 1 means we support WebDAV
        if(SupportsLocking) sb.Append(", 2"); // 2 means we support a basic set of locking features
        sb.Append(", 3"); // 3 means we support the revised RFC 4918 standard rather than the older RFC 2518 standard
        if(SupportedExtensions.Count != 0)
        {
          foreach(string extension in SupportedExtensions) sb.Append(", ").Append(extension);
        }
        Context.Response.Headers[HttpHeaders.DAV] = sb.ToString();

        // the Microsoft Web Folder client prefers to use the Frontend protocol so much that it may refuse to use WebDAV unless we
        // add a special header. it also fails to process 204 No Content responses correctly, so we'll use 200 OK instead
        if(Context.Request.UserAgent != null && Context.Request.UserAgent.StartsWith("Microsoft ", StringComparison.Ordinal))
        {
          Context.Response.Headers["MS-Author-Via"] = "DAV";
          Status = ConditionCodes.OK;
          useEmptyResponseHack = true;
        }
      }
    }

    // now that we've added the headers we want, call the base class to write the response. we don't do that for out-of-scope requests,
    // though, because we want to allow the service with authority over the request URL to have the final say in what gets written
    if(!OutOfScope)
    {
      if(useEmptyResponseHack && Status != null && Status.IsSuccessful)
      {
        Context.Response.StatusCode        = Status.StatusCode;
        Context.Response.StatusDescription = DAVUtility.GetStatusCodeMessage(Status.StatusCode);
      }
      else
      {
        base.WriteResponse();
      }
    }
  }

  /// <summary>Called to indicate that the <c>OPTIONS</c> request is out of the service's scope. (It is still being served because the
  /// service has the <c>serveRootOptions</c> option enabled.)
  /// </summary>
  internal void SetOutOfScope()
  {
    IsServerQuery = true;
    OutOfScope    = true;
  }

  static readonly string[] DefaultMethods = new string[] { HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace };
}

} // namespace AdamMil.WebDAV.Server
