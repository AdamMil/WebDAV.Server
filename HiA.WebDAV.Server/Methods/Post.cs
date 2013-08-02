/*
 * Version: EUPL 1.1
 * 
 * The contents of this file are subject to the European Union Public Licence Version 1.1 (the "Licence"); 
 * you may not use this file except in compliance with the Licence. 
 * You may obtain a copy of the Licence at:
 * http://joinup.ec.europa.eu/software/page/eupl/licence-eupl
 */
// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>POST</c> request.</summary>
/// <remarks>The <c>POST</c> request is described in section 9.5 of RFC 4918 and section 9.5 of RFC 2616.</remarks>
public class PostRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="PostRequest"/> based on a new WebDAV request.</summary>
  public PostRequest(WebDAVContext context) : base(context) { }
}

} // namespace HiA.WebDAV.Server
