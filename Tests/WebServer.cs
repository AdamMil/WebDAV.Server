using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdamMil.Utilities;
using AdamMil.Web;
using AdamMil.WebDAV.Server;
using AdamMil.WebDAV.Server.Services;

namespace AdamMil.WebDAV.Server.Tests
{
  #region TypeWithParameters
  public class TypeWithParameters
  {
    public TypeWithParameters(Type type)
    {
      Type = type;
    }

    public string this[string paramName]
    {
      get { return Parameters[paramName]; }
      set { Parameters[paramName] = value; }
    }

    public static implicit operator TypeWithParameters(Type type)
    {
      return type == null ? null : new TypeWithParameters(type);
    }

    public readonly Dictionary<string, string> Parameters = new Dictionary<string, string>();
    public readonly Type Type;

    public override string ToString()
    {
      return ToString("element", null, null);
    }

    public string ToString(string elementName, string siteRoot, string dataRoot)
    {
      StringBuilder sb = new StringBuilder();
      sb.Append('<').Append(elementName);
      WriteAttributes(sb, siteRoot, dataRoot);
      if(HasChildren)
      {
        sb.AppendLine(">");
        WriteChildren(sb, siteRoot, dataRoot);
        sb.Append("</").Append(elementName).AppendLine(">");
      }
      else
      {
        sb.Append(" />");
      }
      return sb.ToString();
    }

    protected virtual bool HasChildren
    {
      get { return false; }
    }

    protected virtual void WriteAttributes(StringBuilder sb, string siteRoot, string dataRoot)
    {
      if(Type != null) WriteAttribute(sb, "type", Type.AssemblyQualifiedName);
      foreach(KeyValuePair<string, string> pair in Parameters)
      {
        WriteAttribute(sb, pair.Key, pair.Value.Replace("{PhysicalPath}", siteRoot).Replace("{DataPath}", dataRoot));
      }
    }

    protected virtual void WriteChildren(StringBuilder sb, string siteRoot, string dataRoot)
    {
      if(!HasChildren) throw new InvalidOperationException();
    }

    protected static void WriteAttribute(StringBuilder sb, string attributeName, string value)
    {
      if(value != null) sb.Append(' ').Append(attributeName).Append("=\"").Append(XmlUtility.XmlEncode(value)).Append('"');
    }

    protected static void WriteAttribute(StringBuilder sb, string attributeName, bool value)
    {
      sb.Append(' ').Append(attributeName).Append("=\"").Append(value ? "true" : "false").Append('"');
    }

    protected static void WriteAttribute(StringBuilder sb, string attributeName, bool? value)
    {
      if(value.HasValue) WriteAttribute(sb, attributeName, value.Value);
    }
  }
  #endregion

  #region Location
  public class Location : TypeWithParameters
  {
    public Location(string match, Type type) : this(match, type, true, null) { }

    public Location(string match, Type type, bool enabled) : this(match, type, enabled, null) { }

    public Location(string match, Type type, bool enabled, TypeWithParameters authFilterType) : base(type)
    {
      if(string.IsNullOrEmpty(match)) throw new ArgumentException();
      AuthFilterType = authFilterType;
      Match          = match;
      Enabled        = enabled;
    }

    public readonly TypeWithParameters AuthFilterType;
    public bool? CaseSensitive, ServeRootOptions;
    public readonly bool Enabled;
    public string ID;
    public readonly string Match;

    public override string ToString()
    {
      return ToString(null, null);
    }

    public string ToString(string siteRoot, string dataRoot)
    {
      return ToString("add", siteRoot, dataRoot);
    }

    protected override bool HasChildren
    {
      get { return AuthFilterType != null; }
    }

    protected override void WriteAttributes(StringBuilder sb, string siteRoot, string dataRoot)
    {
 	    base.WriteAttributes(sb, siteRoot, dataRoot);
      WriteAttribute(sb, "match", Match);
      WriteAttribute(sb, "enabled", Enabled);
      if(!string.IsNullOrEmpty(ID)) WriteAttribute(sb, "id", ID);
      WriteAttribute(sb, "caseSensitive", CaseSensitive);
      WriteAttribute(sb, "serveRootOptions", ServeRootOptions);
    }

