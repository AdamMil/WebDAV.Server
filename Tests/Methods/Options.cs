using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class OptionsTests : TestBase
  {
    [Test]
    public void Test()
    {
      for(int i=0; i<2; i++)
      {
        string davHeader = "+1, 3", lockVerb = "!LOCK";
        Setup(false, i != 0, i != 0);
        TestDAVSupport("", i == 0 ? null : davHeader, "!MKCOL", "!PUT", "!PROPFIND", "!LOCK");
        TestDAVSupport("dav/dir", davHeader, "PROPFIND", "MKCOL", "!PUT", lockVerb);
        TestDAVSupport("dav/dir/file", davHeader, "PROPFIND", "MKCOL", "!PUT", lockVerb);
        TestDAVSupport("dav/missing", davHeader, "PROPFIND", "MKCOL", "!PUT", lockVerb);
        TestDAVSupport("http", null, "!PROPFIND");
        TestDAVSupport("http/norm", null, "!PROPFIND");
        TestDAVSupport("http/missing", null, "!PROPFIND");
        TestDAVSupport("dav/http", null, "!PROPFIND");
        TestDAVSupport("dav/http/norm", null, "!PROPFIND");
        TestDAVSupport("dav/http/missing", null, "!PROPFIND");
      }

      Setup(true, true, true);
      TestDAVSupport("", "+1, 2, 3", "!MKCOL", "!PUT", "!PROPFIND", "!LOCK");
      TestDAVSupport("dav/dir", "+1, 2, 3", "!PUT", "LOCK");
      TestDAVSupport("dav/dir/file", "+1, 2, 3", "PUT", "LOCK");
      TestDAVSupport("dav/dir/missing", "+1, 2, 3", "PUT", "LOCK");

      // test other headers
      TestRequest("OPTIONS", "dav/dir/file", null, 204, new string[] { DAVHeaders.AcceptEncoding, "gzip, deflate", DAVHeaders.AcceptRanges, "bytes" });
    }

    void Setup(bool writable, bool enableLocking, bool serveRootOptions)
    {
      CreateWebServer(enableLocking ? typeof(MemoryLockManager) : null, null,
                      new Location("/dav/http", null, false),
                      new FileSystemLocation("/dav", writable) { ServeRootOptions = serveRootOptions });
      Server.CreateDirectory("dir");
      Server.CreateFile("dir/file", "Hello, world!");
      Server.CreateDirectory("dav/http");
      Server.CreateDirectory("http");
      Server.CreateFile("http/norm", "No DAV here!");
      Server.CreateFile("dav/http/norm", "No DAV here!");
    }

    void TestDAVSupport(string requestPath, string davHeader)
    {
      TestDAVSupport(requestPath, davHeader, (string[])null);
    }

    void TestDAVSupport(string requestPath, string davHeader, params string[] verbs)
    {
      TestRequest(DAVMethods.Options, requestPath, null, null, 0, new string[] { DAVHeaders.DAV, davHeader }, response =>
      {
        Assert.IsTrue((int)response.StatusCode == 200 || (int)response.StatusCode == 204);
        if(verbs != null)
        {
          foreach(string verb in verbs)
          {
            string value = response.Headers[DAVHeaders.Allow] ?? "";
            if(verb[0] == '!')
            {
              string noVerb = verb.Substring(1);
              Assert.IsFalse(value.Contains(noVerb), "Expected no verb " + noVerb);
            }
            else
            {
              Assert.IsTrue(value.Contains(verb), "Expected verb " + verb);
            }
          }
        }
      });
    }
  }
}
