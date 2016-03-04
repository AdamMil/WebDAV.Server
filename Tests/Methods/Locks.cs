using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using AdamMil.Tests;
using AdamMil.Utilities;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class LockTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      // use a file lock manager rather than a MemoryLockManager because ASP.NET resets the app domain if we delete a directory from the
      // web site. this has supposedly been corrected in .NET 4 (although some people report otherwise), but we're using .NET 3.5 for the
      // test site and anyway this lets us test the FileLockManager a bit
      TypeWithParameters lockManager = new TypeWithParameters(typeof(FileLockManager));
      lockManager["defaultTimeout"]     = "1000";
      lockManager["maximumLocks"]       = "10";
      lockManager["maximumLocksPerUrl"] = "5";
      lockManager["maximumTimeout"]     = "10000";
      lockManager["lockFile"]           = "{PhysicalPath}/locks";
      CreateWebServer(lockManager, typeof(MemoryPropertyStore), new FileSystemLocation(typeof(Helpers.TestFileSystemService), "/", null, true));

      fileContent = Encoding.UTF8.GetBytes("Lockity lock lock");
      Server.CreateDirectory("dir");
      Server.CreateDirectory("dir/subdir");
      Server.CreateFile("dir/file1", fileContent);
      Server.CreateFile("dir/file2", fileContent);
      Server.CreateFile("dir/subdir/file1", fileContent);
      Server.CreateFile("dir/subdir/file2", fileContent);
      Server.CreateFile("file", fileContent);
    }

    [Test]
    public void T01_SimpleExclusive()
    {
      const string CustomXml = "<custom xmlns=\"TEST:\">This is a <b>test</b>!</custom>";
      // test the simple case of an exclusive lock on an existing resource
      LockInfo info = Lock("file", ownerXml:CustomXml);
      info.Test(GetFullUrl("file"), Depth.Self, DAVNames.exclusive, 10000, CustomXml);
      QueryLocks("file", info);

      // we can't lock it again because it's already locked
      Lock("file", 423);
      // not even if we submit our lock token (can't exclusively lock the same root URI twice)
      Lock("file", 423, ifHeader: MakeIfHeader(info));

      // now make sure various operations fail (except DELETE, which we'll test later)
      const string LockError = "<error xmlns=\"DAV:\"><lock-token-submitted><href>/FILE</href></lock-token-submitted></error>";
      TestHelpers.AssertXmlEquals(LockError, RequestXml("PUT", "file", null, "Schlockity schlock schlock", 423));
      TestHelpers.AssertXmlEquals(LockError,
        RequestXml("PROPPATCH", "file", null, "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));

      // now make sure they succeed if we submit the lock token
      TestRequest("PUT", "file", MakeIfHeaders(info), Encoding.ASCII.GetBytes("Schlockity schlock schlock"), 204);
      Assert.AreEqual("Schlockity schlock schlock", DownloadString("file"));
      SetCustomProperties("file", MakeIfHeaders(info), "ice", "tea");
      TestCustomProperties("file", "ice", "tea");

      // now unlock it
      Unlock("dir", info, 409); // 409 Conflict if the request URI is not in the scope of the lock
      QueryLocks("file", 1);
      Unlock("file", info);
      QueryLocks("file", 0);
      Unlock("file", info, 409); // 409 Conflict if it's not currently locked

      // lock it again
      info = Lock("file", timeoutSecs:100);
      info.Test(GetFullUrl("file"), Depth.Self, DAVNames.exclusive, 100, null);

      // now refresh the lock
      info = RefreshLock("file", info);
      info.Test(GetFullUrl("file"), Depth.Self, DAVNames.exclusive, 100, null);
      info = RefreshLock("file", info, 1000);
      info.Test(GetFullUrl("file"), Depth.Self, DAVNames.exclusive, 1000, null);
      Assert.LessOrEqual(950, info.Timeout);

      // and delete the file
      TestHelpers.AssertXmlEquals(LockError, RequestXml("DELETE", "file", null, null, 423));
      TestRequest("DELETE", "file", MakeIfHeaders(info), null, 204);
      TestRequest("GET", "file", 404);

      // now recreate it by locking the unmapped URL, then unlock it and put new content
      info = Lock("file", 201);
      QueryLocks("file", info);
      Unlock("file", info); // unlock the newly created resource and make sure it doesn't disappear on UNLOCK
      TestRequest("PUT", "file", null, fileContent, 204);

      // test the ability to remove dangling locks
      Server.CreateFile("temp", "...");
      info = Lock("temp");
      File.Delete(Path.Combine(Server.DataDirectory, "temp")); // delete the file outside WebDAV so the resource disappears
      Unlock("temp", info); // make sure we can unlock the nonexistent resource

      // test that submitting a lock token to a tag outside the lock scope (but to an ancestor or descendant) doesn't work
      info = Lock("dir/file1");
      TestRequest("PUT", "dir/file1", MakeIfHeaders(info, "dir"), new byte[0], 412);
      Unlock("dir/file1", info);
      info = Lock("dir");
      TestRequest("DELETE", "dir", MakeIfHeaders(info, "dir/file1"), null, 412);
      Unlock("dir", info);

      // test that we can't create directories by locking them or create files under files
      Lock("newdir/", 403);
      Lock("file/missing", 403);
    }

    [Test]
    public void T02_SimpleShared()
    {
      QueryLocks("file", 0); // make sure there are no dangling locks from earlier tests

      // first take an exclusive lock and make sure that we can't later take out a shared lock
      LockInfo info = Lock("file");
      Lock("file", 423);
      Lock("file", 423, scope:"shared", ifHeader:MakeIfHeader(info));
      Unlock("file", info);

      // now take out five shared locks, which is the configured per-URL max
      LockInfo[] sharedLocks = new LockInfo[5];
      for(int i=0; i<sharedLocks.Length; i++) sharedLocks[i] = Lock("file", scope:"shared");
      QueryLocks("file", sharedLocks);
      Lock("file", 423); // now that we're at the limit, make sure an exclusive lock request still returns 423 Locked
      Lock("file", 503, scope: "shared"); // while a shared lock request returns 503 Service Unavailable

      // make sure various operations fail
      const string LockError = "<error xmlns=\"DAV:\"><lock-token-submitted><href>/FILE</href></lock-token-submitted></error>";
      TestHelpers.AssertXmlEquals(LockError, RequestXml("PUT", "file", null, "Schlockity schlock schlock", 423));
      TestHelpers.AssertXmlEquals(LockError, RequestXml("DELETE", "file", null, null, 423));
      TestHelpers.AssertXmlEquals(LockError,
        RequestXml("PROPPATCH", "file", null, "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));

      // now take out 5 more shared locks, in order to hit the global maximum
      LockInfo[] moreLocks = new LockInfo[5];
      for(int i=0; i<moreLocks.Length; i++) moreLocks[i] = Lock("dir/file1", scope:"shared");
      QueryLocks("dir", Depth.SelfAndDescendants, moreLocks); // test a recursive PROPFIND query
      Lock("dir/file2", 503); // test the limit

      // test that refreshing multiple locks is not allowed
      Lock("file", new string[] { DAVHeaders.If, "(" + MakeIfClause(sharedLocks[0]) + " " + MakeIfClause(sharedLocks[1]) + ")" }, null, 400);

      // unlock everything
      foreach(LockInfo i in sharedLocks) Unlock("file", i);
      foreach(LockInfo i in moreLocks) Unlock("dir/file1", i);
    }

    [Test]
    public void T03_Recursive()
    {
      QueryLocks("dir", 0, Depth.SelfAndDescendants); // make sure there are no dangling locks from earlier tests

      // test that Depth.SelfAndChildren gives an error
      Lock("dir", 400, depth:Depth.SelfAndChildren);

      // test recursive properties of non-recursive locks. a non-recursive lock on a directory still prevents the creation, deletion, and
      // movement/renaming of children, and protects the directory properties/body, but does not protect child properties or bodies
      LockInfo info = Lock("dir"); // take a non-recursive lock on the directory
      Lock("dir/missing", 207, expectedXml:MakeDependencyError("dir/missing", "dir"));
      TestRequest("MKCOL", "dir/missing", 423);
      TestRequest("PUT", "dir/missing", null, fileContent, 423);
      TestRequest("DELETE", "dir/file1", 423);
      TestRequest("MKCOL", "dir/subdir/missing", 201);
      TestRequest("DELETE", "dir/subdir/missing", 204);
      TestRequest("PUT", "dir/subdir/missing", null, fileContent, 201);
      TestRequest("DELETE", "dir/subdir/missing", 204);
      SetCustomProperties("dir/file1", "ice", "tea");
      TestCustomProperties("dir/file1", "ice", "tea");
      RemoveCustomProperties("dir/file1", "ice");
      Unlock("dir", info);
      // TODO: test copy/move/rename

      // take an exclusive, recursive lock on the 'dir' directory
      info = Lock("dir", depth:Depth.SelfAndDescendants);
      Lock("dir", 423); // make sure we can't take a non-recursive lock
      Lock("dir/", 423);
      Lock("dir/subdir", 207, expectedXml:MakeDependencyError("dir/subdir", "dir")); // make sure we can't take a lock on a child
      Lock("dir/subdir/", 207, expectedXml:MakeDependencyError("dir/subdir/", "dir"));
      Lock("dir/file1", 207, expectedXml:MakeDependencyError("dir/file1", "dir"));
      Lock("dir/subdir/file1", 207, expectedXml:MakeDependencyError("dir/subdir/file1", "dir")); // make sure we can't take a lock on a descendant

      // make sure various operations fail on descendants
      Func<string, string> makeLockError = path => "<error xmlns=\"DAV:\"><lock-token-submitted><href>/" + path + "</href></lock-token-submitted></error>";
      Lock("dir/subdir/missing", 207, scope:"shared", expectedXml:MakeDependencyError("dir/subdir/missing", "dir"));
      TestRequest("PUT", "dir/subdir/missing", null, fileContent, 423);
      TestRequest("DELETE", "dir/subdir/file1", 423);
      TestHelpers.AssertXmlEquals(makeLockError("DIR/SUBDIR/FILE1"),
        RequestXml("PROPPATCH", "dir/subdir/file1", null, "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));

      // make sure an anonymous principal can't take a lock on a descendant even if it submits the lock token
      Lock("dir/file1", 207, ifHeader:MakeIfHeader(info), expectedXml:MakeDependencyError("dir/file1", "dir"));
      Lock("dir/missing", 207, ifHeader:MakeIfHeader(info), expectedXml:MakeDependencyError("dir/missing", "dir"));

      // make sure operations on descendants succeed if we give the token
      TestRequest("PUT", "dir/subdir/missing", null, fileContent, 423);
      // test that the lock token header can be tagged with the URL of any resource affected by the lock
      TestRequest("PUT", "dir/subdir/missing", MakeIfHeaders(info, "dir/subdir"), fileContent, 201);
      TestRequest("DELETE", "dir/subdir/missing", MakeIfHeaders(info, "dir"), 204);
      TestRequest("PUT", "dir/missing", MakeIfHeaders(info, "dir/file1"), fileContent, 201); // create a new file using PUT
      TestRequest("DELETE", "dir/missing", MakeIfHeaders(info, "dir/subdir/file1"), 204);

      // and unlock the directory, but do so by unlocking a descendant
      Unlock("dir/subdir/file1", info);
      QueryLocks("dir", 0);

      // take a recursive lock on the 'subdir' directory
      info = Lock("dir/subdir", depth:Depth.SelfAndDescendants);
      Unlock("dir/", Lock("dir/")); // make sure we can lock and unlock the parent non-recursively
      Lock("dir/", 207, depth:Depth.SelfAndDescendants, expectedXml:MakeDependencyError("dir/", "dir/subdir")); // make sure we can't lock the parent recursively
      Lock("dir", 207, depth:Depth.SelfAndDescendants, ifHeader:MakeIfHeader(info, "dir/subdir"), expectedXml:MakeDependencyError("dir", "dir/subdir")); // even if we submit the lock token
      Unlock("dir/subdir", info);
      info = Lock("dir/subdir/file1"); // test the same thing two levels down
      Lock("dir", 207, depth:Depth.SelfAndDescendants, ifHeader:MakeIfHeader(info, "dir/subdir/file1"), expectedXml:MakeDependencyError("dir", "dir/subdir/file1"));
      Unlock("dir/subdir/file1", info);
    }

    [Test]
    public void T04_Principals()
    {
      QueryLocks("dir", 0, Depth.SelfAndDescendants); // make sure there are no dangling locks from earlier tests

      string[] user1 = new string[] { "UserId", "1" }, user2 = new string[] { "UserId", "2" }, admin = new string[] { "UserId", "admin" };
      // test that the same principal can't take two shared locks on a single resource
      LockInfo info = Lock("file", scope: "shared", extraHeaders:user1);
      Lock("file", 423, scope: "shared", extraHeaders:user1);
      Unlock("file", Lock("file", scope: "shared")); // but that an anonymous principal can take a lock
      Unlock("file", Lock("file", scope: "shared", extraHeaders:user2), extraHeaders:user2); // as can another principal
      Unlock("file", info, extraHeaders:user1);
      
      // test that two principals can't exclusively lock the same resource
      info = Lock("file", extraHeaders:user1);
      Lock("file", 423, extraHeaders:user2);
      Lock("file", 423);
      Unlock("file", info, extraHeaders:user1);

      // test that a single principal can take locks on multiple resources in the same tree. test going down...
      info = Lock("dir", depth:Depth.SelfAndDescendants, extraHeaders:user1);
      Unlock("dir/file1", Lock("dir/file1", extraHeaders:user1), extraHeaders:user1);
      Unlock("dir/subdir/file1", Lock("dir/subdir/file1", extraHeaders:user1), extraHeaders:user1);
      Lock("dir/subdir/file1", 207, expectedXml: MakeDependencyError("dir/subdir/file1", "dir")); // but an anonymous principal can't
      Lock("dir/subdir/file1", 207, expectedXml: MakeDependencyError("dir/subdir/file1", "dir"), extraHeaders:user2); // and neither can another principal
      Unlock("dir", info, extraHeaders:user1);

      // ... and going up
      info = Lock("dir/subdir/file1", extraHeaders:user1);
      Unlock("dir/subdir", Lock("dir/subdir", depth:Depth.SelfAndDescendants, extraHeaders:user1), extraHeaders:user1);
      Unlock("dir", Lock("dir", depth:Depth.SelfAndDescendants, extraHeaders:user1), extraHeaders:user1);
      Lock("dir", 207, Depth.SelfAndDescendants, expectedXml: MakeDependencyError("dir", "dir/subdir/file1")); // an anonymous principal can't
      Lock("dir", 207, Depth.SelfAndDescendants, expectedXml: MakeDependencyError("dir", "dir/subdir/file1"), extraHeaders:user2); // and neither can another principal
      Unlock("dir/subdir/file1", info, extraHeaders:user1);

      // test that an administrative user can delete locks made by a regular user
      info = Lock("file"); // lock it anonymously
      Unlock("file", info, 403, extraHeaders: user1); // a normal user can't unlock it
      Unlock("file", info, extraHeaders: admin); // but an admin still can
      info = Lock("file", extraHeaders: user1); // lock it with a named user
      Unlock("file", info, 403); // an anonymous user can't unlock it
      Unlock("file", info, 403, extraHeaders:user2); // nor can another user
      Unlock("file", info, extraHeaders:admin); // but an admin can
      QueryLocks("file", 0);

      // test that when a principal takes a lock, other principals (incl. anonymous and admins) can't use the token to make changes. (the
      // "exception" is when both principals are unknown, i.e. both are anonymous)
      const string LockError = "<error xmlns=\"DAV:\"><lock-token-submitted><href>/FILE</href></lock-token-submitted></error>";
      Func<LockInfo,string[],string[]> makeUserIfHeaders = (i,user) => new string[] { user[0], user[1], DAVHeaders.If, MakeIfHeader(info) };
      // lock it anonymously and try to change it using a named user
      info = Lock("file");
      TestHelpers.AssertXmlEquals(LockError, RequestXml("PUT", "file", makeUserIfHeaders(info, user2), "hoo boy", 423));
      TestHelpers.AssertXmlEquals(LockError, RequestXml("DELETE", "file", makeUserIfHeaders(info, user2), null, 423));
      TestHelpers.AssertXmlEquals(LockError,
        RequestXml("PROPPATCH", "file", makeUserIfHeaders(info, user2),
                   "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));
      Unlock("file", info);
      // lock it as a normal user and try to change it as an admin
      info = Lock("file", extraHeaders:user1);
      TestHelpers.AssertXmlEquals(LockError, RequestXml("PUT", "file", makeUserIfHeaders(info, admin), "hoo boy", 423));
      TestHelpers.AssertXmlEquals(LockError, RequestXml("DELETE", "file", makeUserIfHeaders(info, admin), null, 423));
      TestHelpers.AssertXmlEquals(LockError,
        RequestXml("PROPPATCH", "file", makeUserIfHeaders(info, user2),
                   "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));
      // also try to change it as an anonymous user
      TestHelpers.AssertXmlEquals(LockError, RequestXml("PUT", "file", MakeIfHeaders(info), "hoo boy", 423));
      TestHelpers.AssertXmlEquals(LockError, RequestXml("DELETE", "file", MakeIfHeaders(info), null, 423));
      TestHelpers.AssertXmlEquals(LockError,
        RequestXml("PROPPATCH", "file", MakeIfHeaders(info),
                   "<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\"><set><prop><T:ice>tea</T:ice></prop></set></propertyupdate>", 423));
      Unlock("file", info, extraHeaders:user1);
    }

    LockInfo RefreshLock(string requestPath, LockInfo info, int newTimeoutSecs=-1)
    {
      List<string> requestHeaders = new List<string>() { DAVHeaders.If, MakeIfHeader(info) };
      if(newTimeoutSecs != -1)
      {
        requestHeaders.Add(DAVHeaders.Timeout);
        requestHeaders.Add(newTimeoutSecs == 0 ? "Infinite" : "Second-" + newTimeoutSecs.ToStringInvariant());
      }
      return Lock(requestPath, requestHeaders.ToArray(), null, 200);
    }

    byte[] fileContent;

    static string MakeDependencyError(string path, string rootPath)
    {
      return "<multistatus xmlns=\"DAV:\"><response><href>/" + rootPath.ToUpper() + "</href><status>HTTP/1.1 423 Locked</status></response><response><href>/" +
             path + "</href><status>HTTP/1.1 424 Failed Dependency</status></response></multistatus>";
    }
  }
}
