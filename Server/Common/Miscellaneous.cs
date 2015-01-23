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
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.Collections;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

// TODO: add or find xml types for guid and other common types that aren't in xs:

namespace AdamMil.WebDAV.Server
{

#region ContentRange
/// <summary>Represents a value from the HTTP <c>Content-Range</c> header.</summary>
public sealed class ContentRange
{
  /// <summary>Initializes a new <see cref="ContentRange"/> that represents an entire entity body of any length.</summary>
  public ContentRange()
  {
    Start       = -1;
    Length      = -1;
    TotalLength = -1;
  }

  /// <summary>Initializes a new <see cref="ContentRange"/> from an HTTP <c>Content-Range</c> header value.</summary>
  public ContentRange(string headerValue)
  {
    long start, length, totalLength;
    if(!TryParse(headerValue, out start, out length, out totalLength)) throw new FormatException();
    Start       = start;
    Length      = length;
    TotalLength = totalLength;
  }

  /// <summary>Initializes a new <see cref="ContentRange"/> that represents an entire entity body of the given length.</summary>
  public ContentRange(long totalLength)
  {
    if(totalLength < 0) throw new ArgumentOutOfRangeException();
    Start       = -1;
    Length      = -1;
    TotalLength = totalLength;
  }

  /// <summary>Initializes a new <see cref="ContentRange"/> that represents the given range within an entity body.</summary>
  public ContentRange(long start, long length)
  {
    if(start < 0 || length < 0 || start + length < 0) throw new ArgumentOutOfRangeException();
    Start       = start;
    Length      = length;
    TotalLength = -1;
  }

  /// <summary>Initializes a new <see cref="ContentRange"/> that represents the given range within an entity body of the given length.</summary>
  public ContentRange(long start, long length, long totalLength)
  {
    long end = start + length;
    if(start < 0 || length < 0 || totalLength < 0 || end < 0 || end > totalLength) throw new ArgumentOutOfRangeException();
    Start       = start;
    Length      = length;
    TotalLength = totalLength;
  }

  /// <summary>Gets the start of the range within the entity body, or -1 if entire entity body is referenced.</summary>
  public long Start { get; private set; }

  /// <summary>Gets the length of the range within the entity body, or -1 if the entire entity body is referenced.</summary>
  public long Length { get; private set; }

  /// <summary>Gets the total length of the entity body, or -1 if the total length is unspecified.</summary>
  public long TotalLength { get; private set; }

  /// <summary>Returns a string suitable for an HTTP <c>Content-Range</c> value.</summary>
  public string ToHeaderString()
  {
    return "bytes " + (Start == -1 ? "*" : Start.ToStringInvariant() + "-" + (Start+Length-1).ToStringInvariant()) + "/" +
           (TotalLength == -1 ? "*" : TotalLength.ToStringInvariant());
  }

  /// <summary>Attempts to parse an HTTP <c>Content-Range</c> header value and return the <see cref="ContentRange"/> object representing
  /// it. If the value could not be parsed, null will be returned.
  /// </summary>
  public static ContentRange TryParse(string headerValue)
  {
    long start, length, totalLength;
    if(!TryParse(headerValue, out start, out length, out totalLength)) return null;

    return totalLength == -1 ? start == -1 ? new ContentRange() : new ContentRange(start, length)
                              : start == -1 ? new ContentRange(totalLength) : new ContentRange(start, length, totalLength);
  }

  static bool TryParse(string headerValue, out long start, out long length, out long totalLength)
  {
    start       = -1;
    length      = -1;
    totalLength = -1;

    Match m = rangeRe.Match(headerValue);
    if(!m.Success) return false;

    if(m.Groups["s"].Success)
    {
      long end;
      if(!InvariantCultureUtility.TryParseExact(m.Groups["s"].Value, out start) ||
         !InvariantCultureUtility.TryParseExact(m.Groups["e"].Value, out end)   || start > end)
      {
        return false;
      }
      length = end - start + 1;
    }

    if(m.Groups["L"].Success && !InvariantCultureUtility.TryParseExact(m.Groups["L"].Value, out totalLength)) return false;

    return true;
  }