    protected override void WriteChildren(StringBuilder sb, string siteRoot, string dataRoot)
    {
      base.WriteChildren(sb, siteRoot, dataRoot);
      sb.AppendLine("<authorization>").AppendLine(AuthFilterType.ToString("add", siteRoot, dataRoot)).AppendLine("</authorization>");
    }
  }
  #endregion

  #region FileSystemLocation
  public class FileSystemLocation : Location
  {
    public FileSystemLocation(string match) : this(match, null, false) { }
    public FileSystemLocation(string match, bool writable) : this(match, null, writable) { }
    public FileSystemLocation(string match, string rootDirectory) : this(match, rootDirectory, false) { }
    public FileSystemLocation(string match, string rootDirectory, bool writable)
      : this(typeof(FileSystemService), match, rootDirectory, writable, null) { }
    public FileSystemLocation(Type type, string match, string rootDirectory, bool writable)
      : this(type, match, rootDirectory, writable, null) { }
    public FileSystemLocation(Type type, string match, string rootDirectory, bool writable, TypeWithParameters authFilterType)
      : base(match, type, true, authFilterType)
    {
      AllowInfinitePropFind = true;
      RootDirectory = rootDirectory ?? "{DataPath}";
      Writable      = writable;
    }

    public string RootDirectory;
    public bool AllowInfinitePropFind, Writable;

    protected override void WriteAttributes(StringBuilder sb, string siteRoot, string dataRoot)
    {
      base.WriteAttributes(sb, siteRoot, dataRoot);
      WriteAttribute(sb, "fsRoot", RootDirectory.Replace("{PhysicalPath}", siteRoot).Replace("{DataPath}", dataRoot));
      WriteAttribute(sb, "writable", Writable);
      WriteAttribute(sb, "allowInfinitePropFind", AllowInfinitePropFind);
    }
  }
  #endregion

  #region WebServer
  public sealed class WebServer : IDisposable
  {
    public WebServer(string serverProgram, int port, string tempDir,
                     TypeWithParameters globalLockManager, TypeWithParameters globalPropertyStore, Location[] locations)
    {
      try
      {
        this.port = port;
        PrepareServerDirectory(tempDir, port, globalLockManager, globalPropertyStore, locations);
        string args = "/trace:e /systray:false \"/config:" + Path.Combine(TempDirectory, "applicationhost.config") + "\"";
        ProcessStartInfo psi = new ProcessStartInfo(serverProgram, args);
        psi.CreateNoWindow  = true;
        psi.UseShellExecute = false;
        psi.WindowStyle     = ProcessWindowStyle.Hidden;
        process = Process.Start(psi);
      }
      catch
      {
        Dispose();
        throw;
      }
    }

    ~WebServer() { Dispose(); }

    public string DataDirectory
    {
      get { return Path.Combine(TempDirectory, "data"); }
    }

    public string TempDirectory { get; private set; }

    public string WebDirectory
    {
      get { return Path.Combine(TempDirectory, "web"); }
    }

    public IPEndPoint EndPoint
    {
      get { return new IPEndPoint(IPAddress.Loopback, port); }
    }

    public void CreateDirectory(string name)
    {
      AssertStarted();
      Directory.CreateDirectory(Path.Combine(DataDirectory, name));
    }

    public byte[] CreateFile(string name, string textContent)
    {
      byte[] content = Encoding.UTF8.GetBytes(textContent);
      CreateFile(name, content);
      return content;
    }

    public void CreateFile(string name, byte[] content)
    {
      AssertStarted();
      File.WriteAllBytes(Path.Combine(DataDirectory, name), content);
    }

