using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using AdamMil.Utilities;
using NUnit.Framework;

namespace WebDAV.Server.Tests
{
  static class TestHarness
  {
    [MethodImpl(MethodImplOptions.NoInlining)] // prevent GetCallingAssembly from being moved outside RunAll
    public static void RunAll()
    {
      RunAll(Assembly.GetCallingAssembly());
    }

    public static void RunAll(Assembly assembly)
    {
      if(assembly == null) throw new ArgumentNullException();

      List<Type> types = new List<Type>();
      foreach(Type type in assembly.GetTypes())
      {
        if(!type.IsAbstract && type.GetCustomAttributes(typeof(TestFixtureAttribute), true).Length != 0 &&
           type.GetConstructor(Type.EmptyTypes) != null)
        {
          types.Add(type);
        }
      }

      types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
      Console.WriteLine("Running all tests in " + assembly.GetName().Name);
      bool success = true;
      foreach(Type type in types) success &= RunFixture(type);
      Console.WriteLine(success ? "ALL TESTS PASSED" : "ERRORS OCCURRED");
    }

    internal static bool RunFixture(Type fixtureType)
    {
      string fixtureName = fixtureType.FullName, assemblyName = fixtureType.Assembly.GetName().Name;
      if(fixtureName.Length > assemblyName.Length && fixtureName.StartsWith(assemblyName) && fixtureName[assemblyName.Length] == '.')
      {
        fixtureName = fixtureName.Substring(assemblyName.Length+1);
      }
      Console.WriteLine("* " + fixtureName);

      bool success = true;
      try
      {
        if(fixtureType == null) throw new ArgumentNullException();
        ConstructorInfo cons = fixtureType.GetConstructor(Type.EmptyTypes);
        if(fixtureType.IsAbstract || cons == null) throw new ArgumentException();
        MethodInfo[] methods = fixtureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Array.Sort(methods, (a, b) => string.CompareOrdinal(a.Name, b.Name));

        object fixture = null;
        try
        {

          fixture = cons.Invoke(null);
          RunMethods(fixture, methods, typeof(TestFixtureSetUpAttribute), false);
          success = RunMethods(fixture, methods, typeof(TestAttribute), true);
        }
        finally
        {
          if(fixture != null)
          {
            try { success &= RunMethods(fixture, methods, typeof(TestFixtureTearDownAttribute), true); }
            catch { success = false; }
          }
          Utility.Dispose(fixture);
        }

        Console.WriteLine("  FIXTURE " + (success ? "PASSED" : "FAILED"));
      }
      catch(Exception ex)
      {
        Console.WriteLine("  FAILED " + ex.GetType().Name + " - " + ex.ToString());
      }

      Console.WriteLine();
      return success;
    }

    static bool RunMethods(object fixture, MethodInfo[] methods, Type attributeType, bool ignoreExceptions)
    {
      bool success = true;
      foreach(MethodInfo method in methods)
      {
        if(method.GetCustomAttributes(attributeType, true).Length != 0 && method.GetParameters().Length == 0)
        {
          Console.Write("  o " + method.Name + "... ");
          try
          {
            method.Invoke(fixture, null);
            Console.WriteLine("PASSED");
          }
          catch(Exception ex)
          {
            success = false;
            Console.WriteLine("FAILED " + ex.GetType().Name + " - " + ex.ToString());
            if(!ignoreExceptions) throw;
          }
        }
      }
      return success;
    }
  }
}