  static readonly Regex rangeRe = new Regex(@"^\s*bytes (?:\*|(?<s>\d+)-(?<e>\d+))/(?:\*|(?<L>\d+))\s*$",
                                            RegexOptions.Compiled | RegexOptions.ECMAScript);
}
#endregion

#region DAVUtility
/// <summary>Contains useful utilities for DAV services.</summary>
public static class DAVUtility
{
  // TODO: is it correct to encode percent signs?
  /// <summary>Encodes a string into the canonical URL path form so that it can be used to construct URL paths.</summary>
  /// <remarks>This method only encodes the question mark (<c>?</c>), number sign (<c>#</c>), and percent sign (<c>%</c>), which is the
  /// minimal encoding required within a URL path.
  /// </remarks>
  public static string CanonicalPathEncode(string path)
  {
    if(path != null)
    {
      for(int i=0; i<path.Length; i++)
      {
        char c = path[i];
        if(c == '#' || c == '%' || c == '?')
        {
          StringBuilder sb = new StringBuilder(path.Length + 10);
          sb.Append(path, 0, i);
          while(true)
          {
            if(c == '#') sb.Append("%23");
            else if(c == '%') sb.Append("%25");
            else if(c == '?') sb.Append("%3f");
            else sb.Append(c);
            if(++i == path.Length) break;
            c = path[i];
          }
          path = sb.ToString();
          break;
        }
      }
    }

    return path;
  }

  /// <summary>Computes an entity tag by hashing the given entity body. The entity body stream is not rewound before or after computing
  /// the entity tag.
  /// </summary>
  public static EntityTag ComputeEntityTag(Stream entityBody)
  {
    return ComputeEntityTag(entityBody, false);
  }

  /// <summary>Computes an entity tag by hashing the given entity body.</summary>
  /// <param name="entityBody">The stream whose contents will be hashed to create an <see cref="EntityTag"/>.</param>
  /// <param name="rewindStream">If true, <paramref name="entityBody"/> will be rewound before hashing it.</param>
  public static EntityTag ComputeEntityTag(Stream entityBody, bool rewindStream)
  {
    if(entityBody == null) throw new ArgumentNullException();
    if(rewindStream) entityBody.Position = 0;
    return new EntityTag(Convert.ToBase64String(BinaryUtility.HashSHA1(entityBody)), false);
  }

  /// <summary>Gets the canonical message corresponding to an HTTP status code, or null if the message for the given status code is
  /// unknown.
  /// </summary>
  public static string GetStatusCodeMessage(int httpStatusCode)
  {
    return statusMessages.TryGetValue(httpStatusCode);
  }

  /// <summary>Determines whether the given name belongs to the WebDAV namespace and therefore indicates a name used or reserved by the
  /// WebDAV standard.
  /// </summary>
  public static bool IsDAVName(XmlQualifiedName name)
  {
    return name.Namespace.OrdinalEquals(DAVNames.DAV);
  }

  /// <summary>Returns a random MIME boundary.</summary>
  internal static string CreateMimeBoundary()
  {
    // technically, a MIME boundary must be guaranteed to not collide with any data in the message body, but that is unreasonably difficult
    // to ensure (i.e. MIME sucks!), so we'll use a random MIME boundary. MIME boundaries can be up to 69 characters in length, and we'll
    // use all 69 characters to reduce the chance of a collision with any data in the message body (although even 25 characters selected
    // randomly from the full boundary alphabet would provide a chance of collision on par with a random GUID, assuming a 100MB message).
    // we'll use a strong random number generator to provide us with random bytes (largely to avoid duplicate boundary strings returned by
    // poor time-based seed generation in the Random class if the method is called repeatedly in a short time period, and any theoretical
    // attacks based on predicting boundary strings - not that i can think of any). we care about reducing the number of random bytes
    // generated because we're using a cryptographic RNG and want to consume no more entropy from the system than is necessary (just in
    // case entropy is actually consumed by the RNG, as it is in some systems). since there are 74 characters in the MIME boundary
    // alphabet, each character requires log2(74) ~= 6.21 bits. each random byte provides us with 8 bits, so we need at least
    // ceil(log2(74) / 8 * 69) = 54 random bytes, but since 74 is not a power of two, to do it properly we would have to treat the random
    // bytes as a single 432-bit integer which we repeatedly divide by 74. this high-precision math is not difficult to do, but it seems
    // like computational overkill. it is more than enough to use an alphabet of 64 characters and 6 bits per character, given the long
    // length of the string we're generating
    const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_+"; // subset of legal MIME boundary characters
    char[] chars = new char[69]; // 69 characters in the boundary
    byte[] bytes = new byte[52]; // at 6 bits per character, we'll need ceil(69*6/8) = 52 random bytes
    System.Security.Cryptography.RandomNumberGenerator.Create().GetBytes(bytes);
    for(int bitBuffer=0, bits=0, bi=0, ci=0; ci<chars.Length; ci++)
    {
      if(bits < 6)
      {
        bitBuffer = (bitBuffer << 16) | (bytes[bi++] << 8) | bytes[bi++]; // read 2 bytes at a time, 'cuz we can
        bits     += 16;
      }
      chars[ci] = Alphabet[bitBuffer & 63];
      bitBuffer >>= 6;
      bits       -= 6;
    }
    return new string(chars);
  }

