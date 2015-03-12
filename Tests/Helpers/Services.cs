using AdamMil.WebDAV.Server;
using AdamMil.WebDAV.Server.Configuration;

namespace AdamMil.WebDAV.Server.Tests.Helpers
{
  public sealed class TestFileSystemService : FileSystemService
  {
    public TestFileSystemService(ParameterCollection parameters) : base(parameters) { }

    public override bool CanDeleteLock(WebDAVContext context, ActiveLock lockObject)
    {
      return string.Equals(context.CurrentUserId, lockObject.OwnerId, System.StringComparison.Ordinal) || context.CurrentUserId == "admin";
    }

    public override string GetCurrentUserId(WebDAVContext context)
    {
      string userId = context.Request.Headers["UserId"];
      return string.IsNullOrEmpty(userId) ? null : userId;
    }
  }
}