    public void DeleteDirectory(string name)
    {
      AssertStarted();
      string path = Path.Combine(DataDirectory, name);
      if(Directory.Exists(path)) Directory.Delete(path, true);
    }

    public void Dispose()
    {
      if(process != null)
      {
        try
        {
          if(!process.HasExited)
          {
            process.Kill();
            process.WaitForExit();
          }
        }
        catch { }
        process = null;
      }

      if(TempDirectory != null)
      {
        try { Directory.Delete(TempDirectory, true); }
        catch { }
        TempDirectory = null;
      }

      GC.SuppressFinalize(this);
    }

    void AssertStarted()
    {
      if(process == null || process.HasExited) throw new InvalidOperationException("The server is not running.");
    }

    void PrepareServerDirectory(string tempDir, int port, TypeWithParameters globalLockManager,
                                       TypeWithParameters globalPropertyStore, Location[] locations)
    {
      string webConfig = File.ReadAllText(Path.Combine(Globals.WebFileDirectory, "web.config"));
      string hostConfig = File.ReadAllText(Path.Combine(Globals.WebFileDirectory, "applicationhost.config"));
      TempDirectory = CreateTempDirectory(tempDir);
      Directory.CreateDirectory(DataDirectory);
      Directory.CreateDirectory(WebDirectory);

      webConfig = webConfig.Replace(
        "{GlobalLockManager}", globalLockManager == null ? "" : globalLockManager.ToString("davLockManager", WebDirectory, DataDirectory));
      webConfig = webConfig.Replace(
        "{GlobalPropertyStore}", globalPropertyStore == null ? "" : globalPropertyStore.ToString("propertyStore", WebDirectory, DataDirectory));
      StringBuilder sb = new StringBuilder();
      foreach(Location location in locations) sb.AppendLine(location.ToString(WebDirectory, DataDirectory));
      webConfig = webConfig.Replace("{Locations}", sb.ToString());
      webConfig = typeNameRe.Replace(webConfig, match => GetType(match.Groups[1].Value).AssemblyQualifiedName);

      hostConfig = hostConfig.Replace("{PhysicalPath}", WebDirectory).Replace("{DataPath}", DataDirectory)
                             .Replace("{Port}", port.ToStringInvariant());
      File.WriteAllText(Path.Combine(TempDirectory, "applicationhost.config"), hostConfig);
      File.WriteAllText(Path.Combine(WebDirectory, "web.config"), webConfig);

      string binDirectory = Path.Combine(WebDirectory, "bin");
      Directory.CreateDirectory(binDirectory);
      foreach(string pattern in new string[] { "*.dll", "*.pdb" })
      {
        foreach(string file in Directory.GetFiles(Globals.BinaryDirectory, pattern))
        {
          File.Copy(file, Path.Combine(binDirectory, Path.GetFileName(file)));
        }
      }

      TempDirectory = TempDirectory;
    }

    Process process;
    readonly int port;

    static string CreateTempDirectory(string baseDir)
    {
      const string FileChars = "abcdefghijklmnopqrstuvwxyz0123456789";
      if(string.IsNullOrEmpty(baseDir)) baseDir = Path.GetTempPath();

      Random rand = new Random();
      string prefix = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond).ToStringInvariant() + "_";
      char[] chars = new char[8];
      while(true)
      {
        for(int i=0; i<chars.Length; i++) chars[i] = FileChars[rand.Next(FileChars.Length)];
        string directory = Path.Combine(baseDir, prefix + new string(chars));
        if(!Directory.Exists(directory))
        {
          Directory.CreateDirectory(directory);
          return directory;
        }
      }
    }

    static Type GetType(string typeName)
    {
      Type type = typeof(WebDAVModule).Assembly.GetType(typeName, false);
      if(type == null) type = typeof(MediaTypes).Assembly.GetType(typeName, true);
      return type;
    }

    static readonly Regex typeNameRe = new Regex(@"{TypeName:([\w\.]+)}", RegexOptions.Compiled | RegexOptions.ECMAScript);
  }
  #endregion
}