  /// <summary>Extracts an <see cref="XmlElement"/> into its own <see cref="XmlDocument"/>.</summary>
  internal static XmlElement Extract(this XmlElement element)
  {
    if(element == null) throw new ArgumentNullException();
    string xmlLang = element.GetInheritedAttributeValue(DAVNames.xmlLang);
    XmlDocument emptyDoc = new XmlDocument();
    element = (XmlElement)emptyDoc.ImportNode(element, true);
    // include the xml:lang attribute, which may not have been imported along with the node (due to xml:lang being inherited)
    if(!string.IsNullOrEmpty(xmlLang)) element.SetAttribute(DAVNames.xmlLang, xmlLang);
    emptyDoc.AppendChild(element);
    return element;
  }

  /// <summary>Converts the given date time to UTC and truncates it to one-second precision as necessary. This produces a
  /// <see cref="DateTime"/> value that can be compared with other <c>HTTP-date</c> values.
  /// </summary>
  internal static DateTime GetHttpDate(DateTime dateTime)
  {
    // HTTP dates are in UTC, so convert the last modified time to UTC as well. also, round the timestamp down to the nearest second
    // because HTTP dates only have one-second precision and DateTime.ToString("R") also truncates downward
    if(dateTime.Kind == DateTimeKind.Local) dateTime = dateTime.ToUniversalTime();
    long subsecondTicks = dateTime.Ticks % TimeSpan.TicksPerSecond;
    if(subsecondTicks != 0) dateTime = dateTime.AddTicks(-subsecondTicks);
    return dateTime;
  }

  /// <summary>Returns the parent of the given path, or null if the path has no parent. This works with both absolute and relative paths,
  /// and preserves the presence or absence of a trailing slash (although it will not remove a leading slash in any case).
  /// </summary>
  /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is null.</exception>
  internal static string GetParentPath(string path)
  {
    if(path == null) throw new ArgumentNullException();
    if(path.Length == 0 || path[0] == '/' && path.Length == 1) return null;
    int end = path.Length-1, slashOffset = path[end] == '/' ? 1 : 0;
    int lastSlash = path.LastIndexOf('/', end-slashOffset);
    return lastSlash == -1 ? "" : lastSlash == 0 ? "/" : path.Substring(0, lastSlash+slashOffset);
  }

