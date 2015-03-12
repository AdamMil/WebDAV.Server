using System;
using System.Text;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
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
      SetCustomProperties("file.tfb", "foo", "file");
      SetCustomProperties("dir/", "foo", "dir");
      SetCustomProperties("dir/subdir/", "foo", "subdir");
      SetCustomProperties("dir/subdir/file1", "foo", "sfile1");
      SetCustomProperties("dir/subdir/file2", "foo", "sfile2");

      DateTime modifyDate = default(DateTime);
      EntityTag etag = null;
      TestRequest("HEAD", "file.tfb", null, null, 200, null, response =>
      {
        modifyDate = DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]);
        etag       = new EntityTag(response.Headers[DAVHeaders.ETag]);
      });
      
      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfModifiedSince, DAVUtility.GetHttpDateHeader(modifyDate) }, 412);
      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfNoneMatch, etag.ToHeaderString() }, 412);
      TestRequest("DELETE", "file.tfb", new string[] { DAVHeaders.IfMatch, etag.ToHeaderString() }, 204);
      TestRequest("GET", "file.tfb", 404);

      TestDelete("dir/file1");
      TestDelete("dir/subdir/file1");
      TestDelete("dir/");
      TestRequest("GET", "dir/file2", 404);

      // make sure the properties were deleted
      Server.CreateDirectory("dir");
      Server.CreateDirectory("dir/subdir");
      Server.CreateFile("file.tfb", new byte[0]);
      Server.CreateFile("dir/subdir/file1", new byte[0]);
      Server.CreateFile("dir/subdir/file2", new byte[0]);
      foreach(string path in new[] { "dir", "dir/subdir", "file.tfb", "dir/subdir/file1", "dir/subdir/file2" })
      {
        TestCustomProperties(path, "foo", null);
      }
    }

    void TestDelete(string requestPath)
    {
      TestRequest("DELETE", requestPath, 204);
      TestRequest("GET", requestPath, 404);
    }
  }
}
