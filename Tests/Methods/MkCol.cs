using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class MkColTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      CreateWebServer(new FileSystemLocation("/", true));
      Server.CreateDirectory("exists");
      Server.CreateFile("test", new byte[0]);
    }

    [Test]
    public void Test()
    {
      EntityTag tag = DAVUtility.ComputeEntityTag(new System.IO.MemoryStream());
      TestRequest("MKCOL", "dir/subdir", 409); // if ancestors don't exist, it fails with 409 Conflict
      TestRequest("MKCOL", "exists", 405); // method not allowed on existing resources
      TestRequest("MKCOL", "test", 405);
      // test conditional requests using the If header
      TestRequest("MKCOL", "dir", new string[] { DAVHeaders.If, "</missing> ([" + tag.ToHeaderString() + "])" }, 412);
      TestRequest("GET", "dir", 404);
      TestRequest("MKCOL", "dir1", new string[] { DAVHeaders.If, "</missing> (Not [" + tag.ToHeaderString() + "])" }, 201);
      TestRequest("GET", "dir1", 200);
      TestRequest("MKCOL", "dir2", new string[] { DAVHeaders.If, "</test> ([" + tag.ToHeaderString() + "])" }, 201);
      TestRequest("GET", "dir2", 200);

      TestMkCol("dir");
      TestMkCol("dir/subdir");
    }

    void TestMkCol(string requestPath)
    {
      TestRequest("MKCOL", requestPath, 201);
      TestRequest("GET", requestPath, 200);
    }
  }
}