  /// <summary>Gets the XML Schema type name (e.g. xsi:string) representing the type of the given value, or null if the type cannot be
  /// determined.
  /// </summary>
  internal static XmlQualifiedName GetXsiType(object value)
  {
    XmlQualifiedName type = null;
    if(value != null)
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Boolean: type = DAVNames.xsBoolean; break;
        case TypeCode.Byte: type = DAVNames.xsUByte; break;
        case TypeCode.Char: case TypeCode.String: type = DAVNames.xsString; break;
        case TypeCode.DateTime:
        {
          DateTime dateTime = (DateTime)value;
          type = dateTime.Kind == DateTimeKind.Unspecified && dateTime.TimeOfDay.Ticks == 0 ? DAVNames.xsDate : DAVNames.xsDateTime;
          break;
        }
        case TypeCode.Decimal: type = DAVNames.xsDecimal; break;
        case TypeCode.Double: type = DAVNames.xsDouble; break;
        case TypeCode.Int16: type = DAVNames.xsShort; break;
        case TypeCode.Int32: type = DAVNames.xsInt; break;
        case TypeCode.Int64: type = DAVNames.xsLong; break;
        case TypeCode.SByte: type = DAVNames.xsSByte; break;
        case TypeCode.Single: type = DAVNames.xsFloat; break;
        case TypeCode.UInt16: type = DAVNames.xsUShort; break;
        case TypeCode.UInt32: type = DAVNames.xsUInt; break;
        case TypeCode.UInt64: type = DAVNames.xsULong; break;
        case TypeCode.Object:
          if(value is DateTimeOffset) type = DAVNames.xsDateTime;
          else if(value is XmlDuration || value is TimeSpan) type = DAVNames.xsDuration;
          else if(value is byte[]) type = DAVNames.xsB64Binary;
          break;
      }
    }
    return type;
  }

  /// <summary>Encodes an ASCII string as an RFC 2616 <c>quoted-string</c> if it has any characters that need encoding.</summary>
  internal static string HeaderEncode(string ascii)
  {
    if(ascii != null)
    {
      for(int i=0; i<ascii.Length; i++)
      {
        char c = ascii[i];
        if(c < 32 && c != '\t' || c == 0x7f)
        {
          ascii = QuoteString(ascii);
          break;
        }
      }
    }

    return ascii;
  }
  internal static uint ParseConfigParameter(ParameterCollection parameters, string paramName, uint defaultValue)
  {
    return ParseConfigParameter(parameters, paramName, defaultValue, 0, 0);
  }

  internal static uint ParseConfigParameter(ParameterCollection parameters, string paramName, uint defaultValue, uint minValue, uint maxValue)
  {
    uint value = defaultValue;
    string str = parameters.TryGetValue(paramName);
    if(!string.IsNullOrEmpty(str))
    {
      if(!InvariantCultureUtility.TryParse(str, out value))
      {
        throw new ArgumentException("The " + paramName + " value \"" + str + "\" is not a valid integer or is out of range.");
      }
      else if(value < minValue || maxValue != 0 && value > maxValue)
      {
        throw new ArgumentException("The " + paramName + " value \"" + str + "\" is out of range. It must be at least " +
                                    minValue.ToStringInvariant() +
                                    (maxValue == 0 ? null : " and at most " + maxValue.ToStringInvariant()) + ".");
      }
    }
    return value;
  }

  internal static string[] ParseHttpTokenList(string headerString)
  {
    return headerString == null ? null : headerString.Split(',', s => s.Trim(), StringSplitOptions.RemoveEmptyEntries);
  }

  internal static object ParseXmlValue(string text, XmlQualifiedName type)
  {
    object value;
    if(string.IsNullOrEmpty(text)) value = null;
    else if(type == DAVNames.xsString) value = text;
    else if(type == DAVNames.xsDateTime || type == DAVNames.xsDate) value = XmlUtility.ParseDateTime(text);
    else if(type == DAVNames.xsInt) value = XmlConvert.ToInt32(text);
    else if(type == DAVNames.xsULong) value = XmlConvert.ToUInt64(text);
    else if(type == DAVNames.xsLong) value = XmlConvert.ToInt64(text);
    else if(type == DAVNames.xsBoolean) value = XmlConvert.ToBoolean(text);
    else if(type == DAVNames.xsUri) value = new Uri(text, UriKind.RelativeOrAbsolute);
    else if(type == DAVNames.xsDouble) value = XmlConvert.ToDouble(text);
    else if(type == DAVNames.xsFloat) value = XmlConvert.ToSingle(text);
    else if(type == DAVNames.xsDecimal) value = XmlConvert.ToDecimal(text);
    else if(type == DAVNames.xsUInt) value = XmlConvert.ToUInt32(text);
    else if(type == DAVNames.xsShort) value = XmlConvert.ToInt16(text);
    else if(type == DAVNames.xsUShort) value = XmlConvert.ToUInt16(text);
    else if(type == DAVNames.xsUByte) value = XmlConvert.ToByte(text);
    else if(type == DAVNames.xsSByte) value = XmlConvert.ToSByte(text);
    else if(type == DAVNames.xsDuration) value = XmlDuration.Parse(text);
    else if(type == DAVNames.xsB64Binary) value = Convert.FromBase64String(text);
    else if(type == DAVNames.xsHexBinary) value = BinaryUtility.ParseHex(text, true);
    else value = null;
    return value;
  }

  /// <summary>Quotes an ASCII string (which must not be null) in accordance with the <c>quoted-string</c> format defined in RFC 2616.</summary>
  internal static string QuoteString(string ascii)
  {
    if(ascii == null) throw new ArgumentNullException();
    StringBuilder sb = new StringBuilder(ascii.Length + 20);
    sb.Append('\"');
    for(int i=0; i<ascii.Length; i++)
    {
      char c = ascii[i];
      if(c < 32 && c != '\t' || c == '"' || c == '\\' || c == 0x7f) sb.Append('\\');
      sb.Append(c);
    }
    sb.Append('"');
    ascii = sb.ToString();
    return ascii;
  }

  internal static string RemoveTrailingSlash(string path)
  {
    if(path == null) throw new ArgumentNullException();
    return path.Length > 1 && path[path.Length-1] == '/' ? path.Substring(0, path.Length-1) : path;
  }

  /// <summary>Attempts to parse an <c>HTTP-date</c> value, as defined in RFC 2616 section 3.3.1.</summary>
  internal static bool TryParseHttpDate(string value, out DateTime date)
  {
    Match m = httpDateRe.Match(value);
    if(!m.Success)
    {
      date = default(DateTime);
      return false;
    }

    int year  = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
    int month = months.IndexOf(m.Groups["mon"].Value) + 1;
    int day   = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
    int hour  = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
    int min   = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
    int sec   = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);

    if(year < 100) // if we have a two-digit year...
    {
      int currentYear = DateTime.UtcNow.Year, century = currentYear - currentYear % 100;
      year += century; // start by assuming it's within the same century
      int difference = year - currentYear;
      if(difference > 50) year -= 100; // if that would put it more than 50 years in the future, then assume it's actually in the past
      else if(difference < -50) year += 100; // and if that would put it more than 50 years in the past, assume it's actually in the future
      // (to be really correct we should also take into account months, days, etc. when checking if it'd be more than 50 years away, but
      // that's not really worth the effort given that no HTTP/1.1-compliant system can generate a two-digit year anyway, and anybody using
      // ancient or non-compliant software should be prepared to handle varying interpretations of two-digit years when they're ambiguous)
    }

    date = new DateTime(year, month, day, hour, min, sec, DateTimeKind.Utc); // all HTTP dates are in UTC
    return true;
  }

  /// <summary>Tries to parse a value which may be either an absolute URI or an absolute path.</summary>
  internal static bool TryParseSimpleRef(string value, out Uri uri)
  {
    if(Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out uri))
    {
      if(uri.IsAbsoluteUri) return true;
      string path = uri.ToString();
      if(path.Length != 0 && path[0] == '/') return true;
    }
    return false;
  }

  /// <summary>Decodes an HTTP <c>quoted-string</c> given the span of text within the quotation marks.</summary>
  internal static string UnquoteDecode(string value, int start, int length)
  {
    if(value == null) throw new ArgumentNullException();
    for(int i=start, end=start+length; i<end; i++)
    {
      if(value[i] == '\\')
      {
        StringBuilder sb = new StringBuilder(length-1);
        sb.Append(value, start, i-start);
        do
        {
          char c = value[i++];
          if(c == '\\')
          {
            if(i == end) throw new FormatException();
            c = value[i++];
          }
          sb.Append(c);
        } while(i < end);
        return sb.ToString();
      }
    }

    return value.Substring(start, length);
  }

  internal static void ValidateAbsolutePath(string absolutePath)
  {
    if(absolutePath == null) throw new ArgumentNullException();
    if(absolutePath.Length == 0 || absolutePath[0] != '/') throw new ArgumentException("The given path is not absolute.");
  }

  internal static object ValidatePropertyValue(XmlQualifiedName propertyName, object value, XmlQualifiedName expectedType)
  {
    if(value == null || expectedType == null)
    {
      return value;
    }
    else if(expectedType == DAVNames.xsString)
    {
      if(!(value is string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
      return value;
    }
    if(expectedType == DAVNames.xsDateTime || expectedType == DAVNames.xsDate)
    {
      if(value is DateTime || value is DateTimeOffset) return value;
    }
    else if(expectedType == DAVNames.xsInt)
    {
      return (int)ValidateSignedInteger(propertyName, value, int.MinValue, int.MaxValue);
    }
    else if(expectedType == DAVNames.xsULong)
    {
      return ValidateUnsignedInteger(propertyName, value, ulong.MaxValue);
    }
    else if(expectedType == DAVNames.xsLong)
    {
      return ValidateSignedInteger(propertyName, value, long.MinValue, long.MaxValue);
    }
    else if(expectedType == DAVNames.xsBoolean)
    {
      if(value is bool) return value;
    }
    else if(expectedType == DAVNames.xsUri)
    {
      if(value is Uri) return value;
      Uri uri;
      if(value is string && Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) return uri;
    }
    else if(expectedType == DAVNames.xsDouble)
    {
      if(value is double || value is float || IsInteger(value)) return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
    else if(expectedType == DAVNames.xsFloat)
    {
      if(value is float || IsInteger(value)) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }
    else if(expectedType == DAVNames.xsDecimal)
    {
      if(value is double || value is float || value is decimal || IsInteger(value))
      {
        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
      }
    }
    else if(expectedType == DAVNames.xsUInt)
    {
      return (uint)ValidateUnsignedInteger(propertyName, value, uint.MaxValue);
    }
    else if(expectedType == DAVNames.xsShort)
    {
      return (short)ValidateSignedInteger(propertyName, value, short.MinValue, short.MaxValue);
    }
    else if(expectedType == DAVNames.xsUShort)
    {
      return (ushort)ValidateUnsignedInteger(propertyName, value, ushort.MaxValue);
    }
    else if(expectedType == DAVNames.xsUByte)
    {
      return (byte)ValidateUnsignedInteger(propertyName, value, byte.MaxValue);
    }
    else if(expectedType == DAVNames.xsSByte)
    {
      return (sbyte)ValidateSignedInteger(propertyName, value, sbyte.MinValue, sbyte.MaxValue);
    }
    else if(expectedType == DAVNames.xsDuration)
    {
      if(value is XmlDuration || value is TimeSpan) return value;
    }
    else if(expectedType == DAVNames.xsB64Binary || expectedType == DAVNames.xsHexBinary)
    {
      if(value is byte[]) return value;
    }
    else
    {
      return value; // we don't know how to validate it, so assume it's valid
    }

    throw new ArgumentException(propertyName.ToString() + " is expected to be of type " + expectedType.ToString() + " but was of type " +
                                value.GetType().FullName);
  }

  /// <summary>Ensures that the given path has a trailing slash if it's not an empty string. Empty strings will be returned as-is, to
  /// avoid converting relative paths to absolute paths.
  /// </summary>
  internal static string WithTrailingSlash(string path)
  {
    if(path == null) throw new ArgumentNullException();
    return path.Length == 0 || path[path.Length-1] == '/' ? path : path + "/";
  }

  /// <summary>Sets the response status code to the given status code and writes an message to the page. This method does not terminate
  /// the request.
  /// </summary>
  internal static void WriteStatusResponse(HttpRequest request, HttpResponse response, int httpStatusCode, string errorText)
  {
    // we could apply logic here to filter out potentially sensitive error messages (e.g. for 5xx errors), but we won't because we trust
    // the callers to check the configuration settings
    response.StatusCode        = httpStatusCode;
    response.StatusDescription = DAVUtility.GetStatusCodeMessage(httpStatusCode);
    // write a response body unless the status code disallows it
    if(CanIncludeBody(httpStatusCode))
    {
      response.ContentType = "text/plain";
      errorText = StringUtility.Combine(". ", response.StatusDescription, errorText);
      response.Write(string.Format(CultureInfo.InvariantCulture, "{0} {1}\n{2} {3}\n",
                                   request.HttpMethod, request.Url.AbsolutePath, httpStatusCode, errorText));
    }
  }

  /// <summary>Sets the response status code to the given status code and writes an error response based on the given
  /// <see cref="ConditionCode"/>. This method does not terminate the request.
  /// </summary>
  internal static void WriteStatusResponse(HttpRequest request, HttpResponse response, ConditionCode code)
  {
    if(code.ErrorElement == null) // if the condition code has no XML error data...
    {
      WriteStatusResponse(request, response, code.StatusCode, code.Message); // just write the error as text
    }
    else // otherwise, the condition code has some structured XML data that we can insert into the response
    {
      response.StatusCode        = code.StatusCode;
      response.StatusDescription = DAVUtility.GetStatusCodeMessage(code.StatusCode);
      if(CanIncludeBody(code.StatusCode))
      {
        response.ContentEncoding   = System.Text.Encoding.UTF8;
        response.ContentType       = "application/xml"; // media type specified by RFC 4918 section 8.2
        XmlWriterSettings settings = new XmlWriterSettings() { CloseOutput = false, Indent = true, IndentChars = "\t" };
        using(XmlWriter writer = XmlWriter.Create(response.OutputStream, settings)) code.WriteErrorXml(writer);
      }
    }
  }

  /// <summary>Determines whether the given HTTP status code allows entity bodies.</summary>
  static bool CanIncludeBody(int statusCode)
  {
    return statusCode != (int)HttpStatusCode.NoContent && statusCode != (int)HttpStatusCode.NotModified;
  }

  static bool IsInteger(object value)
  {
    switch(Type.GetTypeCode(value.GetType()))
    {
      case TypeCode.Byte: case TypeCode.Int16: case TypeCode.Int32: case TypeCode.Int64: case TypeCode.SByte: case TypeCode.UInt16:
      case TypeCode.UInt32: case TypeCode.UInt64:
        return true;
      case TypeCode.Decimal:
      {
        decimal d = (decimal)value;
        return d == decimal.Truncate(d);
      }
      case TypeCode.Double:
      {
        double d = (double)value;
        return d == Math.Truncate(d);
      }
      case TypeCode.Single:
      {
        float d = (float)value;
        return d == Math.Truncate(d);
      }
      default: return false;
    }
  }

  static long ValidateSignedInteger(XmlQualifiedName propertyName, object value, long min, long max)
  {
    long intValue;
    try
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Decimal:
        {
          decimal d = (decimal)value, trunc = decimal.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = decimal.ToInt64(trunc);
          break;
        }
        case TypeCode.Double:
        {
          double d = (double)value, trunc = Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((long)trunc);
          break;
        }
        case TypeCode.Int16: intValue = (short)value; break;
        case TypeCode.Int32: intValue = (int)value; break;
        case TypeCode.Int64: intValue = (long)value; break;
        case TypeCode.SByte: intValue = (sbyte)value; break;
        case TypeCode.Single:
        {
          float d =  (float)value, trunc = (float)Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((long)trunc);
          break;
        }
        case TypeCode.UInt16: intValue = (ushort)value; break;
        case TypeCode.UInt32: intValue = (uint)value; break;
        case TypeCode.UInt64:
        {
          ulong v = (ulong)value;
          if(v > (ulong)long.MaxValue) goto failed;
          else intValue = (long)v;
          break;
        }
        default: goto failed;
      }

      if(intValue >= min && intValue <= max) return intValue;
    }
    catch(OverflowException)
    {
    }

    failed:
    throw new ArgumentException(propertyName.ToString() + " was expected to be an integer between " + min.ToStringInvariant() +
                                " and " + max.ToStringInvariant() + " (inclusive), but was " + value.ToString());
  }

  static ulong ValidateUnsignedInteger(XmlQualifiedName propertyName, object value, ulong max)
  {
    ulong intValue;
    try
    {
      switch(Type.GetTypeCode(value.GetType()))
      {
        case TypeCode.Decimal:
        {
          decimal d = (decimal)value, trunc = decimal.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = decimal.ToUInt64(trunc);
          break;
        }
        case TypeCode.Double:
        {
          double d = (double)value, trunc = Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((ulong)trunc);
          break;
        }
        case TypeCode.Int16: intValue = checked((ulong)(short)value); break;
        case TypeCode.Int32: intValue = checked((ulong)(int)value); break;
        case TypeCode.Int64: intValue = checked((ulong)(long)value); break;
        case TypeCode.SByte: intValue = checked((ulong)(sbyte)value); break;
        case TypeCode.Single:
        {
          float d =  (float)value, trunc = (float)Math.Truncate(d);
          if(d != trunc) goto failed;
          else intValue = checked((ulong)trunc);
          break;
        }
        case TypeCode.UInt16: intValue = (ushort)value; break;
        case TypeCode.UInt32: intValue = (uint)value; break;
        case TypeCode.UInt64: intValue = (ulong)value; break;
        default: goto failed;
      }

      if(intValue <= max) return intValue;
    }
    catch(OverflowException)
    {
    }

    failed:
    throw new ArgumentException(propertyName.ToString() + " was expected to be an integer between 0 and " + max.ToStringInvariant() +
                                " (inclusive), but was " + value.ToString());
  }

  static readonly Dictionary<int, string> statusMessages = new Dictionary<int, string>()
  {
    { 100, "Continue" }, { 101, "Switching Protocols" },
    { 200, "OK" }, { 201, "Created" }, { 202, "Accepted" }, { 203, "Non-Authoritative Information" }, { 204, "No Content" },
    { 205, "Reset Content" }, { 206, "Partial Content" }, { 207, "Multi-Status" },
    { 300, "Multiple Choices" }, { 301, "Moved Permanently" }, { 302, "Found" }, { 303, "See Other" }, { 304, "Not Modified" },
    { 305, "Use Proxy" }, { 307, "Temporary Redirect" },
    { 400, "Bad Request" }, { 401, "Unauthorized" }, { 402, "Payment Required" }, { 403, "Forbidden" }, { 404, "Not Found" },
    { 405, "Method Not Allowed" }, { 406, "Not Acceptable" }, { 407, "Proxy Authentication Required" }, { 408, "Request Timeout" },
    { 409, "Conflict" }, { 410, "Gone" }, { 411, "Length Required" }, { 412, "Precondition Failed" }, { 413, "Request Entity Too Large" },
    { 414, "Request-URI Too Long" }, { 415, "Unsupported Media Type" }, { 416, "Requested Range Not Satisfiable" },
    { 417, "Expectation Failed" }, { 422, "Unprocessable Entity" }, { 423, "Locked" }, { 424, "Failed Dependency" },
    { 500, "Internal Server Error" }, { 501, "Not Implemented" }, { 502, "Bad Gateway" }, { 503, "Service Unavailable" },
    { 504, "Gateway Timeout" }, { 505, "HTTP Version Not Supported" }, { 507, "Insufficient Storage" }
  };

  const string wkdayRe = @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)";
  const string monthRe = @"(?<mon>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)";
  const string timeRe  = @"(?<h>\d\d):(?<min>\d\d):(?<s>\d\d)";
  const string rfc1123dateRe = wkdayRe + @", (?<d>\d\d) " + monthRe + @" (?<y>\d{4}) " + timeRe + " GMT";
  const string rfc850dateRe = @"(?:Mon|Tues|Wednes|Thurs|Fri|Satur|Sun)day, (?<d>\d\d)-" + monthRe + @"-(?<y>\d\d) " + timeRe + " GMT";
  const string ascdateRe = wkdayRe + " " + monthRe + @" (?<d>[\d ]\d) " + timeRe + @" (?<y>\d{4})";
  static readonly Regex httpDateRe = new Regex(@"^\s*(?:" + rfc1123dateRe + "|" + rfc850dateRe + "|" + ascdateRe + @")\s*$",
                                               RegexOptions.Compiled | RegexOptions.ECMAScript);
  static readonly string[] months = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
}
#endregion

