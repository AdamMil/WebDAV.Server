using System;
using System.Xml;

namespace AdamMil.WebDAV.Server.Tests.Helpers
{
  public sealed class TestAuthorizationFilter : AuthorizationFilter
  {
    public override bool? CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
    {
      if(context.CurrentUserId == "admin2") return true;
      else return null;
    }

    public override bool ShouldDenyAccess(WebDAVContext context, IWebDAVService service, IWebDAVResource resource, XmlQualifiedName access,
                                          out ConditionCode response)
    {
      response = context.RequestPath.IndexOf("hidden", StringComparison.OrdinalIgnoreCase) >= 0 ? ConditionCodes.NotFound : null;
      return response != null || context.RequestPath.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
             context.RequestPath.IndexOf("readonly", StringComparison.OrdinalIgnoreCase) >= 0 && access == DAVNames.write;
    }

    public override bool GetCurrentUserId(WebDAVContext context, out string currentUserId)
    {
      string userId = context.Request.Headers["UserId2"];
      currentUserId = string.IsNullOrEmpty(userId) ? null : userId;
      return currentUserId != null;
    }
  }
}