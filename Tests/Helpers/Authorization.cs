﻿using System;
using AdamMil.WebDAV.Server;

namespace AdamMil.WebDAV.Server.Tests.Helpers
{
  public sealed class TestAuthorizationFilter : AuthorizationFilter
  {
    public override bool? CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
    {
      if(context.CurrentUserId == "admin2") return true;
      else return null;
    }

    public override bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, out bool denyExistence)
    {
      denyExistence = context.RequestPath.IndexOf("hidden", StringComparison.OrdinalIgnoreCase) >= 0;
      return denyExistence || context.RequestPath.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public override bool GetCurrentUserId(WebDAVContext context, out string currentUserId)
    {
      string userId = context.Request.Headers["UserId2"];
      currentUserId = string.IsNullOrEmpty(userId) ? null : userId;
      return currentUserId != null;
    }
  }
}