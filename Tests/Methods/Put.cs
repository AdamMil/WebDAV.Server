using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AdamMil.Tests;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class PutTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      CreateWebServer(null, typeof(MemoryPropertyStore), new FileSystemLocation("/", true));

      Random rand = new Random();
      hello   = Encoding.UTF8.GetBytes("Hello, world!");
      goodbye = Encoding.UTF8.GetBytes("Goodbye, cruel world!");
      largeFile = new byte[10*1024];
      rand.NextBytes(largeFile);
      Server.CreateDirectory("dir");
      Server.CreateFile("dir/large.pdf", largeFile);
    }

    [Test]
    public void T01_Simple()
    {
      // create a new file
      TestPut("small.txt", null, hello, 201, hello);

      // set a dead property
      SetCustomProperties("small.txt", "foo", "bar");

      // overwrite it, using a compressed transfer
      MemoryStream compressedBody = new MemoryStream();
      using(GZipStream gzip = new GZipStream(compressedBody, CompressionMode.Compress, true)) gzip.Write(goodbye, 0, goodbye.Length);
      TestPut("small.txt", new string[] { DAVHeaders.ContentEncoding, "gzip" }, compressedBody.ToArray(), 204, goodbye);

      // check that the dead property is still there
      TestCustomProperties("small.txt", "foo", "bar");

      // test conditional requests
      TestPut("small.txt", new string[] { DAVHeaders.IfNoneMatch, DAVUtility.ComputeEntityTag(new MemoryStream(goodbye)).ToHeaderString() }, hello, 412, goodbye);
      TestPut("small.txt", new string[] { DAVHeaders.IfMatch, DAVUtility.ComputeEntityTag(new MemoryStream(goodbye)).ToHeaderString() }, hello, 204, hello);

      // test error cases.
      // PUT to a directory should fail
      TestRequest("PUT", "dir/", null, new byte[0], 405, null);
      // PUT to a nonexistent directory must fail
      TestRequest("PUT", "dir/sub/file", null, new byte[0], 409, null);
    }

    [Test]
    public void T02_Partial()
    {
      // now do a partial PUT of an existing resource.
      // first do a partial put that keeps the file the same size
      byte[] chunk = new byte[256];
      new Random().NextBytes(chunk);
      Array.Copy(chunk, 0, largeFile, 100, chunk.Length);
      TestPartialPut("dir/large.pdf", new ContentRange(100, chunk.Length), chunk, largeFile);

      // now increase the file size by appending a chunk to it
      int oldLength = largeFile.Length;
      largeFile = ArrayUtility.Concat(false, largeFile, chunk);
      TestPartialPut("dir/large.pdf", new ContentRange(oldLength, chunk.Length, oldLength), chunk, largeFile);

      // now increase the file size by inserting a chunk into the middle. unfortunately, there's no way to specify a zero-length range in
      // HTTP, so we have to include one byte from outside the chunk we're inserting
      oldLength = largeFile.Length;
      largeFile = ArrayUtility.Concat(largeFile.Segment(0, largeFile.Length/2), chunk.Segment(), largeFile.Segment(largeFile.Length/2));
      TestPartialPut("dir/large.pdf", new ContentRange(oldLength/2, 1, oldLength), largeFile.Subarray(oldLength/2, chunk.Length+1), largeFile);

      // now decrease the file size by removing a chunk from the middle
      oldLength = largeFile.Length;
      largeFile = ArrayUtility.Concat(largeFile.Segment(0, 1000), largeFile.Segment(largeFile.Length-1000));
      TestPartialPut("dir/large.pdf", new ContentRange(1000, oldLength-2000, oldLength), new byte[0], largeFile);

      // now partial put to a nonexistent file, creating a new resource
      TestPut("dir/new.pdf", new string[] { DAVHeaders.ContentRange, new ContentRange(0, chunk.Length, 0).ToHeaderString() }, chunk, 201, chunk);

      // test partial put within a nonexistent file, causing an error
      TestPut("dir/new2.pdf", new string[] { DAVHeaders.ContentRange, new ContentRange(1, chunk.Length).ToHeaderString() }, chunk, 416, null);
    }

    void TestPut(string requestPath, string[] requestHeaders, byte[] requestBody, int expectedStatus, byte[] expectedBody)
    {
      TestRequest("PUT", requestPath, requestHeaders, requestBody, expectedStatus,
                  new string[] { DAVHeaders.ETag, expectedBody == null || expectedStatus >= 300 ? null : "+"+DAVUtility.ComputeEntityTag(new MemoryStream(expectedBody)).ToHeaderString() });
      if(expectedBody != null) TestHelpers.AssertArrayEquals(Download(requestPath), expectedBody);
      else TestRequest("GET", requestPath, 404);
    }

    void TestPartialPut(string requestPath, ContentRange contentRange, byte[] requestBody, byte[] expectedBody)
    {
      TestPut(requestPath, new string[] { DAVHeaders.ContentRange, contentRange.ToHeaderString() }, requestBody, 204, expectedBody);
    }

    byte[] hello, goodbye, largeFile;
  }
}
