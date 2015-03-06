using System;
using System.Text;
using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace WebDAV.Server.Tests
{
  [TestFixture]
  public class DeleteTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      CreateWebServer(null, typeof(MemoryPropertyStore), new FileSystemLocation("/", true));

      byte[] file = Encoding.UTF8.GetBytes("Goodbye, cruel world!");
      Server.CreateDirectory("dir");
      Server.CreateDirectory("dir/subdir");
      Server.CreateFile("dir/file1", file);
      Server.CreateFile("dir/file2", file);
      Server.CreateFile("dir/subdir/file1", file);
      Server.CreateFile("dir/subdir/file2", file);
      Server.CreateFile("file.tfb", file);
    }

    [Test]
    public void Test()
    {
      DateTime modifyDate = default(DateTime);
      EntityTag etag = null;
      TestRequest("HEAD", "file.tfb", null, null, 200, null, response =>
      {
        Assert.IsTrue(DAVUtility.TryParseHttpDate(response.Headers[DAVHeaders.LastModified], out modifyDate));
        Assert.IsTrue(EntityTag.TryParse(response.Headers[DAVHeaders.ETag], out etag));
      });

      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfModifiedSince, DAVUtility.GetHttpDateHeader(modifyDate) }, 412);
      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfNoneMatch, etag.ToHeaderString() }, 412);
      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfMatch, etag.ToHeaderString() }, 204);
      TestRequest("GET", "file.tfb", 404);

      TestDelete("dir/file1");
      TestDelete("dir/subdir/file1");
      TestDelete("dir/");
      TestRequest("GET", "dir/file2", 404);
    }

    void TestDelete(string requestPath)
    {
      TestRequest("DELETE", requestPath, 204);
      TestRequest("GET", requestPath, 404);
    }
  }
}
