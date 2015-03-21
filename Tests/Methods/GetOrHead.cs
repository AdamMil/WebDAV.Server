using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using AdamMil.IO;
using AdamMil.Tests;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  [TestFixture]
  public class GetOrHeadTests : TestBase
  {
    [TestFixtureSetUp]
    public void Setup()
    {
      CreateWebServer(new FileSystemLocation("/"));

      Random rand = new Random();
      smallFile = System.Text.Encoding.UTF8.GetBytes("Hello, world!");
      largeFile = new byte[10*1024];
      rand.NextBytes(largeFile);
      Server.CreateDirectory("dir");
      Server.CreateFile("small.txt", smallFile);
      Server.CreateFile("dir/large.tfb", largeFile);
      Server.CreateFile("unknown", smallFile);
      Server.CreateFile("foo.tfb", smallFile);
    }

    [Test]
    public void T01_Simple()
    {
      byte[] dirListing = Download("dir/");
      Assert.IsTrue(System.Text.Encoding.UTF8.GetString(dirListing).Contains("/dir/ contents"));

      TestSimpleGet("unknown", smallFile, false);
      TestSimpleGet("foo.tfb", smallFile, false);
      TestSimpleGet("small.txt", smallFile, false);
      TestSimpleGet("dir/", dirListing, true);
      TestSimpleGet("dir/large.tfb", largeFile, false);
      TestRequest("GET", "unknown/", 404); // can't get a file using a trailing slash
    }

    [Test]
    public void T02_Conditional()
    {
      const string requestPath = "small.txt";
      byte[] expectedBody = smallFile;

      DateTime modifyDate = default(DateTime);
      EntityTag etag = null;
      TestRequest("HEAD", requestPath, null, null, 200, null, response =>
      {
        modifyDate = DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]);
        etag       = new EntityTag(response.Headers[DAVHeaders.ETag]);
        Assert.AreEqual(DateTimeKind.Utc, modifyDate.Kind);
      });

      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfModifiedSince, modifyDate, false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfModifiedSince, modifyDate.AddDays(-1), true);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfModifiedSince, modifyDate.AddDays(1), false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfUnmodifiedSince, modifyDate, true);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfUnmodifiedSince, modifyDate.AddDays(-1), false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfUnmodifiedSince, modifyDate.AddDays(1), true);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfNoneMatch, etag, false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfNoneMatch, new EntityTag(etag.Tag, true), false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfNoneMatch, new EntityTag("foo", false), true);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfMatch, etag, true);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfMatch, new EntityTag(etag.Tag, true), false);
      TestConditionalGet(requestPath, expectedBody, DAVHeaders.IfMatch, new EntityTag("foo", true), false);
    }

    [Test]
    public void T03_Partial()
    {
      const string requestPath = "dir/large.tfb";

      DateTime modifyDate = default(DateTime);
      EntityTag etag = null;
      TestRequest("HEAD", requestPath, null, null, 200, null, response =>
      {
        modifyDate = DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]);
        etag       = new EntityTag(response.Headers[DAVHeaders.ETag]);
      });

      TestRequest("HEAD", requestPath, new string[] { DAVHeaders.Range, "bytes=0-99" }, 206,
                  new string[] { DAVHeaders.ContentLength, "100", DAVHeaders.ContentRange, "bytes 0-99/"+largeFile.Length.ToStringInvariant() });
      TestRequest("HEAD", requestPath, new string[] { DAVHeaders.Range, "bytes=0-99,1000-1099", DAVHeaders.AcceptEncoding, "gzip" }, 206,
                  new string[] { DAVHeaders.ContentLength, null, DAVHeaders.ContentRange, null, DAVHeaders.ContentEncoding, "gzip" });
      TestRequest("GET", requestPath, new string[] { DAVHeaders.Range, "bytes=0-99", DAVHeaders.IfModifiedSince, DAVUtility.GetHttpDateHeader(modifyDate) }, 304,
                  new string[] { DAVHeaders.ContentLength, null, DAVHeaders.ContentRange, null });
      TestRequest("GET", requestPath, new string[] { DAVHeaders.Range, "bytes=0-99,1000-1099", DAVHeaders.IfModifiedSince, DAVUtility.GetHttpDateHeader(modifyDate) }, 304,
                  new string[] { DAVHeaders.ContentLength, null, DAVHeaders.ContentRange, null });

      TestPartialGet(requestPath, largeFile, null, new ByteRange(100, 300));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(-1, 100) }, new ByteRange(largeFile.Length-100, 100));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(1000, -1) }, new ByteRange(1000, largeFile.Length-1000));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(largeFile.Length-1000, 10000) }, new ByteRange(largeFile.Length-1000, 1000));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(largeFile.Length+1000, 10000) }, new ByteRange[0]);
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(largeFile.Length-1000, 10000), new ByteRange(largeFile.Length+1000, 10000) }, new ByteRange(largeFile.Length-1000, 1000));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(100, 300), new ByteRange(300, 200) }, new ByteRange(100, 400)); // combine overlapping ranges
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(100, 300), new ByteRange(300, 200), new ByteRange(450, 200) }, new ByteRange(100, 550));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(300, 200), new ByteRange(100, 300) }, new ByteRange(100, 400));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(100, 300), new ByteRange(450, 100) }, new ByteRange(100, 450)); // combine nearby ranges
      TestPartialGet(requestPath, largeFile, null, new ByteRange(100, 300), new ByteRange(1450, 100));
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(1450, 100), new ByteRange(100, 300) }, new[] { new ByteRange(100, 300), new ByteRange(1450, 100) });
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(1450, 100), new ByteRange(100, 300), new ByteRange(largeFile.Length+1000, 100) },
                     new[] { new ByteRange(100, 300), new ByteRange(1450, 100) });
      TestPartialGet(requestPath, largeFile, null, new[] { new ByteRange(0, 100), new ByteRange(50, 100), new ByteRange(largeFile.Length-150, 125), new ByteRange(largeFile.Length-50, -1) },
                     new[] { new ByteRange(0, 150), new ByteRange(largeFile.Length-150, 150) });

      TestPartialGet(requestPath, largeFile, etag, new ByteRange(100, 300));
      TestPartialGet(requestPath, largeFile, modifyDate, new ByteRange(100, 300));
      TestPartialGet(requestPath, largeFile, new EntityTag(etag.Tag, true), new[] { new ByteRange(100, 300) }, new ByteRange[0]);
      TestPartialGet(requestPath, largeFile, modifyDate.AddDays(-1), new[] { new ByteRange(100, 300) }, new ByteRange(0, largeFile.Length));
      TestPartialGet(requestPath, largeFile, modifyDate.AddDays(1), new[] { new ByteRange(100, 300) }, new ByteRange(0, largeFile.Length));
    }

    #region ByteRange
    struct ByteRange
    {
      public ByteRange(int start, int length)
      {
        Start  = start;
        Length = length;
      }

      public int End
      {
        get { return Start + Length; }
      }

      public override string ToString()
      {
        return Start.ToString() + " + " + Length.ToString();
      }

      public readonly int Start, Length;
    }
    #endregion

    void TestConditionalGet(string requestPath, byte[] expectedBody, string headerName, DateTime modifyDate, bool expectBody)
    {
      TestConditionalGet(requestPath, expectedBody, headerName, DAVUtility.GetHttpDateHeader(modifyDate), expectBody);
    }

    void TestConditionalGet(string requestPath, byte[] expectedBody, string headerName, EntityTag entityTag, bool expectBody)
    {
      TestConditionalGet(requestPath, expectedBody, headerName, entityTag.ToHeaderString(), expectBody);
    }

    void TestConditionalGet(string requestPath, byte[] expectedBody, string headerName, string headerValue, bool expectBody)
    {
      bool expect304 = headerName == DAVHeaders.IfModifiedSince || headerName == DAVHeaders.IfNoneMatch;
      for(int i=0; i<2; i++)
      {
        TestRequest(i == 0 ? "HEAD" : "GET", requestPath,
          new string[] { headerName, headerValue }, null, expectBody ? 200 : expect304 ? 304 : 412 ,
          !expectBody && !expect304 ? null :
            new string[] {
              DAVHeaders.ContentLength, expectBody ? expectedBody.Length.ToStringInvariant() : null,
              DAVHeaders.ContentType, GetContentType(requestPath)
            },
          response =>
          {
            if(i != 0 && expectBody)
            {
              using(Stream stream = response.GetResponseStream()) TestHelpers.AssertArrayEquals(stream.ReadToEnd(), expectedBody);
            }
          });
      }
    }

    void TestPartialGet(string requestPath, byte[] fullBody, object ifRange, params ByteRange[] byteRanges) // assumes ranges are valid, won't be merged, etc.
    {
      TestPartialGet(requestPath, fullBody, ifRange, byteRanges, byteRanges);
    }

    void TestPartialGet(string requestPath, byte[] fullBody, object ifRange, ByteRange[] requestRanges, params ByteRange[] responseRanges)
    {
      if(responseRanges == null) responseRanges = new ByteRange[1] { new ByteRange(0, fullBody.Length) };

      StringBuilder sb = new StringBuilder();
      sb.Append("bytes=");
      for(int i=0; i<requestRanges.Length; i++)
      {
        if(i != 0) sb.Append(',');
        if(requestRanges[i].Start >= 0) sb.Append(requestRanges[i].Start.ToStringInvariant());
        sb.Append('-');
        if(requestRanges[i].Length >= 0)
        {
          sb.Append((requestRanges[i].Start >= 0 ? requestRanges[i].End-1 : requestRanges[i].Length).ToStringInvariant());
        }
      }

      List<string> requestHeaders = new List<string>();
      requestHeaders.Add(DAVHeaders.Range);
      requestHeaders.Add(sb.ToString());
      if(ifRange != null)
      {
        requestHeaders.Add(DAVHeaders.IfRange);
        requestHeaders.Add(ifRange is EntityTag ? ((EntityTag)ifRange).ToHeaderString() : DAVUtility.GetHttpDateHeader((DateTime)ifRange));
      }

      string contentType = responseRanges.Length == 0 ? "text/plain" : responseRanges.Length == 1 ? GetContentType(requestPath) : "multipart/byteranges";
      int expectedStatus = responseRanges.Length == 1 && responseRanges[0].Start == 0 && responseRanges[0].Length == fullBody.Length ? 200 :
                           ifRange is EntityTag && ((EntityTag)ifRange).IsWeak ? 400 :
                           responseRanges.Length == 0 ? 416 : 206;
      string contentRange = responseRanges.Length == 1 && expectedStatus != 200 ?
        "bytes " + responseRanges[0].Start.ToStringInvariant() + "-" + (responseRanges[0].End-1).ToStringInvariant() + "/" +
        fullBody.Length.ToStringInvariant() : null;
      TestRequest("GET", requestPath, requestHeaders.ToArray(), null, expectedStatus,
                  new string[] { DAVHeaders.ContentRange, contentRange, DAVHeaders.ContentType, contentType }, response =>
      {
        if(responseRanges.Length != 0)
        {
          using(Stream stream = response.GetResponseStream())
          {
            if(responseRanges.Length == 1)
            {
              TestHelpers.AssertArrayEquals(stream.ReadToEnd(), fullBody.Subarray(responseRanges[0].Start, responseRanges[0].Length));
            }
            else
            {
              using(MimeReader reader = new MimeReader(stream, response.Headers))
              {
                for(int i=0; i<responseRanges.Length; i++)
                {
                  MimeReader.Part part = reader.GetNextPart();
                  Assert.IsNotNull(part);
                  using(Stream partStream = part.GetContent())
                  {
                    TestHelpers.AssertArrayEquals(partStream.ReadToEnd(), fullBody.Subarray(responseRanges[i].Start, responseRanges[i].Length));
                  }
                }
                Assert.IsNull(reader.GetNextPart());
              }
            }
          }
        }
      });
    }

    void TestSimpleGet(string requestPath, byte[] expectedBody, bool isDynamic)
    {
      string contentType = GetContentType(requestPath);
      DateTime modifyDate = default(DateTime);
      EntityTag etag = null;

      for(int i=0; i<2; i++)
      {
        bool shouldCompress = i != 0 && ShouldCompress(requestPath);
        string[] requestHeaders = i == 0 ? null : new string[] { DAVHeaders.AcceptEncoding, "gzip" };
        string[] expectedHeaders = new string[]
        {
          DAVHeaders.AcceptRanges, "bytes",
          DAVHeaders.ContentEncoding, shouldCompress ? "gzip" : null,
          DAVHeaders.ContentType, "+" + contentType,
          DAVHeaders.ContentLength, shouldCompress ? null : expectedBody.Length.ToStringInvariant(),
        };

        TestRequest("HEAD", requestPath, requestHeaders, null, 200, expectedHeaders, response =>
        {
          if(!isDynamic) modifyDate = DAVUtility.ParseHttpDate(response.Headers[DAVHeaders.LastModified]);
          etag = new EntityTag(response.Headers[DAVHeaders.ETag]);
          using(Stream stream = response.GetResponseStream()) Assert.AreEqual(0, stream.ReadToEnd().Length);
        });

        TestRequest("GET", requestPath, requestHeaders, null, 200, expectedHeaders, response =>
        {
          DateTime modDate;
          EntityTag tag;
          if(!isDynamic)
          {
            Assert.IsTrue(DAVUtility.TryParseHttpDate(response.Headers[DAVHeaders.LastModified], out modDate));
            Assert.AreEqual(modifyDate, modDate);
          }
          Assert.IsTrue(EntityTag.TryParse(response.Headers[DAVHeaders.ETag], out tag));
          Assert.AreEqual(etag, tag);
          using(Stream stream = response.GetResponseStream())
          {
            if(!shouldCompress)
            {
              TestHelpers.AssertArrayEquals(stream.ReadToEnd(), expectedBody);
            }
            else
            {
              MemoryStream ms = new MemoryStream();
              using(Stream gzip = new GZipStream(stream, CompressionMode.Decompress, true)) gzip.CopyTo(ms);
              TestHelpers.AssertArrayEquals(ms.ToArray(), expectedBody);
            }
          }
        });
      }
    }

    static string GetContentType(string requestPath)
    {
      if(requestPath.EndsWith('/')) return "text/html";
      switch(Path.GetExtension(requestPath).Trim('.'))
      {
        case "pdf": return "application/pdf";
        case "tfb": return "text/foobar";
        case "txt": return "text/plain";
        default: return "application/octet-stream";
      }
    }

    static bool ShouldCompress(string requestPath)
    {
      return GetContentType(requestPath).StartsWith("text/");
    }

    byte[] smallFile, largeFile;
  }
}
