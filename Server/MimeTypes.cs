/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2013 by Adam Milazzo.

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

namespace AdamMil.WebDAV.Server
{

/// <summary>Provides methods to get appropriate file extensions for MIME types and guess appropriate MIME types for file names.</summary>
public static class MimeTypes
{
	static MimeTypes()
	{
		extToMime = new Dictionary<string,string>();
		mimeToExt = new Dictionary<string,string>();

    WebDAVServerSection section = WebDAVServerSection.Get();
    MimeMapCollection mimeMap = section.MimeMap;
		foreach(MimeMapElement map in mimeMap)
    {
      AddMimeMap(map.MimeType, map.CanonicalMimeType, map.Extension, map.CanonicalExtension);
    }
    defaultExtension = mimeMap.GetDefaultExtension();
    defaultMimeType  = mimeMap.GetDefaultMimeType();
    if(defaultExtension != null) defaultExtension = "." + defaultExtension; // add the leading period now to match mimeToExt
    CanonicalizeExtensions();

    CompressionMapCollection compressionMap = section.CompressionMap;
    compression = new CompressionEntry[compressionMap.Count];
    int index = 0;
    foreach(CompressionMapElement map in compressionMap) compression[index++] = new CompressionEntry(map.MimePattern, map.Compress);
	}

	/// <summary>Returns an appropriate file extension for the given MIME type, including the initial period.
	/// If an appropriate extension is not known, null is returned.
	/// </summary>
	public static string GetFileExtension(string mimeType)
	{
		if(string.IsNullOrEmpty(mimeType)) throw new ArgumentException();
		mimeType = mimeType.ToLowerInvariant();
		string extension;
    if(!mimeToExt.TryGetValue(mimeType, out extension)) extension = defaultExtension;
		return extension;
	}

	/// <summary>Attempts to guess the MIME type for a file with the given name.
	/// If a MIME type could not be guessed, null will be returned.
	/// </summary>
	public static string GuessMimeType(string fileName)
	{
		if(string.IsNullOrEmpty(fileName)) throw new ArgumentException();
		fileName = Path.GetFileName(fileName).ToLowerInvariant();

		for(int dotIndex = fileName.Length-1; dotIndex >= 0; dotIndex--)
		{
			dotIndex = fileName.LastIndexOf('.', dotIndex);
			if(dotIndex == -1) break;

			string mimeType;
			if(extToMime.TryGetValue(fileName.Substring(dotIndex+1), out mimeType)) return mimeType;
		}

    return defaultMimeType;
	}

  /// <summary>Determines whether resources of the given MIME type should be compressed.</summary>
  public static bool ShouldCompress(string mimeType)
  {
    if(mimeType == null) mimeType = "";
    foreach(CompressionEntry entry in compression)
    {
      if(entry.IsMatch(mimeType)) return entry.Compress;
    }
    return false;
  }

  #region CompressionEntry
  sealed class CompressionEntry
  {
    public CompressionEntry(string mimePattern, bool compress)
    {
      if(string.IsNullOrEmpty(mimePattern)) throw new ArgumentException();
      this.mimePattern = mimePattern;
      this.Compress    = compress;
      
      wildcardIndex = mimePattern.IndexOf('*');
      if(wildcardIndex != -1)
      {
        if(mimePattern.IndexOf('*', wildcardIndex+1) == -1) // if it contains only one asterisk...
        {
          this.mimePattern = mimePattern.Remove(wildcardIndex, 1); // remove the asterisk
        }
        else // otherwise, it contains multiple asterisks, so build a Regex
        {
          StringBuilder sb = new StringBuilder();
          if(wildcardIndex != 0) sb.Append('^');
          int start = 0;
          while(wildcardIndex != -1)
          {
            sb.Append(Regex.Escape(mimePattern.Substring(start, wildcardIndex-start)));
            start = wildcardIndex+1;
            if(start > 1 && start < mimePattern.Length) sb.Append(".*");
            wildcardIndex = mimePattern.IndexOf('*', start);
          }
          if(start != mimePattern.Length) sb.Append(Regex.Escape(mimePattern.Substring(start))).Append('$');
          regex = new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
          this.mimePattern = null;
        }
      }
    }

    public bool IsMatch(string mimeType)
    {
      if(regex != null) return regex.IsMatch(mimeType);
      if(wildcardIndex == -1) return string.Equals(mimePattern, mimeType, StringComparison.OrdinalIgnoreCase);

      if(wildcardIndex == mimePattern.Length) // if the pattern is a prefix match (e.g. text/* or just *)...
      {
        return wildcardIndex == 0 || mimeType.StartsWith(mimePattern, StringComparison.OrdinalIgnoreCase);
      }
      else if(wildcardIndex == 0) // if the pattern is a suffix match (e.g. */xml)...
      {
        return mimeType.EndsWith(mimePattern, StringComparison.OrdinalIgnoreCase);
      }
      else // otherwise, the pattern is both a prefix and suffix match (e.g. application/*+xml)...
      {
        int suffixLength = mimePattern.Length - wildcardIndex;
        return mimeType.Length >= mimePattern.Length &&
               string.Compare(mimePattern, 0, mimeType, 0, wildcardIndex, StringComparison.OrdinalIgnoreCase) == 0 &&
               string.Compare(mimePattern, wildcardIndex, mimeType, mimeType.Length-suffixLength, suffixLength,
                              StringComparison.OrdinalIgnoreCase) == 0;
      }
    }

    public readonly bool Compress;

    readonly string mimePattern;
    readonly int wildcardIndex;
    readonly Regex regex;
  }
  #endregion

  static void AddMimeMap(string mimeType, bool canonicalMimeType, string extension, bool canonicalExtension)
	{
    if(mimeType == null || extension == null) throw new ArgumentNullException();
    mimeType  = mimeType.ToLowerInvariant();
    extension = extension.ToLowerInvariant();
		if(canonicalMimeType) extToMime[extension] = mimeType;
		if(canonicalExtension) mimeToExt[mimeType] = extension;
	}

	/// <summary>Finds mime types that have only one registered extension, and makes that extension the canonical one. Also,
	/// prepends a period to all extensions so it doesn't have to be done every time <see cref="GetFileExtension"/> is called.
	/// </summary>
	static void CanonicalizeExtensions()
	{
		// first see how many times each mime type is used
		Dictionary<string, string> mimeUsage = new Dictionary<string, string>();
		foreach(KeyValuePair<string,string> pair in extToMime)
		{
			// track mime types used once by saving the extension if there's one usage, or saving null if there's more than one
			string onlyExtension;
			if(!mimeUsage.TryGetValue(pair.Value, out onlyExtension)) mimeUsage[pair.Value] = pair.Key; // save the extension the first time
			else mimeUsage[pair.Value] = null; // otherwise, this is the not first time, so overwrite the extension with null
		}

		// then make mime types with a count of 1 canonical if no canonical mapping already exists
		foreach(KeyValuePair<string,string> pair in mimeUsage)
		{
			if(pair.Value != null && !mimeToExt.ContainsKey(pair.Key)) mimeToExt[pair.Key] = pair.Value;
		}

		// prepend periods to all of the extensions so we don't have to do it every time GetFileExtension() is called
		foreach(KeyValuePair<string, string> pair in new List<KeyValuePair<string, string>>(mimeToExt))
		{
			mimeToExt[pair.Key] = "." + pair.Value;
		}
	}

	static readonly Dictionary<string, string> mimeToExt, extToMime;
  static readonly CompressionEntry[] compression;
  static readonly string defaultExtension, defaultMimeType;
}

} // namespace AdamMil.WebDAV.Server