#region EntityTag
/// <summary>Represents an HTTP entity tag. (See the description of entity tags in RFC 2616 for more details.)</summary>
public sealed class EntityTag : IElementValue
{
  /// <summary>Initializes a new <see cref="EntityTag"/>.</summary>
  /// <param name="tag">The entity tag. This is an arbitrary string value that represents the state of a resource's content, such that
  /// identical tag values represent either identical or equivalent content, depending on the value of the <paramref name="isWeak"/>
  /// parameter.
  /// </param>
  /// <param name="isWeak">If false, this represents a strong entity tag, where entities may have the same tag only if they are
  /// byte-for-byte identical. If true, this represents a weak entity tag, where entities may have the same tag as long as they could be
  /// swapped with no significant change in semantics.
  /// </param>
  public EntityTag(string tag, bool isWeak)
  {
    if(tag == null) throw new ArgumentNullException();
    Tag    = tag;
    IsWeak = isWeak;
  }

  /// <summary>Initializes a new <see cref="EntityTag"/> based on the value of an HTTP <c>ETag</c> header.</summary>
  public EntityTag(string headerValue)
  {
    if(headerValue == null) throw new ArgumentNullException();
    if(headerValue.Length < 2) throw new FormatException();
    int start = 1;
    char c = headerValue[0];
    if(c == 'W') // if the value starts with W/, that indicates a weak entity tag
    {
      if(headerValue[1] != '/' || headerValue.Length < 4) throw new FormatException();
      c = headerValue[2];
      IsWeak = true;
      start = 3;
    }

    if(c != '"' || headerValue[headerValue.Length-1] != '"') throw new FormatException();
    Tag = DAVUtility.UnquoteDecode(headerValue, start, headerValue.Length - start - 1);
  }

