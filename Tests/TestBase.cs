using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server;
using NUnit.Framework;

namespace WebDAV.Server.Tests
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

    protected void TestRequest(string method, string requestPath, params int[] expectedStatuses)
    {
      TestRequest(method, requestPath, null, null, expectedStatuses, (string[])null, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, int[] expectedStatuses,
                               params string[] expectedHeaders)
    {
      TestRequest(method, requestPath, requestHeaders, null, expectedStatuses, expectedHeaders, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, byte[] requestBody, int[] expectedStatuses,
                               params string[] expectedHeaders)
    {
      TestRequest(method, requestPath, requestHeaders, requestBody, expectedStatuses, expectedHeaders, null);
    }

    protected void TestRequest(string method, string requestPath, string[] requestHeaders, byte[] requestBody, int[] expectedStatuses, string[] expectedHeaders,
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
        if(requestBody != null) stream.Write(requestBody, 0, requestBody.Length);
        using(HttpWebResponse response = GetResponseWithoutException(request))
        {
          if(expectedStatuses != null && expectedStatuses.Length != 0 && !expectedStatuses.Contains((int)response.StatusCode))
          {
            Assert.Fail("Unexpected response status " + ((int)response.StatusCode).ToStringInvariant() + " (" +
                        response.StatusCode.ToString() + ")");
          }

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
