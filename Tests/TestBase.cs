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

    protected string GetFullUrl(string requestPath)
    {
      return "http://localhost:" + Server.EndPoint.Port.ToStringInvariant() + "/" + requestPath;
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
      if((properties.Length & 1) != 0) throw new ArgumentException();
      StringBuilder sb = new StringBuilder();
      sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>")
        .AppendLine("<propertyupdate xmlns=\"DAV:\" xmlns:T=\"TEST:\">").AppendLine("<set><prop>");
      for(int i=0; i<properties.Length; i += 2)
      {
        sb.Append("<T:").Append(properties[i]).Append('>').Append(properties[i+1]).Append("</T:").Append(properties[i]).Append('>')
          .AppendLine();
      }
      sb.AppendLine("</prop></set>").AppendLine("</propertyupdate>");
      TestRequest("PROPPATCH", requestPath, null, Encoding.UTF8.GetBytes(sb.ToString()), 207);
    }

    protected void TestCustomProperties(string requestPath, params string[] properties)
    {
      TestCustomProperties(requestPath, false, properties);
    }

    protected void TestCustomProperties(string requestPath, bool allProps, params string[] properties)
    {
      if((properties.Length & 1) != 0) throw new ArgumentException();
      StringBuilder sb = new StringBuilder();
      sb.AppendLine("<propfind xmlns=\"DAV:\" xmlns:T=\"TEST:\">").AppendLine(allProps ? "<allprop/>" : "<prop>");
      int existingCount = 0;
      for(int i=0; i<properties.Length; i += 2)
      {
        if(!allProps) sb.Append("<T:").Append(properties[i]).AppendLine("/>");
        if(properties[i+1] != null) existingCount++;
      }
      if(!allProps) sb.AppendLine("</prop>");
      sb.AppendLine("</propfind>");

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