  /// <summary>Gets the entity tag string.</summary>
  public string Tag { get; private set; }

  /// <summary>If true, this represents a weak entity tag. If false, it represents a strong entity tag.</summary>
  public bool IsWeak { get; private set; }

  /// <inheritdoc/>
  /// <remarks>This method is uses the strong entity tag comparison, as if <see cref="StronglyEquals"/> was called.</remarks>
  public override bool Equals(object obj)
  {
    return StronglyEquals(obj as EntityTag);
  }

  /// <inheritdoc/>
  public override int GetHashCode()
  {
    return Tag.GetHashCode();
  }

  /// <summary>Determines whether two entity tags are identical in every way. This is the strong comparison function defined by RFC 2616
  /// section 13.3.3.
  /// </summary>
  public bool StronglyEquals(EntityTag match)
  {
    return !IsWeak && match != null && !match.IsWeak && Tag.OrdinalEquals(match.Tag);
  }

  /// <summary>Gets the value of the entity tag used within the HTTP <c>ETag</c> header as defined by RFC 2616 section 14.19.</summary>
  public string ToHeaderString()
  {
    string value = DAVUtility.QuoteString(Tag);
    if(IsWeak) value = "W/" + value;
    return value;
  }

  /// <summary>Determines whether two entity tags have identical tag strings. (The weakness flags don't have to match.) This is the weak
  /// comparison function defined by RFC 2616 section 13.3.3.
  /// </summary>
  public bool WeaklyEquals(EntityTag match)
  {
    return match != null && Tag.OrdinalEquals(match.Tag);
  }

