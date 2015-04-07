using System;
using AdamMil.WebDAV.Server.Services;
using AdamMil.WebDAV.Server.Tests.Helpers;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class AuthorizationTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      CreateWebServer(typeof(MemoryLockManager), typeof(MemoryPropertyStore),
                      new FileSystemLocation(typeof(FileSystemService), "/", null, true, typeof(TestAuthorizationFilter)));
      Server.CreateDirectory("dir");
      Server.CreateFile("file", "huh");
      Server.CreateFile("denied", "can't get me");
      Server.CreateFile("hidden", "can't see me");
      Server.CreateFile("readonly", "can't touch me");
    }

    [Test]
    public void Test()
    {
      // test that operations that access existing resources return the right status codes
      byte[] patchBody = System.Text.Encoding.UTF8.GetBytes("<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>");
      TestRequest("GET", "file", 200);
      TestRequest("GET", "readonly", 200);
      TestRequest("GET", "denied", 403);
      TestRequest("GET", "hidden", 404); // we should get 404 on methods that only access existing resources on hidden URLs
      TestRequest("PROPPATCH", "denied", null, patchBody, 403);
      TestRequest("PROPPATCH", "missing", null, patchBody, 404);
      TestRequest("PUT", "denied", null, new byte[1], 403);
      TestRequest("PUT", "hidden", null, new byte[1], 403); // we should get 403 on methods that can create resources on hidden URLs
      TestRequest("PUT", "readonly", null, new byte[1], 403);

      // the auth filter gets the user ID from UserId2 and allows admin2 to delete locks
      LockInfo info = Lock("file");
      Unlock("file", info, extraHeaders:new string[] { "UserId2", "admin2" });
      QueryLocks("file", 0);

      // test that operations on unmapped URIs are also blocked
      TestRequest("PUT", "dir/denied", null, new byte[1], 403);
      TestRequest("PUT", "dir/hidden", null, new byte[1], 403); // we should get 403 rather than 404 for methods that create resources
      TestRequest("MKCOL", "dir/denied", 403);
      TestRequest("MKCOL", "dir/hidden", 403); // we should get 403 rather than 404 for methods that create resources
      Lock("dir/denied", 403);
      Lock("dir/hidden", 403);
    }
  }
}
