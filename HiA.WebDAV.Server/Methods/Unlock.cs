/*
 * Version: EUPL 1.1
 * 
 * The contents of this file are subject to the European Union Public Licence Version 1.1 (the "Licence"); 
 * you may not use this file except in compliance with the Licence. 
 * You may obtain a copy of the Licence at:
 * http://joinup.ec.europa.eu/software/page/eupl/licence-eupl
 */
using System;
using System.Net;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>UNLOCK</c> request.</summary>
/// <remarks>The <c>UNLOCK</c> request is described in section 9.11 of RFC 4918.</remarks>
public class UnlockRequest : SimpleRequest
{
  /// <summary>Initializes a new <see cref="UnlockRequest"/> based on a new WebDAV request.</summary>
  public UnlockRequest(WebDAVContext context) : base(context)
  {
    // parse the Timeout header if specified
    string value = context.Request.Headers[HttpHeaders.LockToken];
    if(value != null)
    {
      int start, length;
      value.Trim(out start, out length);
      if(length >= 5 && value[start] == '<' && value[start+length-1] == '>')
      {
        value = value.Substring(start+1, length-2);
        Uri uri;
        if(Uri.TryCreate(value, UriKind.Absolute, out uri)) LockToken = value;
      }
    }

    if(LockToken == null) throw Exceptions.BadRequest("Expected a valid Lock-Token header.");
  }

  /// <summary>Gets the lock token that the client has requested to unlock.</summary>
  public string LockToken { get; private set; }

  /// <summary>Processes a standard <c>UNLOCK</c> request.</summary>
  /// <remarks>This method attempts to unlock the canonical resource URL if <see cref="WebDAVContext.RequestResource"/> is not null, and
  /// to unlock the <see cref="WebDAVContext.RequestPath"/> otherwise.
  /// </remarks>
  public void ProcessStandardRequest()
  {
    ConditionCode precondition = CheckPreconditions(null);
    if(precondition != null && precondition.StatusCode != (int)HttpStatusCode.NotModified)
    {
      Status = precondition;
      return;
    }

    ActiveLock lockObject = null;
    if(Context.LockManager != null)
    {
      string lockPath = Context.ServiceRoot + (Context.RequestResource == null ? Context.RequestPath
                                                                               : Context.RequestResource.CanonicalPath);
      lockObject = Context.LockManager.GetLock(LockToken, lockPath);
    }

    if(lockObject == null) Status = ConditionCodes.LockTokenMatchesRequestUri409;
    else if(precondition != null) Status = precondition;
    else Context.LockManager.RemoveLock(lockObject);
  }
}

} // namespace HiA.WebDAV.Server