  IEnumerable<string> IElementValue.GetNamespaces()
  {
    return null;
  }

  void IElementValue.WriteValue(XmlWriter writer, WebDAVContext context)
  {
    if(writer == null) throw new ArgumentNullException();
    writer.WriteString(ToHeaderString());
  }
}
#endregion

#region HttpHeaders
static class HttpHeaders
{
  public const string AcceptEncoding = "Accept-Encoding", AcceptRanges = "Accept-Ranges", Allow = "Allow";
  public const string ContentEncoding = "Content-Encoding", ContentLength = "Content-Length", ContentRange = "Content-Range";
  public const string DAV = "DAV", Destination = "Destination", ETag = "ETag";
  public const string If = "If", IfMatch = "If-Match", IfModifiedSince = "If-Modified-Since", IfNoneMatch = "If-None-Match";
  public const string IfRange = "If-Range", IfUnmodifiedSince = "If-Unmodified-Since", LastModified = "Last-Modified";
  public const string Location = "Location", LockToken = "Lock-Token", Overwrite = "Overwrite", Range = "Range", Timeout = "Timeout";
}
#endregion

#region HttpMethods
static class HttpMethods
{
  public const string Copy = "COPY", Delete = "DELETE", Get = "GET", Head = "HEAD", Lock = "LOCK", MkCol = "MKCOL", Move = "MOVE";
  public const string Options = "OPTIONS", Post = "POST", PropFind = "PROPFIND", PropPatch = "PROPPATCH", Put = "PUT", Trace = "TRACE";
  public const string Unlock = "UNLOCK";
}
#endregion

} // namespace AdamMil.WebDAV.Server
