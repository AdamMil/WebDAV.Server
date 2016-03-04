using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AdamMil.Tests;
using AdamMil.WebDAV.Server.Tests.Helpers;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class CopyOrMoveTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      // use a file-based managers rather than memory-based ones because ASP.NET resets the app domain if we delete a directory from the
      // web site. this has supposedly been corrected in .NET 4 (although some people report otherwise), but we're using .NET 3.5 for the
      // test site and anyway this lets us test the directory-based storage a bit
      TypeWithParameters lockManager = new TypeWithParameters(typeof(FileLockManager));
      lockManager["lockDir"] = "{DataPath}/locks";
      TypeWithParameters propertyStore = new TypeWithParameters(typeof(FilePropertyStore));
      propertyStore["propertyDir"] = "{DataPath}/props";

      CreateWebServer(lockManager, propertyStore, new FileSystemLocation("/fs1", "{DataPath}/loc1", true),
                      new FileSystemLocation("/fs2", "{DataPath}/loc2", true), new Location("/mem", typeof(TestMemoryService)),
                      new FileSystemLocation("/fsX", "{DataPath}/loc1", true)); // duplicate of /fs1

      Server.CreateDirectory("locks");
      Server.CreateDirectory("props");
    }

    [Test]
    public void T01_Copy()
    {
      SetupFiles();

      // test some error cases
      TestCopy("fs1/dir", "fs1/dir/foo", 403); // can't copy to a descendant
      TestCopy("fs1/dir/subdir", "fs1/dir", 403); // can't copy to an ancestor
      TestCopy("fs1/dir", "fsX/dir/foo", 403); // can't copy to a descendant even through a different path
      TestCopy("fs1/dir/subdir", "fsX/dir", 403); // can't copy to an ancestor even through a different path
      TestCopy("fs1/file", "fs1/dir/sub1/sub2/file", 409); // intermediate collections must exist
      TestCopy("fs1/dir2", "fs1/dir/sub1/sub2", 409); // intermediate collections must exist

      // as a special case, copying/moving to the same path is a no-op if Overwrite is true
      // not all services will allow this, however
      TestCopy("fs1/dir", "fs1/dir", 412);
      TestCopy("mem/file", "mem/file", 412);
      TestCopy("fsX/dir", "fs1/dir", 412); // even through a different path
      TestCopy("fs1/dir", "fs1/dir", overwrite:true);
      TestCopy("mem/file", "mem/file", overwrite:true);
      TestCopy("fsX/dir", "fs1/dir", overwrite:true); // even through a different path

      // test copying files
      EntityTag etag = null;
      TestRequest("HEAD", "mem/file", null, null, 200, null, response => { etag = new EntityTag(response.Headers[DAVHeaders.ETag]); });

      // test simple copying from a generic (memory) source to a filesystem destination
      TestCopy("mem/file", "fs1/memFile");
      TestHelpers.AssertArrayEquals(Download("fs1/memFile"), Download("mem/file"));
      TestCopy("mem/dir1/file1", "fs1/memFile", 412); // the file already exists now
      TestCopy("mem/dir1/file1", "fs1/memFile", overwrite:true); // overwrite it
      TestHelpers.AssertArrayEquals(Download("fs1/memFile"), Download("mem/dir1/file1"));
      TestRequest("DELETE", "fs1/memFile", 204); // delete it
      TestRequest("GET", "fs1/memFile", 404); // make sure it's gone

      // test a conditional copy based on etags
      TestCopy("mem/file", "fs1/memFile", 412, extraHeaders:new string[] { DAVHeaders.IfNoneMatch, etag.ToHeaderString() });
      TestCopy("mem/file", "fs1/memFile", extraHeaders:new string[] { DAVHeaders.IfMatch, etag.ToHeaderString() });

      // test copying from the filesystem to the memory service
      TestCopy("fs2/file", "mem/dir2/file2", overwrite:true);
      TestHelpers.AssertArrayEquals(Download("mem/dir2/file2"), Download("fs2/file"));

      // test the copying of dead properties
      SetCustomProperties("mem/file", "foo", "bar");
      TestCopy("mem/file", "fs1/memFile", overwrite:true);
      TestCustomProperties("fs1/memFile", "foo", "bar");
      RemoveCustomProperties("mem/file", "foo");
      RemoveCustomProperties("fs1/memFile", "foo");
      TestRequest("DELETE", "fs1/memFile", 204);

      // test copying between filesystems, including dead properties
      TestCopy("fs1/file", "fs2/dir2/file");
      TestHelpers.AssertArrayEquals(Download("fs2/dir2/file"), Download("fs1/file"));
      TestCopy("fs1/file", "fs2/dir2/file", 412);
      SetCustomProperties("fs1/file", "foo", "bar");
      TestCopy("fs1/file", "fs2/dir2/file", overwrite:true);
      TestCustomProperties("fs2/dir2/file", "foo", "bar");
      RemoveCustomProperties("fs1/file", "foo");
      RemoveCustomProperties("fs2/dir2/file", "foo");

      // test copying using an absolute URI for the destination
      TestCopy("fs1/file", null, overwrite: true, extraHeaders: new string[] { DAVHeaders.Destination, GetFullUrl("fs2/dir2/file") });

      // test copying directories
      SetCustomProperties("mem/dir1/file1", "name", "file1");
      SetCustomProperties("mem/dir2/file2", "name", "file2");
      TestCopy("mem/", "fs1/tmp");
      foreach(string path in new string[] { "dir1/file1", "dir2/file2", "dir2/file1", "dir2/file2", "file" })
      {
        TestHelpers.AssertArrayEquals(Download("fs1/tmp/" + path), Download("mem/" + path));
      }
      TestCustomProperties("fs1/tmp/dir1/file1", "name", "file1");
      TestCustomProperties("fs1/tmp/dir2/file2", "name", "file2");
      foreach(string path in new string[] { "mem/dir1/file1", "mem/dir1/file2", "fs1/tmp/dir1/file1", "fs1/tmp/dir2/file2" })
      {
        RemoveCustomProperties(path, "name");
      }

      // now overwrite the directory with another, going directly between filesystems
      SetCustomProperties("fs2/dir/subdir/file1", "name", "file1");
      SetCustomProperties("fs2/dir/subdir/file2", "name", "file2");
      TestCopy("fs2/", "fs1/tmp", 412);
      TestCopy("fs2/", "fs1/tmp", overwrite:true);
      foreach(string path in new string[] { "dir/file1", "dir/file2", "dir/subdir/file1", "dir/subdir/file2", "file" })
      {
        TestHelpers.AssertArrayEquals(Download("fs1/tmp/" + path), Download("fs2/" + path));
      }
      TestCustomProperties("fs1/tmp/dir/subdir/file1", "name", "file1");
      TestCustomProperties("fs1/tmp/dir/subdir/file2", "name", "file2");
      foreach(string path in new string[] { "fs2/dir/subdir/file1", "fs2/dir/subdir/file2", "fs1/tmp/dir/subdir/file1", "fs1/tmp/dir/subdir/file2" })
      {
        RemoveCustomProperties(path, "name");
      }
      TestRequest("GET", "fs1/tmp/dir1/file1", 404); // make sure the files weren't merged

      // now overwrite the directory with a file
      TestCopy("mem/file", "fs1/tmp", 412);
      TestCopy("mem/file", "fs1/tmp", overwrite:true);
      TestHelpers.AssertArrayEquals(Download("fs1/tmp"), Download("mem/file"));

      // now overwrite the file with a directory
      TestCopy("fsX/dir/subdir", "fs1/tmp", overwrite:true);
      TestHelpers.AssertArrayEquals(Download("fs1/tmp/file1"), Download("fsX/dir/subdir/file1"));

      // now overwrite the directory with another directory, but non-recursively
      TestCopy("fsX/dir/subdir", "fs1/tmp", overwrite:true, extraHeaders:new string[] { DAVHeaders.Depth, GetDepthHeader(Depth.Self) });
      TestRequest("GET", "fs1/tmp", 200); // the directory should be copied
      TestRequest("GET", "fs1/tmp/file1", 404); // but not the children
    }

    [Test]
    public void T02_Move()
    {
      SetupFiles();

      // test some error cases
      TestMove("fs1/dir", "fs1/dir/foo", 403); // can't copy to a descendant
      TestMove("fs1/dir/subdir", "fs1/dir", 403); // can't copy to an ancestor
      TestMove("fs1/dir", "fsX/dir/foo", 403); // can't copy to a descendant even through a different path
      TestMove("fs1/dir/subdir", "fsX/dir", 403); // can't copy to an ancestor even through a different path
      TestMove("fs1/file", "fs1/dir/sub1/sub2/file", 409); // intermediate collections must exist
      TestMove("fs1/dir2", "fs1/dir/sub1/sub2", 409); // intermediate collections must exist
      TestMove("fs1/dir", "fs2/tmp", 403, extraHeaders:new string[] { DAVHeaders.Depth, GetDepthHeader(Depth.Self) }); // collection moves must be recursive

      // as a special case, copying/moving to the same path is a no-op, if Overwrite is true
      // not all services will allow this, however
      TestMove("fs1/dir", "fs1/dir", 412);
      TestMove("fsX/dir", "fs1/dir", 412); // even through a different path
      TestMove("fs1/dir", "fs1/dir", overwrite:true);
      TestMove("fsX/dir", "fs1/dir", overwrite:true); // even through a different path

      // test movement of files between filesystems, including both live and dead properties
      string srcPath = "fs1/file", destPath = "fs2/dir2/file";
      DateTime modifyDate = default(DateTime);
      TestRequest("HEAD", srcPath, null, null, 200, null, response =>
      {
        modifyDate = DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]);
      });
      byte[] body = Download(srcPath);
      SetCustomProperties(srcPath, "prop", "value");
      TestMove(srcPath, destPath);
      TestHelpers.AssertArrayEquals(Download(destPath), body); // test the file content
      TestRequest("GET", srcPath, 404);
      TestCustomProperties(destPath, "prop", "value"); // test dead properties
      TestRequest("HEAD", destPath, null, null, 200, null, response => // and live ones
      {
        Assert.AreEqual(modifyDate, DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]));
      });
      TestRequest("PUT", srcPath, null, body, 201); // recreate the original file
      TestCustomProperties(srcPath, "prop", null); // and test that the properties were moved and not just copied
      RemoveCustomProperties(destPath, "prop");
      TestRequest("DELETE", destPath, 204);

      // test movement of directories between filesystems
      body = Download("fs1/dir/file1");
      SetCustomProperties("fs1/dir/file1", "name", "file1");
      SetCustomProperties("fs1/dir/file2", "name", "file2");
      TestMove("fs1/", "fs2/dir", overwrite:true); // test moving the root of a filesystem service
      foreach(string path in new string[] { "dir/file1", "dir/file2", "dir/subdir/file1", "dir/subdir/file2", "file" })
      {
        TestHelpers.AssertArrayEquals(Download("fs2/dir/" + path), body);
        TestRequest("GET", "fs1/" + path, 404);
      }
      TestCustomProperties("fs2/dir/dir/file1", "name", "file1");
      TestCustomProperties("fs2/dir/dir/file2", "name", "file2");
      RemoveCustomProperties("fs2/dir/dir/file1", "name");
      RemoveCustomProperties("fs2/dir/dir/file2", "name");
      TestRequest("GET", "fs1/dir/subdir", 404);
      TestRequest("GET", "fs1/dir", 404);
      TestRequest("MKCOL", "fs1", 201);
      TestRequest("MKCOL", "fs1/dir", 201);
      TestRequest("PUT", "fs1/dir/file1", null, body, 201);
      TestCustomProperties("fs1/dir/file1", "name", null);
    }

    [Test]
    public void T03_Locks()
    {
      SetupFiles();

      Func<string, string> makeSimpleLockError =
        path => "<error xmlns=\"DAV:\"><lock-token-submitted><href>/" + path + "</href></lock-token-submitted></error>";

      // lock just the destination and try a copy
      LockInfo info = Lock("fs2/tmp", 201); // create a new file using a lock
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs2/TMP"), // it fails without a token submitted
        RequestXml("MOVE", "fs1/file", new string[] { DAVHeaders.Destination, "/fs2/tmp" }, null, 423));
      TestCopy("fs1/file", "fs2/tmp", 412, extraHeaders:MakeIfHeaders(info, "fs2/tmp")); // it fails with Overwrite: F
      TestCopy("fs1/file", "fs2/tmp", overwrite:true, extraHeaders:MakeIfHeaders(info, "fs2/tmp")); // use a tagged header, which should work
      QueryLocks("fs2/tmp", 0); // overwriting the file should have removed the lock
      info = Lock("fs2/tmp"); // lock it again and try an untagged If header
      TestCopy("fs1/file", "fs2/tmp", 412, overwrite:true, extraHeaders:MakeIfHeaders(info)); // it should fail because the If expression is false
      TestCopy("fs1/file", "fs2/tmp", overwrite:true, extraHeaders:new string[] { DAVHeaders.If, "(Not <urn:fake:123>) " + MakeIfHeader(info) }); // force it to true
      QueryLocks("fs2/tmp", 0); // overwriting the file should have removed the lock
      TestRequest("DELETE", "fs2/tmp", 204);

      // lock the source and try a copy, which should work without a token submitted
      info = Lock("fs1/file");
      TestCopy("fs1/file", "fs2/tmp");
      QueryLocks("fs1/file", info); // and the lock should still be there
      TestRequest("DELETE", "fs2/tmp", 204);
      // lock the destination too
      LockInfo destLock = Lock("fs2/tmp", 201); // create a new file and lock it
      // a copy should work if we use an untagged header containing both lock tokens
      TestCopy("fs1/file", "fs2/tmp", overwrite:true, extraHeaders:MakeIfHeaders(info, destLock));
      QueryLocks("fs2/tmp", 0); // once again the destination lock should be gone
      TestRequest("DELETE", "fs2/tmp", 204);
      Unlock("fs1/file", info); // and the source lock should still be there

      // now lock the source and try a move
      info = Lock("fs1/file");
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs1/FILE"), // if fails without the token
        RequestXml("MOVE", "fs1/file", new string[] { DAVHeaders.Destination, "/fs2/tmp" }, null, 423));
      TestMove("fs1/file", "fs2/tmp", extraHeaders:MakeIfHeaders(info)); // it works with the token
      QueryLocks("fs2/tmp", 0);
      TestMove("fs2/tmp", "fs1/file"); // move it back
      QueryLocks("fs1/file", 0);

      // take recursive, shared locks on directories and try some copies and moves
      info = Lock("fs1/dir", depth:Depth.SelfAndDescendants, scope:"shared");
      destLock = Lock("fs2/dir/subdir", depth:Depth.SelfAndDescendants, scope:"shared");
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs1/DIR"), // test that we get the right lock URLs
        RequestXml("MOVE", "fs1/dir/file1", new string[] { DAVHeaders.Destination, "/fs2/dir/tmp" }, null, 423));
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs2/DIR/SUBDIR"),
        RequestXml("COPY", "fs1/file", new string[] { DAVHeaders.Destination, "/fs2/dir/subdir/tmp" }, null, 423));
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs2/DIR/SUBDIR"),
        RequestXml("COPY", "fs1/file", new string[] { DAVHeaders.Destination, GetFullUrl("fs2/dir/subdir/tmp") }, null, 423));
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs2/DIR/SUBDIR"),
        RequestXml("COPY", "fs1/dir2", new string[] { DAVHeaders.Destination, "/fs2/dir" }, null, 423));
      TestHelpers.AssertXmlEquals(makeSimpleLockError("fs1/DIR"),
        RequestXml("MOVE", "fs1", new string[] { DAVHeaders.Destination, "/fs2/dir2" }, null, 423));
      TestMove("fs2/dir/subdir/file1", "fs1/dir/subdir/tmp", 423, extraHeaders:MakeIfHeaders(info, "fs1/dir")); // submitting just one token won't do
      TestMove("fs2/dir/subdir/file1", "fs1/dir/subdir/tmp", 423, extraHeaders:MakeIfHeaders(destLock));
      TestMove("fs2/dir/subdir/file1", "fs1/dir/subdir/tmp", extraHeaders:MakeIfHeaders(info, destLock));
      TestMove("fs1/dir/subdir/tmp", "fs2/dir/subdir/file1", 423); // we need a token to copy it back too
      TestMove("fs1/dir/subdir/tmp", "fs2/dir/subdir/file1", 423, extraHeaders:MakeIfHeaders(info));
      TestMove("fs1/dir/subdir/tmp", "fs2/dir/subdir/file1", 423, extraHeaders:MakeIfHeaders(destLock, "fs2/dir/subdir"));
      TestMove("fs1/dir/subdir/tmp", "fs2/dir/subdir/file1", extraHeaders:MakeIfHeaders(info, destLock));
      Unlock("fs1/dir", info);
      Unlock("fs2/dir/subdir", destLock);
    }

    void SetupFiles()
    {
      byte[] file = Encoding.UTF8.GetBytes("I'm a world traveler!");
      Server.DeleteDirectory("loc1");
      Server.CreateDirectory("loc1");
      Server.CreateDirectory("loc1/dir");
      Server.CreateDirectory("loc1/dir/subdir");
      Server.CreateFile("loc1/dir/file1", file);
      Server.CreateFile("loc1/dir/file2", file);
      Server.CreateFile("loc1/dir/subdir/file1", file);
      Server.CreateFile("loc1/dir/subdir/file2", file);
      Server.CreateDirectory("loc1/dir2");
      Server.CreateFile("loc1/file", file);

      System.Threading.Thread.Sleep(1500); // make sure the files get different timestamps
      file = Encoding.UTF8.GetBytes("I'm a traveler too, ya know.");
      Server.DeleteDirectory("loc2");
      Server.CreateDirectory("loc2");
      Server.CreateDirectory("loc2/dir");
      Server.CreateDirectory("loc2/dir/subdir");
      Server.CreateFile("loc2/dir/file1", file);
      Server.CreateFile("loc2/dir/file2", file);
      Server.CreateFile("loc2/dir/subdir/file1", file);
      Server.CreateFile("loc2/dir/subdir/file2", file);
      Server.CreateDirectory("loc2/dir2");
      Server.CreateFile("loc2/file", file);
      System.Threading.Thread.Sleep(1500); // make sure the files changed by the test itself also get different timestamps
    }

    void TestCopy(string srcPath, string destPath, int expectedStatus=0, bool overwrite=false, string[] extraHeaders=null, string expectedXml=null)
    {
      TestCopyOrMove("COPY", srcPath, destPath, expectedStatus, overwrite, extraHeaders, expectedXml);
    }

    void TestMove(string srcPath, string destPath, int expectedStatus=0, bool overwrite=false, string[] extraHeaders=null, string expectedXml=null)
    {
      TestCopyOrMove("MOVE", srcPath, destPath, expectedStatus, overwrite, extraHeaders, expectedXml);
    }

    void TestCopyOrMove(string method, string srcPath, string destPath, int expectedStatus=0, bool overwrite=false, string[] extraHeaders=null, string expectedXml=null)
    {
      List<string> requestHeaders = new List<string>();
      if(destPath != null)
      {
        requestHeaders.Add(DAVHeaders.Destination);
        requestHeaders.Add("/" + destPath);
      }
      if(!overwrite) // Overwrite is true by default
      {
        requestHeaders.Add(DAVHeaders.Overwrite);
        requestHeaders.Add(overwrite ? "T" : "F");
      }
      if(extraHeaders != null) requestHeaders.AddRange(extraHeaders);
      if(expectedStatus == 0) expectedStatus = overwrite ? 204 : 201;
      TestRequest(method, srcPath, requestHeaders.ToArray(), null, expectedStatus, null, response =>
      {
        if(expectedXml != null)
        {
          using(Stream stream = response.GetResponseStream()) TestHelpers.AssertXmlEquals(expectedXml, stream);
        }
      });
    }
  }
}
