using System.IO;
using System.Reflection;
using AdamMil.Tests;

namespace AdamMil.WebDAV.Server.Tests
{
  static class Globals
  {
    public static string BinaryDirectory
    {
      get
      {
        if(_binDirectory == null) _binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return _binDirectory;
      }
    }

    public static string WebFileDirectory
    {
      get
      {
        if(_webFileDirectory == null) _webFileDirectory = Path.Combine(BinaryDirectory, "WebFiles");
        return _webFileDirectory;
      }
    }

    public static void Main()
    {
      TestHarness.RunAll();
      System.Console.Write("Press Enter...");
      System.Console.ReadLine();
    }

    static string _binDirectory, _webFileDirectory;
  }
}