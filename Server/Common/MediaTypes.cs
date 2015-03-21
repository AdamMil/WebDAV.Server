/*
AdamMil.WebDAV.Server is a library providing a flexible, extensible, and fairly
standards-compliant WebDAV server for the .NET Framework.

http://www.adammil.net/
Written 2012-2015 by Adam Milazzo.

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
using AdamMil.WebDAV.Server.Configuration;

namespace AdamMil.WebDAV.Server
{

/// <summary>Provides methods to get media type properties and guess appropriate media types for file names.</summary>
public static class MediaTypes
{
  static MediaTypes()
  {
    mediaToExt = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    extToMedia = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    WebDAVServerSection section = WebDAVServerSection.Get() ?? new WebDAVServerSection();
    MediaMapCollection mediaTypeMap = section.MediaTypeMap;
    foreach(MediaMapElement map in mediaTypeMap)
    {
      AddMediaTypeMap(map.MediaType, map.CanonicalMediaType, map.Extension, map.CanonicalExtension);
    }
    defaultExtension = mediaTypeMap.GetDefaultExtension();
    defaultMediaType = mediaTypeMap.GetDefaultMediaType();
    if(defaultExtension != null) defaultExtension = "." + NormalizeCase(defaultExtension); // add the period to match mediaToExt
    if(defaultMediaType != null) defaultMediaType = NormalizeCase(defaultMediaType);
    CanonicalizeExtensions();

    CompressionMapCollection compressionMap = section.CompressionMap;
    compression = new CompressionEntry[compressionMap.Count];
    int index = 0;
    foreach(CompressionMapElement map in compressionMap) compression[index++] = new CompressionEntry(map.MediaTypePattern, map.Compress);
  }

  /// <summary>Returns an appropriate file extension for the given media type, including the initial period.
  /// If an appropriate extension is not known, null is returned.
  /// </summary>
  public static string GetFileExtension(string mediaType)
  {
    if(string.IsNullOrEmpty(mediaType)) throw new ArgumentException();
    string extension;
    if(!mediaToExt.TryGetValue(NormalizeCase(mediaType), out extension)) extension = defaultExtension;
    return extension;
  }

  /// <summary>Attempts to guess the media type for a file with the given name. If a media type could not be guessed, null will be
  /// returned.
  /// </summary>
  public static string GuessMediaType(string fileName)
  {
    if(string.IsNullOrEmpty(fileName)) throw new ArgumentException();
    fileName = NormalizeCase(Path.GetFileName(fileName));

    for(int dotIndex = fileName.Length-1; dotIndex >= 0; dotIndex--)
    {
      dotIndex = fileName.LastIndexOf('.', dotIndex);
      if(dotIndex == -1) break;

      string mediaType;
      if(extToMedia.TryGetValue(fileName.Substring(dotIndex+1), out mediaType)) return mediaType;
    }

    return defaultMediaType;
  }

  /// <summary>Determines whether resources of the given media type should be compressed.</summary>
  public static bool ShouldCompress(string mediaType)
  {
    if(mediaType == null) mediaType = "";
    foreach(CompressionEntry entry in compression)
    {
      if(entry.IsMatch(mediaType)) return entry.Compress;
    }
    return false;
  }

  #region CompressionEntry
  sealed class CompressionEntry
  {
    public CompressionEntry(string mediaTypePattern, bool compress)
    {
      if(string.IsNullOrEmpty(mediaTypePattern)) throw new ArgumentException();
      this.mediaTypePattern = mediaTypePattern;
      this.Compress = compress;
      
      wildcardIndex = mediaTypePattern.IndexOf('*');
      if(wildcardIndex != -1)
      {
        if(mediaTypePattern.IndexOf('*', wildcardIndex+1) == -1) // if it contains only one asterisk...
        {
          this.mediaTypePattern = mediaTypePattern.Remove(wildcardIndex, 1); // remove the asterisk
        }
        else // otherwise, it contains multiple asterisks, so build a Regex
        {
          StringBuilder sb = new StringBuilder();
          if(wildcardIndex != 0) sb.Append('^');
          int start = 0;
          while(wildcardIndex != -1)
          {
            sb.Append(Regex.Escape(mediaTypePattern.Substring(start, wildcardIndex-start)));
            start = wildcardIndex+1;
            if(start > 1 && start < mediaTypePattern.Length) sb.Append(".*");
            wildcardIndex = mediaTypePattern.IndexOf('*', start);
          }
          if(start != mediaTypePattern.Length) sb.Append(Regex.Escape(mediaTypePattern.Substring(start))).Append('$');
          regex = new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
          this.mediaTypePattern = null;
        }
      }
    }

    public bool IsMatch(string mediaType)
    {
      if(regex != null) return regex.IsMatch(mediaType);
      if(wildcardIndex == -1) return string.Equals(mediaTypePattern, mediaType, StringComparison.OrdinalIgnoreCase);

      if(wildcardIndex == mediaTypePattern.Length) // if the pattern is a prefix match (e.g. text/* or just *)...
      {
        return wildcardIndex == 0 || mediaType.StartsWith(mediaTypePattern, StringComparison.OrdinalIgnoreCase);
      }
      else if(wildcardIndex == 0) // if the pattern is a suffix match (e.g. */xml)...
      {
        return mediaType.EndsWith(mediaTypePattern, StringComparison.OrdinalIgnoreCase);
      }
      else // otherwise, the pattern is both a prefix and suffix match (e.g. application/*+xml)...
      {
        int suffixLength = mediaTypePattern.Length - wildcardIndex;
        return mediaType.Length >= mediaTypePattern.Length &&
               string.Compare(mediaTypePattern, 0, mediaType, 0, wildcardIndex, StringComparison.OrdinalIgnoreCase) == 0 &&
               string.Compare(mediaTypePattern, wildcardIndex, mediaType, mediaType.Length-suffixLength, suffixLength,
                              StringComparison.OrdinalIgnoreCase) == 0;
      }
    }

    public readonly bool Compress;

    readonly string mediaTypePattern;
    readonly int wildcardIndex;
    readonly Regex regex;
  }
  #endregion

  static void AddMediaTypeMap(string mediaType, bool canonicalMediaType, string extension, bool canonicalExtension)
  {
    if(mediaType == null || extension == null) throw new ArgumentNullException();
    if(canonicalMediaType) extToMedia[NormalizeCase(extension)] = mediaType;
    if(canonicalExtension) mediaToExt[NormalizeCase(mediaType)] = extension;
  }

  /// <summary>Finds media types that have only one registered extension, and makes that extension the canonical one. Also,
  /// prepends a period to all extensions so it doesn't have to be done every time <see cref="GetFileExtension"/> is called.
  /// </summary>
  static void CanonicalizeExtensions()
  {
    // first see how many times each media type is used. (mediaUsage should ignore case even if extToMedia/mediaToExt don't.)
    Dictionary<string, string> mediaUsage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach(KeyValuePair<string,string> pair in extToMedia)
    {
      // track media types used once by saving the extension if there's one usage, or saving null if there's more than one
      string onlyExtension;
      if(!mediaUsage.TryGetValue(pair.Value, out onlyExtension)) mediaUsage[pair.Value] = pair.Key; // save the extension the first time
      else mediaUsage[pair.Value] = null; // otherwise, this is the not first time, so overwrite the extension with null
    }

    // then make media types with a count of 1 canonical if no canonical mapping already exists
    foreach(KeyValuePair<string,string> pair in mediaUsage)
    {
      string mediaType = NormalizeCase(pair.Key);
      if(pair.Value != null && !mediaToExt.ContainsKey(mediaType)) mediaToExt[mediaType] = pair.Value;
    }

    // prepend periods to all of the extensions so we don't have to do it every time GetFileExtension() is called
    foreach(KeyValuePair<string, string> pair in new List<KeyValuePair<string, string>>(mediaToExt))
    {
      mediaToExt[pair.Key] = "." + pair.Value;
    }
  }

  static string NormalizeCase(string key)
  {
    return key; // we needn't change the case at all since we're using OrdinalIgnoreCase for mediaToExt and extToMedia
  }

  static readonly Dictionary<string, string> mediaToExt, extToMedia;
  static readonly CompressionEntry[] compression;
  static readonly string defaultExtension, defaultMediaType;
}

} // namespace AdamMil.WebDAV.Server
