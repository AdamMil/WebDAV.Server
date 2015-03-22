using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AdamMil.IO;
using AdamMil.Tests;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace AdamMil.WebDAV.Server.Tests
{
  public abstract class TestBase : IDisposable
  {
    ~TestBase() { Dispose(false); }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    #region LockInfo
    protected sealed class LockInfo
    {
      public LockInfo(TestBase test, XmlNode node)
      {
        Assert.AreEqual(DAVNames.activelock, node.GetQualifiedName());

        node = node.FirstChild;
        Assert.AreEqual(DAVNames.lockscope, node.GetQualifiedName());
        Assert.AreEqual(DAVNames.DAV, node.NamespaceURI);
        Scope = node.FirstChild.GetQualifiedName();

        node = node.NextSibling;
        Assert.AreEqual(DAVNames.locktype, node.GetQualifiedName());
        Assert.AreEqual(DAVNames.write, node.FirstChild.GetQualifiedName());

        node = node.NextSibling;
        Assert.AreEqual(DAVNames.depth, node.GetQualifiedName());
        string value = node.InnerText.Trim();
        if(value == "0") Depth = Depth.Self;
        else if(value == "1") Depth = Depth.SelfAndChildren;
        else if(value == "infinity") Depth = Depth.SelfAndDescendants;
        else Assert.Fail("Invalid depth " + value);

        node = node.NextSibling;
        if(node.GetQualifiedName() == DAVNames.owner)
        {
          OwnerData = (XmlElement)node.FirstChild;
          node = node.NextSibling;
        }

        Assert.AreEqual(DAVNames.timeout, node.GetQualifiedName());
        value = node.InnerText;
        if(value != "Infinite")
        {
          Assert.IsTrue(value.StartsWith("Second-"));
          Timeout = uint.Parse(value.Substring(7), CultureInfo.InvariantCulture);
        }
        node = node.NextSibling;

        Assert.AreEqual(DAVNames.locktoken, node.GetQualifiedName());
        Assert.AreEqual(DAVNames.href, node.FirstChild.GetQualifiedName());
        LockToken = new Uri(node.FirstChild.InnerText, UriKind.Absolute);
        node = node.NextSibling;

        Assert.AreEqual(DAVNames.lockroot, node.GetQualifiedName());
        Assert.AreEqual(DAVNames.href, node.FirstChild.GetQualifiedName());
        LockRoot = new Uri(node.FirstChild.InnerText, UriKind.RelativeOrAbsolute);
        if(!LockRoot.IsAbsoluteUri) LockRoot = new Uri(new Uri(test.GetFullUrl("/"), UriKind.Absolute), LockRoot);
        Assert.IsNull(node.NextSibling);
      }

      public Uri LockToken, LockRoot;
      public Depth Depth;
      public XmlQualifiedName Scope;
      public XmlElement OwnerData;
      public uint Timeout;

      public void Test(LockInfo expected)
      {
        Test(expected, false);
      }

      public void Test(LockInfo expected, bool exactTimeout)
      {
        Test(expected.LockRoot.ToString(), expected.Depth, expected.Scope, exactTimeout ? (int)expected.Timeout : -1,
             expected.OwnerData == null ? null : expected.OwnerData.OuterXml);
        if(!exactTimeout) Assert.LessOrEqual(Timeout, expected.Timeout);
      }

      public void Test(string lockRoot, Depth depth, XmlQualifiedName scope, int timeout, string ownerXml)
      {
        Assert.AreEqual(new Uri(lockRoot.ToLowerInvariant(), UriKind.Absolute), new Uri(LockRoot.ToString().ToLowerInvariant(), UriKind.Absolute));
        Assert.AreEqual(depth, Depth);
        Assert.AreEqual(scope, Scope);
        if(timeout != -1) Assert.AreEqual((uint)timeout, Timeout);
        if(ownerXml == null) Assert.IsNull(OwnerData);
        else TestHelpers.AssertXmlEquals(ownerXml, OwnerData);
      }
    }
    #endregion

    protected WebServer Server { get; private set; }

    protected virtual void Dispose(bool manualDispose)
    {
      Utility.Dispose(Server);
      Server = null;
    }

    protected void CreateWebServer(params Location[] locations)
    {
      CreateWebServer(null, null, locations);
    }

    protected void CreateWebServer(TypeWithParameters globalLockManager, TypeWithParameters globalPropertyStore,
                                   params Location[] locations)
    {
      Utility.Dispose(Server);
      Server = null;

      string serverProgram = ConfigurationManager.AppSettings["ServerProgram"] as string;
      if(string.IsNullOrEmpty(serverProgram)) serverProgram = @"C:\Program Files\IIS Express\iisexpress.exe";
      if(!File.Exists(serverProgram)) throw new ConfigurationErrorsException("Web server not found: " + serverProgram);

      string portString = ConfigurationManager.AppSettings["Port"] as string;
      if(string.IsNullOrEmpty(portString)) portString = "8080";
      int port;
      if(!InvariantCultureUtility.TryParse(portString, out port)) throw new ConfigurationErrorsException("Invalid port: " + portString);

      string tempDir = ConfigurationManager.AppSettings["TempDirectory"] as string;
      if(!string.IsNullOrEmpty(tempDir) && !Directory.Exists(tempDir))
      {
        throw new ConfigurationErrorsException("Temp directory doesn't exist: " + tempDir);
      }

      string killServer = ConfigurationManager.AppSettings["KillServer"] as string;
      if(!string.IsNullOrEmpty(killServer)) KillWebServer(killServer);

      Server = new WebServer(serverProgram, port, tempDir, globalLockManager, globalPropertyStore, locations);
    }

    protected byte[] Download(string requestPath)
    {
      byte[] data;
      HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GetFullUrl(requestPath));
      using(WebResponse response = request.GetResponse())
      using(Stream stream = response.GetResponseStream())
      {
        data = stream.ReadToEnd();
      }
      System.Threading.Thread.Sleep(50); // TODO: get rid of this crap (below, too)
      return data;
    }

    protected string DownloadString(string requestPath)
    {
      return Encoding.UTF8.GetString(Download(requestPath));
    }

    protected internal string GetFullUrl(string requestPath)
    {
      return "http://localhost:" + Server.EndPoint.Port.ToStringInvariant() + "/" + System.Web.HttpUtility.UrlPathEncode(requestPath);
    }

    protected XmlNodeList GetPropertyElements(string requestPath)
    {
      return GetPropertyElements(requestPath, null);
    }

    protected XmlNodeList GetPropertyElements(string requestPath, string requestXml)
    {
      XmlDocument doc = RequestXml("PROPFIND", requestPath, new string[] { DAVHeaders.Depth, "0" }, requestXml, 207);
      XmlNamespaceManager xmlns = new XmlNamespaceManager(doc.NameTable);
      xmlns.AddNamespace("D", "DAV:");
      return doc.SelectNodes("/D:multistatus/D:response/D:propstat/D:prop/node()", xmlns);
    }

    protected LockInfo Lock(string requestPath, int expectedStatus=200, Depth depth=Depth.Self, string scope="exclusive", uint timeoutSecs=0,
                            string ifHeader=null, string expectedXml=null, string ownerXml=null, string[] extraHeaders=null)
    {
      List<string> requestHeaders = new List<string>() { DAVHeaders.Timeout, timeoutSecs == 0 ? "Infinite" : "Second-" + timeoutSecs.ToStringInvariant() };
      if(depth != Depth.Unspecified)
      {
        requestHeaders.Add(DAVHeaders.Depth);
        requestHeaders.Add(GetDepthHeader(depth));
      }
      if(ifHeader != null) { requestHeaders.Add(DAVHeaders.If); requestHeaders.Add(ifHeader); }
      if(extraHeaders != null) requestHeaders.AddRange(extraHeaders);

      string bodyXml
        = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><lockinfo xmlns=\"DAV:\" xmlns:E=\"EXT:\"><E:foo/><lockscope><"+scope+"/></lockscope><E:bar/><locktype><write/></locktype><E:baz/>";
      if(ownerXml != null) bodyXml += "<owner>" + ownerXml + "</owner>";
      bodyXml += "</lockinfo>";

      LockInfo info = Lock(requestPath, requestHeaders.ToArray(), bodyXml, expectedStatus, expectedXml);
      if(info != null)
      {
        Assert.AreEqual(depth, info.Depth);
        Assert.AreEqual(scope, info.Scope.Name);
        if(ownerXml == null) Assert.IsNull(info.OwnerData);
        else TestHelpers.AssertXmlEquals(ownerXml, info.OwnerData);
      }
      return info;
    }

    protected LockInfo Lock(string requestPath, string[] requestHeaders, string bodyXml, int expectedStatus, string expectedXml=null)
    {
      LockInfo info = null;
      TestRequest("LOCK", requestPath, requestHeaders, bodyXml == null ? null : Encoding.UTF8.GetBytes(bodyXml), expectedStatus, null, response =>
      {
        string token = response.Headers[DAVHeaders.LockToken];
        if(expectedStatus < 200 || expectedStatus >= 300 || expectedStatus == 207)
        {
          Assert.IsNullOrEmpty(token);
          if(expectedXml != null)
          {
            using(Stream stream = response.GetResponseStream()) TestHelpers.AssertXmlEquals(expectedXml, stream);
          }
        }
        else
        {
          XmlDocument responseXml = new XmlDocument();
          using(Stream stream = response.GetResponseStream()) responseXml.Load(stream);
          if(expectedXml != null) TestHelpers.AssertXmlEquals(expectedXml, responseXml);

          Assert.AreEqual(DAVNames.prop, responseXml.DocumentElement.GetQualifiedName());
          Assert.AreEqual(DAVNames.lockdiscovery, responseXml.DocumentElement.FirstChild.GetQualifiedName());

          info = new LockInfo(this, responseXml.DocumentElement.FirstChild.FirstChild);
          if(bodyXml != null) // if the client submitted a body, it should have received a Lock-Token header
          {
            Assert.IsNotNullOrEmpty(token);
            Assert.AreEqual(info.LockToken, new Uri(token.TrimStart('<').TrimEnd('>'), UriKind.Absolute));
          }
        }
      });

      return info;
    }

    protected static string MakeIfClause(LockInfo info)
    {
      return "<" + info.LockToken.ToString() + ">";
    }

    protected static string MakeIfHeader(params LockInfo[] info)
    {
      StringBuilder sb = new StringBuilder();
      foreach(LockInfo i in info)
      {
        if(sb.Length != 0) sb.Append(' ');
        sb.Append("(<").Append(i.LockToken.ToString()).Append(">)");
      }
      return sb.ToString();
    }

    protected static string MakeIfHeader(LockInfo info, string tagPath)
    {
      return "</" + tagPath + "> (<" + info.LockToken.ToString() + ">)";
    }

    protected static string[] MakeIfHeaders(params LockInfo[] info)
    {
      return new string[] { DAVHeaders.If, MakeIfHeader(info) };
    }

    protected static string[] MakeIfHeaders(LockInfo info, string tagPath)
    {
      return new string[] { DAVHeaders.If, MakeIfHeader(info, tagPath) };
    }

    protected LockInfo[] QueryLocks(string requestPath, Depth depth)
    {
      XmlDocument xml = RequestXml("PROPFIND", requestPath, new string[] { DAVHeaders.Depth, GetDepthHeader(depth) },
                                   "<propfind xmlns=\"DAV:\" xmlns:E=\"EXT:\"><E:foo/><prop><lockdiscovery/></prop><E:bar/></propfind>", 207);
      XmlNamespaceManager xmlns = new XmlNamespaceManager(xml.NameTable);
      xmlns.AddNamespace("D", DAVNames.DAV);
      List<LockInfo> locks = new List<LockInfo>();
      foreach(XmlNode node in xml.SelectNodes("//D:prop/D:lockdiscovery/D:activelock", xmlns)) locks.Add(new LockInfo(this, node));
      return locks.ToArray();
    }

    protected LockInfo[] QueryLocks(string requestPath, int expectedLockCount)
    {
      return QueryLocks(requestPath, expectedLockCount, Depth.Self);
    }

    protected LockInfo[] QueryLocks(string requestPath, int expectedLockCount, Depth depth)
    {
      LockInfo[] info = QueryLocks(requestPath, depth);
      Assert.AreEqual(expectedLockCount, info.Length);
      return info;
    }

    protected LockInfo[] QueryLocks(string requestPath, params LockInfo[] expectedLocks)
    {
      return QueryLocks(requestPath, Depth.Self, expectedLocks);
    }

    protected LockInfo[] QueryLocks(string requestPath, Depth depth, params LockInfo[] expectedLocks)
    {
      LockInfo[] locks = QueryLocks(requestPath, expectedLocks.Length, depth);
      for(int i=0; i<expectedLocks.Length; i++)
      {
        // reorder the locks so they're in the same order as the expected locks
        for(int j=i; j<locks.Length; j++)
        {
          if(locks[j].LockToken == expectedLocks[i].LockToken)
          {
            if(i != j) Utility.Swap(ref locks[i], ref locks[j]);
            break;
          }
        }
        // then verify each lock
        locks[i].Test(expectedLocks[i]);
      }
      return locks;
    }

    protected void RemoveCustomProperties(string requestPath, params string[] properties)
    {
      RemoveCustomProperties(requestPath, null, properties);
    }

    protected void RemoveCustomProperties(string requestPath, string[] requestHeaders, params string[] properties)
    {
      StringBuilder sb = new StringBuilder();
      sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>")
        .AppendLine("<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\" xmlns:E=\"EXT:\">").AppendLine("<E:foo/><remove><E:bar/><prop>");
      foreach(string propertyName in properties)
      {
        sb.Append("<T:").Append(propertyName).Append("/>");
      }
      sb.AppendLine().AppendLine("</prop><E:baz/></remove><E:bugle/>").AppendLine("</propertyupdate>");
      TestRequest("PROPPATCH", requestPath, requestHeaders, Encoding.UTF8.GetBytes(sb.ToString()), 207);
    }

    protected XmlDocument RequestXml(string method, string requestPath)
    {
      return RequestXml(method, requestPath, null, null, 0);
    }

    protected XmlDocument RequestXml(string method, string requestPath, string[] requestHeaders)
    {
      return RequestXml(method, requestPath, requestHeaders, null, 0);
    }

    protected XmlDocument RequestXml(string method, string requestPath, string[] requestHeaders, string requestBody)
    {
      return RequestXml(method, requestPath, requestHeaders, requestBody, 0);
    }

    protected XmlDocument RequestXml(string method, string requestPath, string[] requestHeaders, string requestBody, int expectedStatus)
    {
      XmlDocument doc = null;
      byte[] body = requestBody == null ? null : Encoding.UTF8.GetBytes(requestBody);
      TestRequest(method, requestPath, requestHeaders, body, expectedStatus, (string[])null, response =>
      {
        using(Stream stream = response.GetResponseStream())
        {
          doc = new XmlDocument();
          doc.Load(stream);
        }
      });
      return doc;
    }

    protected void SetCustomProperties(string requestPath, params string[] properties)
    {
      SetCustomProperties(requestPath, null, properties);
    }

    protected void SetCustomProperties(string requestPath, string[] requestHeaders, params string[] properties)
    {
      if((properties.Length & 1) != 0) throw new ArgumentException();
      StringBuilder sb = new StringBuilder();
      sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>")
        .AppendLine("<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\" xmlns:E=\"EXT:\">").AppendLine("<E:foo/><set><E:bar/><prop>");
      for(int i=0; i<properties.Length; i += 2)
      {
        sb.Append("<T:").Append(properties[i]).Append('>').Append(properties[i+1]).Append("</T:").Append(properties[i]).Append('>')
          .AppendLine();
      }
      sb.AppendLine("</prop><E:baz/></set><E:bozo/>").AppendLine("</propertyupdate>");
      TestRequest("PROPPATCH", requestPath, requestHeaders, Encoding.UTF8.GetBytes(sb.ToString()), 207);
    }

    protected void TestCustomProperties(string requestPath, params string[] properties)
    {
      TestCustomProperties(requestPath, false, properties);
    }

    protected void TestCustomProperties(string requestPath, bool allProps, params string[] properties)
    {
      if((properties.Length & 1) != 0) throw new ArgumentException();
      StringBuilder sb = new StringBuilder();
      sb.AppendLine("<propfind xmlns=\"DAV:\" xmlns:T=\"TEST:\" xmlns:E=\"EXT:\"><E:foo/>").AppendLine(allProps ? "<allprop/>" : "<prop>");
      int existingCount = 0;
      for(int i=0; i<properties.Length; i += 2)
      {
        if(!allProps) sb.Append("<T:").Append(properties[i]).AppendLine("/>");
        if(properties[i+1] != null) existingCount++;
      }
      if(!allProps) sb.AppendLine("</prop>");
      sb.AppendLine("<E:bar/></propfind>");

      Dictionary<string, string> dict = new Dictionary<string, string>();
      foreach(XmlElement element in GetPropertyElements(requestPath, sb.ToString()))
      {
        if(element.NamespaceURI == "TEST:" && (allProps || !element.IsEmpty)) dict[element.LocalName] = element.InnerXml;
      }
      Assert.AreEqual(existingCount, dict.Count);
      for(int i=0; i<properties.Length; i += 2)
      {
        if(properties[i+1] != null) Assert.AreEqual(properties[i+1], dict[properties[i]]);
        else Assert.IsFalse(dict.ContainsKey(properties[i]));
      }
    }

    protected void TestRequest(string method, string requestPath, int expectedStatus)
    {
      TestRequest(method, requestPath, null, null, expectedStatus, (string[])null, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, int expectedStatus,
                               params string[] expectedHeaders)
    {
      TestRequest(method, requestPath, requestHeaders, null, expectedStatus, expectedHeaders, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, byte[] requestBody, int expectedStatus,
                               params string[] expectedHeaders)
    {
      TestRequest(method, requestPath, requestHeaders, requestBody, expectedStatus, expectedHeaders, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, byte[] requestBody, int expectedStatus, string[] expectedHeaders,
                               Action<HttpWebResponse> processor)
    {
      HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GetFullUrl(requestPath));
      request.Method = method;

      if(requestHeaders != null)
      {
        for(int i=0; i<requestHeaders.Length; i += 2)
        {
          string name = requestHeaders[i], value = requestHeaders[i+1];
          switch(name)
          {
            case DAVHeaders.IfModifiedSince: request.IfModifiedSince = DAVUtility.ParseHttpDate(value); break;
            case DAVHeaders.Range:
            {
              if(!value.StartsWith("bytes=")) throw new ArgumentException();
              // HACK: HttpWebRequest requires you to use the AddRange methods to set the Range header, but the methods don't allow all
              // valid Range headers, so we have to access a private method to do the job
              MethodInfo addRange = typeof(HttpWebRequest).GetMethod("AddRange", BindingFlags.Instance | BindingFlags.NonPublic, null,
                                                                     new Type[] { typeof(string), typeof(string), typeof(string) }, null);
              foreach(string chunk in value.Substring(6).Split(','))
              {
                int dash = chunk.IndexOf('-');
                if(dash == -1) throw new ArgumentException();
                addRange.Invoke(request, new object[] { "bytes", chunk.Substring(0, dash), chunk.Substring(dash+1) });
              }
              break;
            }
            default: request.Headers[name] = value; break;
          }
        }
      }

      using(Stream stream = requestBody == null ? null : request.GetRequestStream())
      {
        if(requestBody != null)
        {
          stream.Write(requestBody, 0, requestBody.Length);
          stream.Close();
        }
        using(HttpWebResponse response = GetResponseWithoutException(request))
        {
          if(expectedStatus != 0) Assert.AreEqual(expectedStatus, (int)response.StatusCode);

          if(expectedHeaders != null)
          {
            for(int i=0; i<expectedHeaders.Length; i += 2)
            {
              string name = expectedHeaders[i], value = response.Headers[name], expectedPattern = expectedHeaders[i+1];
              if(string.IsNullOrEmpty(expectedPattern) || expectedPattern[0] == '+')
              {
                if(!string.IsNullOrEmpty(expectedPattern)) expectedPattern = expectedPattern.Substring(1);
                Assert.AreEqual(expectedPattern, value, "Expected header " + name + " value \"" + value + "\" doesn't equal " +
                                (expectedPattern ?? "<NULL>"));
              }
              else if(!new Regex(expectedPattern).IsMatch(value ?? ""))
              {
                Assert.Fail("Header " + name + " value \"" + value + "\" doesn't match pattern " + expectedPattern);
              }
            }
          }

          if(processor != null) processor(response);
        }
      }

      System.Threading.Thread.Sleep(50);
    }

    protected void Unlock(string requestPath, LockInfo info, int expectedStatus=204, string expectedXml=null, string[] extraHeaders=null)
    {
      List<string> headers = new List<string>();
      headers.Add(DAVHeaders.LockToken);
      headers.Add("<" + info.LockToken.ToString() + ">");
      if(extraHeaders != null) headers.AddRange(extraHeaders);
      TestRequest("UNLOCK", requestPath, headers.ToArray(), null, expectedStatus, null, response =>
      {
        if(expectedXml != null)
        {
          using(Stream stream = response.GetResponseStream()) TestHelpers.AssertXmlEquals(expectedXml, stream);
        }
      });
    }

    /// <summary>Returns the WebDAV <c>Depth</c> header corresponding to the given <see cref="Depth"/> value, or null if
    /// <paramref name="depth"/> is <see cref="Depth.Unspecified"/>.
    /// </summary>
    protected static string GetDepthHeader(Depth depth)
    {
      switch(depth)
      {
        case Depth.Self: return "0";
        case Depth.SelfAndChildren: return "1";
        case Depth.SelfAndDescendants: return "infinity";
        case Depth.Unspecified: return null;
        default: throw new ArgumentException("Invalid depth value: " + depth.ToString());
      }
    }

    protected static HttpWebResponse GetResponseWithoutException(HttpWebRequest request)
    {
      try { return (HttpWebResponse)request.GetResponse(); }
      catch(WebException e) { return (HttpWebResponse)e.Response; }
    }

    protected static void KillWebServer(string processName)
    {
      try
      {
        foreach(Process process in Process.GetProcessesByName(processName))
        {
          try
          {
            if(!process.HasExited) process.Kill();
            process.WaitForExit();
          }
          catch { }
        }
      }
      catch { }
    }
  }
}
