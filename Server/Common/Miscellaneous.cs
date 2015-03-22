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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AdamMil.Collections;
using AdamMil.IO;
using AdamMil.Utilities;
using AdamMil.WebDAV.Server.Configuration;

namespace AdamMil.WebDAV.Server
{

#region BinaryReaderWriterExtensions
static class BinaryReaderWriterExtensions
{
  /// <summary>Reads a value that was written with <see cref="WriteValueWithType"/>.</summary>
  internal static object ReadValueWithType(this AdamMil.IO.BinaryReader reader)
  {
    if(reader == null) throw new ArgumentNullException();
    ValueType type = (ValueType)reader.ReadByte();
    object value;
    if((type & ValueType.IsArray) == 0) // if it's not an array...
    {
      switch(type)
      {
        case ValueType.Byte: value = reader.ReadByte(); break;
        case ValueType.Char: value = reader.ReadChar(); break;
        case ValueType.DateTime: value = reader.ReadDateTime(); break;
        case ValueType.DateTimeOffset:
          value = new DateTimeOffset(reader.ReadInt64(), new TimeSpan(reader.ReadEncodedInt32() * TimeSpan.TicksPerMinute));
          break;
        case ValueType.DBNull: value = DBNull.Value; break;
        case ValueType.Decimal: value = reader.ReadDecimal(); break;
        case ValueType.Double: value = reader.ReadDouble(); break;
        case ValueType.False: value = false; break;
        case ValueType.Guid: value = reader.ReadGuid(); break;
        case ValueType.Int16: value = reader.ReadInt16(); break;
        case ValueType.Int32: value = reader.ReadEncodedInt32(); break;
        case ValueType.Int64: value = reader.ReadEncodedInt64(); break;
        case ValueType.Null: value = null; break;
        case ValueType.SByte: value = reader.ReadSByte(); break;
        case ValueType.Single: value = reader.ReadSingle(); break;
        case ValueType.String: value = reader.ReadStringWithLength(); break;
        case ValueType.True: value = true; break;
        case ValueType.UInt16: value = reader.ReadUInt16(); break;
        case ValueType.UInt32: value = reader.ReadEncodedUInt32(); break;
        case ValueType.UInt64: value = reader.ReadEncodedUInt64(); break;
        case ValueType.TimeSpan: value = new TimeSpan(reader.ReadInt64()); break;
        case ValueType.XmlDuration:
        {
          int months = reader.ReadEncodedInt32();
          value = new XmlDuration(Math.Abs(months), (long)reader.ReadEncodedUInt64(), months < 0);
          break;
        }
        case ValueType.XmlQualifiedName: value = new XmlQualifiedName(reader.ReadStringWithLength(), reader.ReadStringWithLength()); break;
        default: throw new InvalidDataException("Unrecognized type: " + type.ToString());
      }
    }
    else // it's an array
    {
      int length = (int)reader.ReadEncodedUInt32();
      switch(type & ~ValueType.IsArray)
      {
        case ValueType.Byte: value = reader.ReadBytes(length); break;
        case ValueType.Char: value = reader.ReadChars(length); break;
        case ValueType.DateTime: value = reader.ReadDateTimes(length); break;
        case ValueType.DateTimeOffset:
        {
          DateTimeOffset[] array = new DateTimeOffset[length];
          for(int i=0; i<array.Length; i++)
          {
            array[i] = new DateTimeOffset(reader.ReadInt64(), new TimeSpan(reader.ReadEncodedInt32() * TimeSpan.TicksPerMinute));
          }
          value = array;
          break;
        }
        case ValueType.DBNull:
        {
          DBNull[] array = new DBNull[length];
          for(int i=0; i<array.Length; i++) array[i] = DBNull.Value;
          value = array;
          break;
        }
        case ValueType.Decimal: value = reader.ReadDecimals(length); break;
        case ValueType.Double: value = reader.ReadDoubles(length); break;
        case ValueType.False: // this stands for Boolean
        {
          bool[] array = new bool[length];
          for(int i=0; length != 0; )
          {
            int byteValue = reader.ReadByte();
            for(int bits=8; bits != 0; byteValue >>= 1, bits--)
            {
              array[i++] = (byteValue & 1) != 0;
              if(--length == 0) break;
            }
          }
          value = array;
          break;
        }
        case ValueType.Guid: value = reader.ReadGuids(length); break;
        case ValueType.Int16: value = reader.ReadInt16s(length); break;
        case ValueType.Int32: value = reader.ReadEncodedInt32s(length); break;
        case ValueType.Int64: value = reader.ReadEncodedInt64s(length); break;
        case ValueType.SByte: value = reader.ReadSBytes(length); break;
        case ValueType.Single: value = reader.ReadSingles(length); break;
        case ValueType.String: value = reader.ReadStringsWithLengths(length); break;
        case ValueType.TimeSpan:
        {
          TimeSpan[] array = new TimeSpan[length];
          for(int i=0; i<array.Length; i++) array[i] = new TimeSpan(reader.ReadInt64());
          value = array;
          break;
        }
        case ValueType.UInt16: value = reader.ReadUInt16s(length); break;
        case ValueType.UInt32: value = reader.ReadEncodedUInt32s(length); break;
        case ValueType.UInt64: value = reader.ReadEncodedUInt64s(length); break;
        case ValueType.XmlDuration:
        {
          XmlDuration[] array = new XmlDuration[length];
          for(int i=0; i<array.Length; i++)
          {
            int months = reader.ReadEncodedInt32();
            array[i] = new XmlDuration(Math.Abs(months), (long)reader.ReadEncodedUInt64(), months < 0);
          }
          value = array;
          break;
        }
        case ValueType.XmlQualifiedName:
        {
          XmlQualifiedName[] array = new XmlQualifiedName[length];
          for(int i=0; i<array.Length; i++)
          {
            string localName = reader.ReadStringWithLength();
            if(localName != null) array[i] = new XmlQualifiedName(localName, reader.ReadStringWithLength());
          }
          value = array;
          break;
        }
        default: throw new InvalidDataException("Unrecognized type: " + type.ToString());
      }
    }

    return value;
  }

  /// <summary>Writes an object to the stream. All built-in non-pointer primitive types are supported, plus <see cref="DateTime"/>,
  /// <see cref="DateTimeOffset"/>, <see cref="DBNull"/>, <see cref="Decimal"/>, <see cref="Guid"/>, <see cref="string"/>,
  /// <see cref="TimeSpan"/>, <see cref="XmlDuration"/>, <see cref="XmlQualifiedName"/>, and one-dimensional arrays of the previous types.
  /// Null values are also supported. The format in which the object will be written is not specified, but it can be read with
  /// <see cref="ReadValueWithType"/>.
  /// </summary>
  internal static void WriteValueWithType(this AdamMil.IO.BinaryWriter writer, object value)
  {
    if(writer == null) throw new ArgumentNullException();
    Type type = value == null ? null : value.GetType();
    if(type == null || !type.IsArray) // if it's not an array...
    {
      switch(Type.GetTypeCode(type))
      {
        case TypeCode.Boolean:
          writer.Write((byte)((bool)value ? ValueType.True : ValueType.False));
          break;
        case TypeCode.Byte:
          writer.Write((byte)ValueType.Byte);
          writer.Write((byte)value);
          break;
        case TypeCode.Char:
          writer.Write((byte)ValueType.Char);
          writer.Write((char)value);
          break;
        case TypeCode.DateTime:
          writer.Write((byte)ValueType.DateTime);
          writer.Write((DateTime)value);
          break;
        case TypeCode.DBNull:
          writer.Write((byte)ValueType.DBNull);
          break;
        case TypeCode.Decimal:
          writer.Write((byte)ValueType.Decimal);
          writer.Write((decimal)value);
          break;
        case TypeCode.Double:
          writer.Write((byte)ValueType.Double);
          writer.Write((double)value);
          break;
        case TypeCode.Empty:
          writer.Write((byte)ValueType.Null);
          break;
        case TypeCode.Int16:
          writer.Write((byte)ValueType.Int16);
          writer.Write((short)value);
          break;
        case TypeCode.Int32:
          writer.Write((byte)ValueType.Int32);
          writer.WriteEncoded((int)value);
          break;
        case TypeCode.Int64:
          writer.Write((byte)ValueType.Int64);
          writer.WriteEncoded((long)value);
          break;
        case TypeCode.SByte:
          writer.Write((byte)ValueType.SByte);
          writer.Write((sbyte)value);
          break;
        case TypeCode.Single:
          writer.Write((byte)ValueType.Single);
          writer.Write((float)value);
          break;
        case TypeCode.String:
          writer.Write((byte)ValueType.String);
          writer.WriteStringWithLength((string)value);
          break;
        case TypeCode.UInt16:
          writer.Write((byte)ValueType.UInt16);
          writer.Write((ushort)value);
          break;
        case TypeCode.UInt32:
          writer.Write((byte)ValueType.UInt32);
          writer.WriteEncoded((uint)value);
          break;
        case TypeCode.UInt64:
          writer.Write((byte)ValueType.UInt64);
          writer.WriteEncoded((ulong)value);
          break;
        default:
          if(type == typeof(DateTimeOffset))
          {
            DateTimeOffset dto = (DateTimeOffset)value;
            writer.Write((byte)ValueType.DateTimeOffset);
            writer.Write(dto.DateTime.Ticks);
            writer.WriteEncoded((int)(dto.Offset.Ticks / TimeSpan.TicksPerMinute)); // truncate the offset to whole minutes
          }
          else if(type == typeof(Guid))
          {
            writer.Write((byte)ValueType.Guid);
            writer.Write((Guid)value);
          }
          else if(type == typeof(TimeSpan))
          {
            writer.Write((byte)ValueType.TimeSpan);
            writer.Write(((TimeSpan)value).Ticks);
          }
          else if(type == typeof(XmlDuration))
          {
            XmlDuration xd = (XmlDuration)value;
            int months = xd.TotalMonths;
            if(xd.IsNegative) months = -months;
            writer.Write((byte)ValueType.XmlDuration);
            writer.WriteEncoded(months);
            writer.WriteEncoded((ulong)xd.Ticks);
          }
          else if(type == typeof(XmlQualifiedName))
          {
            XmlQualifiedName name = (XmlQualifiedName)value;
            writer.Write((byte)ValueType.XmlQualifiedName);
            writer.WriteStringWithLength(name.Name);
            writer.WriteStringWithLength(name.Namespace);
          }
          else
          {
            throw new ArgumentException("Unsupported type: " + type.FullName);
          }
          break;
      }
    }
    else // it's an array
    {
      Array array = (Array)value;
      if(array.Rank != 1) throw new ArgumentException("Multidimensional arrays are not supported.");
      type = type.GetElementType();
      switch(Type.GetTypeCode(type))
      {
        case TypeCode.Boolean:
        {
          writer.Write((byte)(ValueType.False | ValueType.IsArray)); // use False to represent arrays of booleans
          writer.WriteEncoded((uint)array.Length);
          bool[] boolArray = (bool[])value;
          for(int i=0; i<array.Length; )
          {
            int byteValue = 0;
            for(int bits=8, mask=1; bits != 0; mask <<= 1, bits--)
            {
              byteValue |= (boolArray[i] ? mask : 0);
              if(++i == array.Length) break;
            }
            writer.Write((byte)byteValue);
          }
          break;
        }
        case TypeCode.Byte:
          writer.Write((byte)(ValueType.Byte | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((byte[])value);
          break;
        case TypeCode.Char:
          writer.Write((byte)(ValueType.Char | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((char[])value);
          break;
        case TypeCode.DateTime:
          writer.Write((byte)(ValueType.DateTime | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((DateTime[])value);
          break;
        case TypeCode.DBNull:
          writer.Write((byte)(ValueType.DBNull | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          break;
        case TypeCode.Decimal:
          writer.Write((byte)(ValueType.Decimal | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((decimal[])value);
          break;
        case TypeCode.Double:
          writer.Write((byte)(ValueType.Double | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((double[])value);
          break;
        case TypeCode.Int16:
          writer.Write((byte)(ValueType.Int16 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((short[])value);
          break;
        case TypeCode.Int32:
          writer.Write((byte)(ValueType.Int32 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          foreach(int intValue in (int[])value) writer.WriteEncoded(intValue);
          break;
        case TypeCode.Int64:
          writer.Write((byte)(ValueType.Int64 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          foreach(long intValue in (long[])value) writer.WriteEncoded(intValue);
          break;
        case TypeCode.SByte:
          writer.Write((byte)(ValueType.SByte | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((sbyte[])value);
          break;
        case TypeCode.Single:
          writer.Write((byte)(ValueType.Single | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((float[])value);
          break;
        case TypeCode.String:
          writer.Write((byte)(ValueType.String | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          foreach(string str in (string[])value) writer.WriteStringWithLength(str);
          break;
        case TypeCode.UInt16:
          writer.Write((byte)(ValueType.UInt16 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          writer.Write((ushort[])value);
          break;
        case TypeCode.UInt32:
          writer.Write((byte)(ValueType.UInt32 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          foreach(uint intValue in (uint[])value) writer.WriteEncoded(intValue);
          break;
        case TypeCode.UInt64:
          writer.Write((byte)(ValueType.UInt64 | ValueType.IsArray));
          writer.WriteEncoded((uint)array.Length);
          foreach(ulong intValue in (ulong[])value) writer.WriteEncoded(intValue);
          break;
        default:
          if(type == typeof(DateTimeOffset))
          {
            writer.Write((byte)(ValueType.DateTimeOffset | ValueType.IsArray));
            writer.WriteEncoded((uint)array.Length);
            foreach(DateTimeOffset dto in (DateTimeOffset[])value)
            {
              writer.Write(dto.DateTime.Ticks);
              writer.WriteEncoded((int)(dto.Offset.Ticks / TimeSpan.TicksPerMinute)); // truncate the offset to whole minutes
            }
          }
          else if(type == typeof(Guid))
          {
            writer.Write((byte)(ValueType.Guid | ValueType.IsArray));
            writer.WriteEncoded((uint)array.Length);
            writer.Write((Guid[])value);
          }
          else if(type == typeof(TimeSpan))
          {
            writer.Write((byte)(ValueType.TimeSpan | ValueType.IsArray));
            writer.WriteEncoded((uint)array.Length);
            foreach(TimeSpan timeSpan in (TimeSpan[])value) writer.Write(timeSpan.Ticks);
          }
          else if(type == typeof(XmlDuration))
          {
            writer.Write((byte)(ValueType.XmlDuration | ValueType.IsArray));
            writer.WriteEncoded((uint)array.Length);
            foreach(XmlDuration xd in (XmlDuration[])value)
            {
              int months = xd.TotalMonths;
              if(xd.IsNegative) months = -months;
              writer.WriteEncoded(months);
              writer.WriteEncoded((ulong)xd.Ticks);
            }
          }
          else if(type == typeof(XmlQualifiedName))
          {
            writer.Write((byte)(ValueType.XmlQualifiedName | ValueType.IsArray));
            writer.WriteEncoded((uint)array.Length);
            foreach(XmlQualifiedName name in (XmlQualifiedName[])value)
            {
              if(name == null)
              {
                writer.WriteStringWithLength(null);
              }
              else
              {
                writer.WriteStringWithLength(name.Name);
                writer.WriteStringWithLength(name.Namespace);
              }
            }
          }
          else
          {
            throw new ArgumentException("Unsupported type: " + value.GetType().FullName);
          }
          break;
      }
    }
  }

  enum ValueType : byte
  {
    Null=0, False=1, True=2, Byte=3, Char=4, DateTime=5, Decimal=6, Double=7, Int16=8, Int32=9, Int64=10, SByte=11, Single=12, String=13,
    UInt16=14, UInt32=15, UInt64=16, Guid=17, DBNull=18, DateTimeOffset=19, TimeSpan=20, XmlDuration=21, XmlQualifiedName=22,
    IsArray=0x80
  }
}
#endregion

#region ContentEncoding
/// <summary>Represents a value from the <c>Content-Encoding</c> header.</summary>
public enum ContentEncoding
{
  /// <summary>The <c>identity</c> encoding, which does not alter the output.</summary>
  Identity=0,
  /// <summary>The <c>gzip</c> encoding, which compresses the output using the gzip algorithm.</summary>
  GZip,
  /// <summary>The <c>deflate</c> encoding, which compresses the output using the deflate algorithm.</summary>
  Deflate
}
#endregion

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
    if(start < 0 || length <= 0 || start + length < 0) throw new ArgumentOutOfRangeException();
    Start       = start;
    Length      = length;
    TotalLength = -1;
  }

  /// <summary>Initializes a new <see cref="ContentRange"/> that represents the given range within an entity body of the given length.</summary>
  public ContentRange(long start, long length, long totalLength) : this(start, length)
  {
    if(totalLength < 0) throw new ArgumentOutOfRangeException();
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

  /// <inheritdoc/>
  public override string ToString()
  {
    return ToHeaderString();
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

#region DAVHeaders
/// <summary>Defines HTTP headers commonly used with WebDAV resources.</summary>
public static class DAVHeaders
{
  /// <summary>The HTTP <c>Accept-Encoding</c> header, defined in RFC 7231 section 5.3.4.</summary>
  public const string AcceptEncoding = "Accept-Encoding";
  /// <summary>The HTTP <c>Accept-Ranges</c> header, defined in RFC 7233 section 2.3.</summary>
  public const string AcceptRanges = "Accept-Ranges";
  /// <summary>The HTTP <c>Allow</c> header, defined in RFC 7231 section 7.4.1.</summary>
  public const string Allow = "Allow";
  /// <summary>The HTTP <c>Content-Encoding</c> header, defined in RFC 7231 section 3.1.2.2.</summary>
  public const string ContentEncoding = "Content-Encoding";
  /// <summary>The HTTP <c>Content-Length</c> header, defined in RFC 7230 section 3.3.2.</summary>
  public const string ContentLength = "Content-Length";
  /// <summary>The HTTP <c>Content-Range</c> header, defined in RFC 7233 section 4.2.</summary>
  public const string ContentRange = "Content-Range";
  /// <summary>The HTTP <c>Content-Type</c> header, defined in RFC 7231 section 3.1.1.5.</summary>
  public const string ContentType = "Content-Type";
  /// <summary>The WebDAV <c>DAV</c> header, defined in RFC 4918 section 10.1.</summary>
  public const string DAV = "DAV";
  /// <summary>The WebDAV <c>Depth</c> header, defined in RFC 4918 section 10.2.</summary>
  public const string Depth = "Depth";
  /// <summary>The WebDAV <c>Destination</c> header, defined in RFC 4918 section 10.3.</summary>
  public const string Destination = "Destination";
  /// <summary>The HTTP <c>ETag</c> header, defined in RFC 7232 section 2.3.</summary>
  public const string ETag = "ETag";
  /// <summary>The WebDAV <c>If</c> header, defined in RFC 4918 section 10.4.</summary>
  public const string If = "If";
  /// <summary>The HTTP <c>If-Match</c> header, defined in RFC 7232 section 3.1.</summary>
  public const string IfMatch = "If-Match";
  /// <summary>The HTTP <c>If-Modified-Since</c> header, defined in RFC 7232 section 3.3.</summary>
  public const string IfModifiedSince = "If-Modified-Since";
  /// <summary>The HTTP <c>If-None-Match</c> header, defined in RFC 7232 section 3.2.</summary>
  public const string IfNoneMatch = "If-None-Match";
  /// <summary>The HTTP <c>If-Range</c> header, defined in RFC 7233 section 3.2.</summary>
  public const string IfRange = "If-Range";
  /// <summary>The HTTP <c>If-Unmodified-Since</c> header, defined in RFC 7232 section 3.4.</summary>
  public const string IfUnmodifiedSince = "If-Unmodified-Since";
  /// <summary>The HTTP <c>Last-Modified</c> header, defined in RFC 7232 section 2.2.</summary>
  public const string LastModified = "Last-Modified";
  /// <summary>The HTTP <c>Location</c> header, defined in RFC 7231 section 7.1.2.</summary>
  public const string Location = "Location";
  /// <summary>The WebDAV <c>Lock-Token</c> header, defined in RFC 4918 section 10.5.</summary>
  public const string LockToken = "Lock-Token";
  /// <summary>The WebDAV <c>Overwrite</c> header, defined in RFC 4918 section 10.6.</summary>
  public const string Overwrite = "Overwrite";
  /// <summary>The HTTP <c>Range</c> header, defined in RFC 7233 section 3.1.</summary>
  public const string Range = "Range";
  /// <summary>The WebDAV <c>Timeout</c> header, defined in RFC 4918 section 10.7.</summary>
  public const string Timeout = "Timeout";
}
#endregion

#region DAVMethods
/// <summary>Defines HTTP methods (verbs) commonly used with WebDAV resources.</summary>
public static class DAVMethods
{
  /// <summary>The WebDAV <c>COPY</c> verb, defined in RFC 4918 section 9.8.</summary>
  public const string Copy = "COPY";
  /// <summary>The HTTP <c>DELETE</c> verb, defined in RFC 7231 section 4.3.5.</summary>
  public const string Delete = "DELETE";
  /// <summary>The HTTP <c>GET</c> verb, defined in RFC 7231 section 4.3.1.</summary>
  public const string Get = "GET";
  /// <summary>The HTTP <c>HEAD</c> verb, defined in RFC 7231 section 4.3.2.</summary>
  public const string Head = "HEAD";
  /// <summary>The WebDAV <c>LOCK</c> verb, defined in RFC 4918 section 9.10.</summary>
  public const string Lock = "LOCK";
  /// <summary>The WebDAV <c>MKCOL</c> verb, defined in RFC 4918 section 9.3.</summary>
  public const string MkCol = "MKCOL";
  /// <summary>The WebDAV <c>MOVE</c> verb, defined in RFC 4918 section 9.9.</summary>
  public const string Move = "MOVE";
  /// <summary>The HTTP <c>OPTIONS</c> verb, defined in RFC 7231 section 4.3.6.</summary>
  public const string Options = "OPTIONS";
  /// <summary>The HTTP <c>POST</c> verb, defined in RFC 7231 section 4.3.3.</summary>
  public const string Post = "POST";
  /// <summary>The WebDAV <c>PROPFIND</c> verb, defined in RFC 4918 section 9.1.</summary>
  public const string PropFind = "PROPFIND";
  /// <summary>The WebDAV <c>PROPPATCH</c> verb, defined in RFC 4918 section 9.2.</summary>
  public const string PropPatch = "PROPPATCH";
  /// <summary>The HTTP <c>PUT</c> verb, defined in RFC 7231 section 4.3.4.</summary>
  public const string Put = "PUT";
  /// <summary>The HTTP <c>TRACE</c> verb, defined in RFC 7231 section 4.3.8.</summary>
  public const string Trace = "TRACE";
  /// <summary>The WebDAV <c>UNLOCK</c> verb, defined in RFC 4918 section 9.11.</summary>
  public const string Unlock = "UNLOCK";
}
#endregion

#region DAVUtility
/// <summary>Contains useful utilities for DAV services.</summary>
public static class DAVUtility
{
  /// <summary>Encodes a string representing an unescaped path into a minimally escaped form so that it can be used to construct
  /// canonical paths. This is suitable for encoding complete paths, partial paths, or path segments, assuming that the service does not
  /// allow path segments to contain slash (<c>/</c>) characters. (Otherwise, the service must construct paths by individually escaping
  /// path segments and separating them with slashes. <see cref="CanonicalSegmentEncode"/> can be used to escape segments.)
  /// </summary>
  /// <remarks>This method only encodes the percent sign (<c>%</c>) character. The resulting path is not legal to insert into a URI because
  /// not all reserved characters are escaped. If the service reserves additional characters (such as using a semicolon to separate a
  /// resource name from parameters to that resource, as in <c>/dir;version=1.1/file</c>), the service is responsible for minimally
  /// URL-escaping those characters after calling this method, for instance if a resource name itself may contain a semicolon using the
  /// previous example. The hex digits used in such additional escaping must be normalized to uppercase. A service must not use any
  /// characters that must be escaped in URLs for this purpose. (See RFC 3986. Common safe delimiters are semicolon, comma, and equals.)
  /// Otherwise, such characters will later be incorrectly escaped by <see cref="UriPathPartialEncode"/>.
  /// </remarks>
  public static string CanonicalPathEncode(string path)
  {
    if(!string.IsNullOrEmpty(path))
    {
      int i = path.IndexOf('%');
      if(i >= 0)
      {
        StringBuilder sb = new StringBuilder(path.Length + 12);
        sb.Append(path, 0, i);
        do
        {
          char c = path[i];
          if(c == '%') sb.Append("%25");
          else sb.Append(c);
        } while(++i < path.Length);
        path = sb.ToString();
      }
    }

    return path;
  }

  /// <summary>Encodes a string representing an unescaped path segment into a minimally escaped form so that it can be used to construct
  /// canonical paths. This is suitable for encoding a single path segment, but is not suitable for encoding complete paths or partial
  /// paths containing multiple segments. (These are already minimally encoded, assuming their segments were minimally encoded.) For
  /// example, you may use this method to encode a single file name <c>fileName</c> in order to append it to <c>dir/</c>, but you must
  /// not use it to encode a complete path such as <c>dir/fileName</c>.
  /// </summary>
  /// <remarks>This method only encodes the forward slash (<c>/</c>) and percent sign (<c>%</c>) characters. The resulting path segment is
  /// not legal to insert into a URI because not all reserved characters are escaped. If the service reserves additional characters (such
  /// as using a semicolon to separate a resource name from parameters to that resource, as in <c>/dir;version=1.1/file</c>), the service
  /// is responsible for minimally URL-escaping those characters after calling this method, for instance if a resource name itself may
  /// contain a semicolon using the previous example. The hex digits used in such additional escaping must be normalized to uppercase.
  /// A service must not use any characters that must be escaped in URLs for this purpose. (See RFC 3986. Common safe delimiters are
  /// semicolon, comma, and equals.) Otherwise, such characters will later be incorrectly escaped by <see cref="UriPathPartialEncode"/>.
  /// </remarks>
  public static string CanonicalSegmentEncode(string pathSegment)
  {
    if(!string.IsNullOrEmpty(pathSegment))
    {
      for(int i=0; i<pathSegment.Length; i++)
      {
        char c = pathSegment[i];
        if(c == '/' || c == '%')
        {
          StringBuilder sb = new StringBuilder(pathSegment.Length + 9);
          sb.Append(pathSegment, 0, i);
          while(true)
          {
            if(c == '/') sb.Append("%2F"); // hex digits should be normalized to uppercase (RFC 3986 section 6.2.2.1)
            else if(c == '%') sb.Append("%25");
            else sb.Append(c);
            if(++i == pathSegment.Length) break;
            c = pathSegment[i];
          }
          pathSegment = sb.ToString();
          break;
        }
      }
    }

    return pathSegment;
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
    return new EntityTag(Convert.ToBase64String(BinaryUtility.HashSHA1(entityBody)), false, false);
  }

  /// <summary>Returns a random MIME boundary.</summary>
  public static string CreateMimeBoundary()
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

  /// <summary>Wraps an output stream as necessary to apply a content encoding.</summary>
  /// <param name="outputStream">The output stream.</param>
  /// <param name="encoding">The encoding to apply to the stream.</param>
  /// <param name="leaveOpen">If false, closing the wrapper will close <paramref name="outputStream"/>. If true, the output stream will
  /// remain open after the wrapper is closed.
  /// </param>
  public static Stream EncodeOutputStream(Stream outputStream, ContentEncoding encoding, bool leaveOpen)
  {
    if(encoding == ContentEncoding.GZip) outputStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen);
    else if(encoding == ContentEncoding.Deflate) outputStream = new DeflateStream(outputStream, CompressionMode.Compress, leaveOpen);
    else if(leaveOpen) outputStream = new DelegateStream(outputStream, false);
    return outputStream;
  }

  /// <summary>Converts the given <see cref="DateTime"/> to UTC and truncates it to one-second precision as necessary. This produces a
  /// <see cref="DateTime"/> value that can be compared with other <c>HTTP-date</c> values.
  /// </summary>
  public static DateTime GetHttpDate(DateTime dateTime)
  {
    // HTTP dates are in UTC, so convert the last modified time to UTC as well. also, round the timestamp down to the nearest second
    // because HTTP dates only have one-second precision and DateTime.ToString("R") also truncates downward
    if(dateTime.Kind == DateTimeKind.Local) dateTime = dateTime.ToUniversalTime();
    long subsecondTicks = dateTime.Ticks % TimeSpan.TicksPerSecond;
    if(subsecondTicks != 0) dateTime = dateTime.AddTicks(-subsecondTicks);
    return dateTime;
  }

  /// <summary>Converts the given <see cref="DateTime"/> to an <c>HTTP-date</c> header value, which is used for headers like
  /// <c>Last-Modified</c>, etc.
  /// </summary>
  public static string GetHttpDateHeader(DateTime dateTime)
  {
    return GetHttpDate(dateTime).ToString("R", CultureInfo.InvariantCulture);
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

  /// <summary>Parses an <c>HTTP-date</c> value, as defined in RFC 7231 section 7.1.1.1.</summary>
  public static DateTime ParseHttpDate(string value)
  {
    if(value == null) throw new ArgumentNullException();
    DateTime datetime;
    if(!TryParseHttpDate(value, out datetime)) throw new FormatException();
    return datetime;
  }

  /// <summary>Quotes an ASCII string (which must not be null) in accordance with the <c>quoted-string</c> format defined in RFC 7230
  /// section 3.2.6.
  /// </summary>
  public static string QuoteString(string ascii)
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

  /// <summary>Attempts to parse an <c>HTTP-date</c> value, as defined in RFC 7231 section 7.1.1.1.</summary>
  public static bool TryParseHttpDate(string value, out DateTime date)
  {
    Match m = value == null ? null : httpDateRe.Match(value);
    if(m == null || !m.Success)
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

  /// <summary>Decodes a path from a URI into a minimally escaped form (see <see cref="CanonicalPathEncode"/>) that can be used in the
  /// construction of canonical paths.
  /// </summary>
  public static string UriPathPartialDecode(string path)
  {
    if(!string.IsNullOrEmpty(path))
    {
      int i = path.IndexOf('%');
      if(i >= 0)
      {
        StringBuilder sb = new StringBuilder(path.Length);
        sb.Append(path, 0, i);
        UriDecoder decoder = new UriDecoder();
        do
        {
          char c = path[i];
          if(c != '%')
          {
            sb.Append(c);
          }
          else
          {
            c = decoder.Decode(path, ref i);
            // slash and percent remain encoded in minimally encoded paths
            if(c == '/') sb.Append("%2F"); // hex digits should be normalized to uppercase (RFC 3986 section 6.2.2.1)
            else if(c == '%') sb.Append("%25");
            else sb.Append(c);
          }
        } while(++i < path.Length);
        path = sb.ToString();
      }
    }
    return path;
  }

  /// <summary>Further encodes a path that is already minimally encoded (see <see cref="CanonicalPathEncode"/>) into a form that can be
  /// legally used within a URI. This must be done before a path is emitted into a URI reference such as a <c>DAV:href</c> element or an
  /// HTML page.
  /// </summary>
  /// <remarks>This encodes characters that are reserved within paths according to RFC 3986, except for the two characters assumed to have
  /// already been encoded by <see cref="CanonicalSegmentEncode"/>.
  /// </remarks>
  public static string UriPathPartialEncode(string path)
  {
    if(!string.IsNullOrEmpty(path))
    {
      for(int i=0; i<path.Length; i++)
      {
        char c = path[i];
        if(MustBeEscapedInPath(c))
        {
          StringBuilder sb = new StringBuilder(path.Length + 33);
          sb.Append(path, 0, i);
          byte[] bytes = null;
          while(true)
          {
            if(!MustBeEscapedInPath(c))
            {
              sb.Append(c);
            }
            else // otherwise, it's reserved and must be percent-encoded in UTF-8
            {
              if(c <= 127) // if the character is low ASCII, then it can be represented as UTF-8 with the same byte value
              {
                sb.Append('%').Append(BinaryUtility.ToHexChar((byte)(c >> 4))).Append(BinaryUtility.ToHexChar((byte)(c & 15)));
              }
              else // otherwise, we have to go through the whole encoding process
              {
                if(bytes == null) bytes = new byte[4]; // all UTF-8 characters fit in 4 bytes
                for(int j=0, count=Encoding.UTF8.GetBytes(path, i, 1, bytes, 0); j<count; j++)
                {
                  byte value = bytes[j];
                  sb.Append('%').Append(BinaryUtility.ToHexChar((byte)(value >> 4))).Append(BinaryUtility.ToHexChar((byte)(value & 15)));
                }
              }
            }
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

  /// <summary>Normalizes the set of encoded characters in a path. The result will have the minimal set of characters encoded while still
  /// being legal to insert into a URI path. This is equivalent to using <see cref="UriPathPartialDecode"/> and then
  /// <see cref="UriPathPartialEncode"/> on the result.
  /// </summary>
  public static string UriPathNormalize(string path)
  {
    return UriPathPartialEncode(UriPathPartialDecode(path));
  }

  /// <summary>Performs complete decoding of all percent-encoded characters within the given string. Unlike
  /// <see cref="HttpUtility.UrlDecode(string)"/> and <see cref="o:System.Net.WebUtility.UrlDecode"/>, this method does not decode plus
  /// signs ('+') into spaces (which is incorrect for paths) and does not accept the non-standard <c>%uXXXX</c> encoding.
  /// </summary>
  public static string UriPathDecode(string path)
  {
    if(!string.IsNullOrEmpty(path))
    {
      int i = path.IndexOf('%');
      if(i >= 0)
      {
        StringBuilder sb = new StringBuilder(path.Length);
        sb.Append(path, 0, i);
        UriDecoder decoder = new UriDecoder();
        do
        {
          char c = path[i];
          if(c == '%') c = decoder.Decode(path, ref i);
          sb.Append(c);
        } while(++i < path.Length);
        path = sb.ToString();
      }
    }
    return path;
  }

  /// <summary>Ensures that the given path has a trailing slash if it's not an empty string. Empty strings will be returned as-is, to
  /// avoid converting relative paths to absolute paths.
  /// </summary>
  public static string WithTrailingSlash(string path)
  {
    if(path == null) throw new ArgumentNullException();
    return path.Length == 0 || path[path.Length-1] == '/' ? path : path + "/";
  }

  /// <summary>Extracts an <see cref="XmlElement"/> into its own <see cref="XmlDocument"/>.</summary>
  internal static XmlElement Extract(this XmlElement element)
  {
    if(element == null) throw new ArgumentNullException();
    XmlDocument emptyDoc = new XmlDocument();
    XmlElement newElement = (XmlElement)emptyDoc.ImportNode(element, true);
    // include the xml:lang attribute, which may not have been imported along with the node (due to xml:lang needing to be inherited)
    string xmlLang = element.GetInheritedAttributeValue(DAVNames.xmlLang);
    if(!string.IsNullOrEmpty(xmlLang)) newElement.SetAttribute(DAVNames.xmlLang, xmlLang);
    FixQNames(element, newElement);
    emptyDoc.AppendChild(newElement);
    return newElement;
  }

  /// <summary>Uniquely encodes a string into a valid file name.</summary>
  internal static string FileNameEncode(string str)
  {
    if(!string.IsNullOrEmpty(str))
    {
      for(int i=0; i<str.Length; i++)
      {
        char c = str[i];
        if(c == '%' || badFileNameChars.Contains(c))
        {
          StringBuilder sb = new StringBuilder(str.Length + 10);
          sb.Append(str, 0, i);
          while(true)
          {
            if(c == '%' || badFileNameChars.Contains(c))
            {
              sb.Append('%').Append(BinaryUtility.ToHexChar((byte)(c&15))).Append(BinaryUtility.ToHexChar((byte)((c>>4)&15)));
            }
            else
            {
              sb.Append(c);
            }
            if(++i == str.Length) break;
            c = str[i];
          }
          str = sb.ToString();
          break;
        }
      }
    }

    return str;
  }

  internal static Stream GetManifestResourceStream(string path)
  {
    string name = typeof(WebDAVModule).Namespace + "." + path.Replace('/', '.');
    return System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
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

  /// <summary>Gets the XML Schema type name (e.g. xs:string) representing the type of the given value, or null if the type cannot be
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
          else if(value is Guid) type = DAVNames.msGuid;
          else if(value is byte[]) type = DAVNames.xsB64Binary;
          else if(value is XmlQualifiedName) type = DAVNames.xsQName;
          else if(value is XmlDuration || value is TimeSpan) type = DAVNames.xsDuration;
          break;
      }
    }
    return type;
  }

  /// <summary>Determines whether a property value of the given type can be stored .</summary>
  internal static bool IsStorablePropertyType(object value)
  {
    if(value == null) return true;
    Type type = value.GetType();
    if(type.IsArray)
    {
      if(((Array)value).Rank != 1) return false; // we only support one-dimensional arrays
      type = type.GetElementType();
    }
    if(Type.GetTypeCode(type) != TypeCode.Object) return true;
    return type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(XmlQualifiedName) ||
           type == typeof(XmlDuration) || type == typeof(TimeSpan);
  }

  /// <summary>Determines whether the value is a token as defined by section 3.2.6 of RFC 7230.</summary>
  internal static bool IsToken(string value)
  {
    for(int i=0; i<value.Length; i++)
    {
      char c = value[i];
      // control characters, spaces, and certain punctuation marks are illegal in tokens
      if(c <= 32 || c >= 0x7f || (c < 'A' || c > 'z' || c > 'Z' && c < 'a') && Array.BinarySearch(illegalTokenChars, c) >= 0)
      {
        return false;
      }
    }
    return true;
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

  internal static object ParseXmlValue(string text, XmlQualifiedName type, XmlNode context)
  {
    if(type == null || context == null) throw new ArgumentNullException();

    object value;
    if(string.IsNullOrEmpty(text))
    {
      value = null;
    }
    else if(type.Namespace.OrdinalEquals(DAVNames.XmlSchema))
    {
      switch(type.Name)
      {
        case "anyURI": value = new Uri(text, UriKind.RelativeOrAbsolute); break;
        case "base64Binary": value = Convert.FromBase64String(text); break;
        case "boolean": value = XmlConvert.ToBoolean(text); break;
        case "byte": value = XmlConvert.ToSByte(text); break;
        case "dateTime": case "date": value = XmlUtility.ParseDateTime(text); break;
        case "decimal": value = XmlConvert.ToDecimal(text); break;
        case "double": value = XmlConvert.ToDouble(text); break;
        case "duration": value = XmlDuration.Parse(text); break;
        case "float": value = XmlConvert.ToSingle(text); break;
        case "hexBinary": value = BinaryUtility.ParseHex(text, true); break;
        case "int": value = XmlConvert.ToInt32(text); break;
        case "long": value = XmlConvert.ToInt64(text); break;
        case "QName":
        {
          XmlQualifiedName qname = context.ParseQualifiedName(text);
          qname.Validate();
          value = qname;
          break;
        }
        case "short": value = XmlConvert.ToInt16(text); break;
        case "string": value = text; break;
        case "unsignedByte": value = XmlConvert.ToByte(text); break;
        case "unsignedInt": value = XmlConvert.ToUInt32(text); break;
        case "unsignedLong": value = XmlConvert.ToUInt64(text); break;
        case "unsignedShort": value = XmlConvert.ToUInt16(text); break;
        default: value = text; break;
      }
    }
    else if(type == DAVNames.msGuid)
    {
      value = new Guid(text);
    }
    else
    {
      value = text;
    }
    return value;
  }

  internal static string RemoveTrailingSlash(string path)
  {
    if(path == null) throw new ArgumentNullException();
    return path.Length > 1 && path[path.Length-1] == '/' ? path.Substring(0, path.Length-1) : path;
  }

  /// <summary>Executes the given action, returns its <see cref="ConditionCode"/> or a condition code fashioned from any exception it
  /// throws.
  /// </summary>
  internal static ConditionCode TryExecute(Func<ConditionCode> action)
  {
    if(action == null) throw new ArgumentNullException();
    ConditionCode status;
    try
    {
      status = action();
    }
    catch(System.Web.HttpException ex)
    {
      WebDAVException wde = ex as WebDAVException;
      status =
        (wde != null ? wde.ConditionCode : null) ?? new ConditionCode(ex.GetHttpCode(), WebDAVModule.FilterErrorMessage(ex.Message));
    }
    catch(UnauthorizedAccessException)
    {
      status = ConditionCodes.Forbidden;
    }
    catch(Exception ex)
    {
      status = new ConditionCode(HttpStatusCode.InternalServerError, WebDAVModule.FilterErrorMessage(ex.Message));
    }
    return status;
  }

  internal static ConditionCode TryExecute<A>(Func<A,ConditionCode> action, A arg)
  {
    if(action == null) throw new ArgumentNullException();
    return TryExecute(() => action(arg));
  }

  internal static ConditionCode TryExecute<A1,A2>(Func<A1,A2,ConditionCode> action, A1 a1, A2 a2)
  {
    if(action == null) throw new ArgumentNullException();
    return TryExecute(() => action(a1, a2));
  }

  internal static ConditionCode TryExecute<A1,A2,A3>(Func<A1,A2,A3,ConditionCode> action, A1 a1, A2 a2, A3 a3)
  {
    if(action == null) throw new ArgumentNullException();
    return TryExecute(() => action(a1, a2, a3));
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
    int i = value.IndexOf('\\', start, length);
    if(i >= 0)
    {
      for(int end=start+length; i<end; i++)
      {
        StringBuilder sb = new StringBuilder(length-1);
        sb.Append(value, start, i-start);
        do
        {
          char c = value[i];
          if(c == '\\')
          {
            if(++i == end) throw new FormatException();
            c = value[i];
          }
          sb.Append(c);
        } while(++i < end);
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

  internal static void ValidateRelativePath(string relativePath)
  {
    if(relativePath == null) throw new ArgumentNullException();
    if(relativePath.Length != 0 && relativePath[0] == '/') throw new ArgumentException("The given path is not relative.");
  }

  internal static object ValidatePropertyValue(XmlQualifiedName propertyName, object value, XmlQualifiedName expectedType)
  {
    if(value == null || expectedType == null)
    {
      return value;
    }
    else if(expectedType.Namespace.OrdinalEquals(DAVNames.XmlSchema))
    {
      switch(expectedType.Name)
      {
        case "anyURI":
        {
          if(value is Uri) return value;
          Uri uri;
          if(value is string && Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) return uri;
          break;
        }
        case "base64Binary": case "hexBinary":
          if(value is byte[]) return value;
          break;
        case "boolean":
          if(value is bool) return value;
          break;
        case "byte":
          return (sbyte)ValidateSignedInteger(propertyName, value, sbyte.MinValue, sbyte.MaxValue);
        case "dateTime": case "date":
          if(value is DateTime || value is DateTimeOffset) return value;
          break;
        case "decimal":
          if(value is double || value is float || value is decimal || IsInteger(value))
          {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
          }
          break;
        case "double":
          if(value is double || value is float || IsInteger(value)) return Convert.ToDouble(value, CultureInfo.InvariantCulture);
          break;
        case "duration":
          if(value is XmlDuration || value is TimeSpan) return value;
          break;
        case "float":
          if(value is float || IsInteger(value)) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
          break;
        case "int":
          return (int)ValidateSignedInteger(propertyName, value, int.MinValue, int.MaxValue);
        case "long":
          return ValidateSignedInteger(propertyName, value, long.MinValue, long.MaxValue);
        case "QName":
          if(value is XmlQualifiedName) return value;
          break;
        case "short":
          return (short)ValidateSignedInteger(propertyName, value, short.MinValue, short.MaxValue);
        case "string":
          if(!(value is string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
          return value;
        case "unsignedByte":
          return (byte)ValidateUnsignedInteger(propertyName, value, byte.MaxValue);
        case "unsignedInt":
          return (uint)ValidateUnsignedInteger(propertyName, value, uint.MaxValue);
        case "unsignedLong":
          return ValidateUnsignedInteger(propertyName, value, ulong.MaxValue);
        case "unsignedShort":
          return (ushort)ValidateUnsignedInteger(propertyName, value, ushort.MaxValue);
        default:
          return value;
      }
    }
    else if(expectedType == DAVNames.msGuid)
    {
      if(value is Guid) return value;
      if(value is string) return new Guid((string)value);
    }
    else
    {
      return value; // we don't know how to validate it, so assume it's valid
    }

    throw new ArgumentException(propertyName.ToString() + " is expected to be of type " + expectedType.ToString() + " but was of type " +
                                value.GetType().FullName);
  }

  /// <summary>Sets the response status code to the given status code and writes an message to the page. This method does not terminate
  /// the request.
  /// </summary>
  internal static void WriteStatusResponse(HttpRequest request, HttpResponse response, int httpStatusCode, string errorText)
  {
    // we could apply logic here to filter out potentially sensitive error messages (e.g. for 5xx errors), but we won't because we trust
    // the callers to check the configuration settings
    response.SetStatus(httpStatusCode);

    // write a response body unless the status code disallows it
    if(CanIncludeBody(httpStatusCode))
    {
      if(response.BufferOutput) response.SetContentType("text/plain");
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
      response.SetStatus(code);
      if(CanIncludeBody(code.StatusCode))
      {
        response.ContentEncoding = System.Text.Encoding.UTF8;
        response.SetContentType("application/xml"); // media type specified by RFC 4918 section 8.2
        XmlWriterSettings settings = new XmlWriterSettings() { CloseOutput = false, Indent = true, IndentChars = "\t" };
        using(XmlWriter writer = XmlWriter.Create(response.OutputStream, settings)) code.WriteErrorXml(writer);
      }
    }
  }

  #region UriDecoder
  /// <summary>Performs decoding of percent-encoded characters in a Uri.</summary>
  struct UriDecoder
  {
    /// <summary>Given a URI string and an index that points to a '%' character, returns the next character decoded from it and updates
    /// <paramref name="index"/> to point to the last character of the encoded data.
    /// </summary>
    public char Decode(string str, ref int index)
    {
      int i = index;
      byte hi, lo;
      if(i+2 >= str.Length || !BinaryUtility.TryParseHex(str[++i], out hi) || !BinaryUtility.TryParseHex(str[++i], out lo))
      {
        throw new FormatException("Bad hex digits in URL.");
      }

      char c = (char)((hi<<4) | lo);
      if(c >= 128) // UTF-8 characters less than 128 are mapped directly to low ASCII. characters greater than that must be decoded
      {
        if(decoder == null)
        {
          decoder = Encoding.UTF8.GetDecoder();
          bytes   = new byte[1];
          chars   = new char[1];
        }

        bytes[0] = (byte)c;
        for(int length=1; ; length++)
        {
          if(decoder.GetChars(bytes, 0, 1, chars, 0, false) != 0) break;
          // as Bill Gates said, 4 bytes ought to be enough for anybody (decoding UTF-8)
          if(length == 4 || str[++i] != '%') throw new FormatException("Invalid UTF-8-encoded character in URL.");
          if(i+2 >= str.Length || !BinaryUtility.TryParseHex(str[++i], out hi) || !BinaryUtility.TryParseHex(str[++i], out lo))
          {
            throw new FormatException("Bad hex digits in URL.");
          }
          bytes[0] = (byte)((hi<<4) | lo);
        }
        c = chars[0];
      }

      index = i;
      return c;
    }

    Decoder decoder;
    byte[] bytes;
    char[] chars;
  }
  #endregion

  /// <summary>Determines whether the given HTTP status code allows entity bodies.</summary>
  static bool CanIncludeBody(int statusCode)
  {
    return statusCode != (int)HttpStatusCode.NoContent && statusCode != (int)HttpStatusCode.NotModified;
  }

  /// <summary>Recursively fixes QName values in xsi:type attributes and element content to reference the correct namespaces in the new
  /// element context. Unfortunately, this doesn't correct QName values in other attributes because we don't know which other attributes
  /// expect QNames.
  /// </summary>
  static void FixQNames(XmlElement oldElem, XmlElement newElem)
  {
    string xsiType = oldElem.GetAttribute(DAVNames.xsiType);
    if(!string.IsNullOrEmpty(xsiType)) // if the element has an xsi:type attribute...
    {
      XmlQualifiedName qname = oldElem.ParseQualifiedName(xsiType);
      newElem.SetAttribute(DAVNames.xsiType, qname.ToString(newElem));
      if(qname == DAVNames.xsQName && oldElem.HasSimpleNonSpaceContent()) // if the content is also (supposed to be) a QName...
      {
        newElem.InnerText = oldElem.ParseQualifiedName(oldElem.InnerText).ToString(newElem);
      }
    }

    for(XmlNode oc=oldElem.FirstChild, nc=newElem.FirstChild; oc != null; oc=oc.NextSibling, nc=nc.NextSibling)
    {
      if(oc.NodeType == XmlNodeType.Element) FixQNames((XmlElement)oc, (XmlElement)nc);
    }
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

  static bool MustBeEscapedInPath(char c)
  {
    return c >= 'a' ? c > 'z' && c != '~' :
           c >= 'A' ? c > 'Z' && (c == '`' || c == '^' || c == '\\') :
           c >= '0' ? c > '9' && (c <= '=' ? c == ':' || c == '<' : c == '>' || c == '?') :
           c <= ' ' || c == '"' || c == '#'; // % must also be escaped, but we'll assume it's already been
  }                                          // because 'c' comes from a minimally encoded path 

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
        case TypeCode.Byte: intValue = (byte)value; break;
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
  static readonly char[] badFileNameChars = Path.GetInvalidFileNameChars();
  static readonly Regex httpDateRe = new Regex(@"^\s*(?:" + rfc1123dateRe + "|" + rfc850dateRe + "|" + ascdateRe + @")\s*$",
                                               RegexOptions.Compiled | RegexOptions.ECMAScript);
  /// <summary>A list of printable characters that are not legal in tokens, sorted by ASCII value.</summary>
  static readonly char[] illegalTokenChars = { '"', '(', ')', ',', '/', ':', ';', '<', '=', '>', '?', '@', '[', '\\', ']', '{', '}', };
  static readonly string[] months = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
}
#endregion

#region EntityTag
/// <summary>Represents an HTTP entity tag. (See the description of entity tags in RFC 7232 for more details.)</summary>
/// <remarks>Note that the format of HTTP entity tags changed between RFC 2616 and RFC 7232. In the former, it was defined as an HTTP
/// <c>quoted-string</c>, and thus allowed backslash escaping and embedded double-quote characters. In the latter, it was redefined
/// to be an opaque string that does not allow double-quote characters, with no escaping. Thus, you should avoid backslash characters
/// in entity tags to prevent clients written to the older standard from performing backslash unescaping.
/// </remarks>
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
  /// <remarks>Note that the format of HTTP entity tags changed between RFC 2616 and RFC 7232. In the former, it was defined as an HTTP
  /// <c>quoted-string</c>, and thus allowed backslash escaping and embedded double-quote characters. In the latter, it was redefined
  /// to be an opaque string that does not allow double-quote characters, with no escaping. Thus, you should avoid backslash characters
  /// in entity tags to prevent clients written to the older standard from performing backslash unescaping.
  /// </remarks>
  public EntityTag(string tag, bool isWeak) : this(tag, isWeak, true) { }

  /// <summary>Initializes a new <see cref="EntityTag"/> based on the value of an HTTP <c>ETag</c> header.</summary>
  public EntityTag(string headerValue)
  {
    if(headerValue == null) throw new ArgumentNullException();
    string tag;
    bool isWeak;
    if(!TryParse(headerValue, out tag, out isWeak)) throw new FormatException();
    Tag    = tag;
    IsWeak = isWeak;
  }

  internal EntityTag(string tag, bool isWeak, bool validate)
  {
    if(tag == null) throw new ArgumentNullException();
    Tag    = tag;
    IsWeak = isWeak;

    if(validate && !IsValidTag(tag, 0, tag.Length))
    {
      throw new ArgumentException("Invalid entity tag (" + ToHeaderString() + "). See RFC 7232 section 2.3.");
    }
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

  /// <summary>Determines whether two entity tags are identical in every way. This is the strong comparison function defined by RFC 7232
  /// section 2.3.2.
  /// </summary>
  public bool StronglyEquals(EntityTag match)
  {
    return !IsWeak && match != null && !match.IsWeak && Tag.OrdinalEquals(match.Tag);
  }

  /// <summary>Gets the value of the entity tag used within the HTTP <c>ETag</c> header as defined by RFC 7232 section 2.3.</summary>
  public string ToHeaderString()
  {
    return (IsWeak ? "W/" : null) + "\"" + Tag + "\"";
  }

  /// <inheritdoc/>
  public override string ToString()
  {
    return ToHeaderString();
  }

  /// <summary>Determines whether two entity tags have identical tag strings. (The weakness flags don't have to match.) This is the weak
  /// comparison function defined by RFC 7232 section 2.3.2.
  /// </summary>
  public bool WeaklyEquals(EntityTag match)
  {
    return match != null && Tag.OrdinalEquals(match.Tag);
  }

  /// <summary>Attempts to parse an <see cref="EntityTag"/> based on the value of an HTTP <c>ETag</c> header. Returns null if the value
  /// could not be successfully parsed.
  /// </summary>
  public static EntityTag TryParse(string headerValue)
  {
    string tag;
    bool isWeak;
    return TryParse(headerValue, out tag, out isWeak) ? new EntityTag(tag, isWeak, false) : null;
  }

  /// <summary>Attempts to parse an <see cref="EntityTag"/> based on the value of an HTTP <c>ETag</c> header. Returns true if the value
  /// could be successfully parsed.
  /// </summary>
  public static bool TryParse(string headerValue, out EntityTag entityTag)
  {
    entityTag = TryParse(headerValue);
    return entityTag != null;
  }

  internal static EntityTag TryParse(string value, ref int index, int endExclusive)
  {
    int i = index;
    bool isWeak = false;
    char c = value[i++];
    if(c == 'W') // if the entity tag starts with W, that represents a weak tag
    {
      if(endExclusive - i < 2 || value[i++] != '/') return null; // W must be followed by a slash
      c = value[i++]; // grab the first one, which should be a quotation mark
      isWeak = true;
    }
    if(c != '"' || i == endExclusive) return null; // a double quote at the minimum must follow, so expect more characters

    // find the end of the entity tag and add it to the list
    int start = i;
    i = value.IndexOf('"', i, endExclusive-i);
    if(i == -1) return null;
    index = i+1;

    return IsValidTag(value, start, i) ? new EntityTag(value.Substring(start, i-start), isWeak, false) : null;
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

  static bool IsValidTag(string str, int index, int endExclusive)
  {
    // in RFC 2616, the tag was defined as a quoted-string, and we would have used UnquoteDecode to decode it. RFC 7232 has changed the
    // format of the entity tag to simply be any ASCII characters from 0x21 to 0xFF except 0x22 and 0x7F (i.e. all high ascii and all
    // visible low ASCII except double-quote). effectively, this removed backslash escaping from the format
    for(; index<endExclusive; index++)
    {
      char c = str[index];
      if(c < 0x21 || c > 0xFF || c == '"' || c == 0x7F) return false;
    }
    return true;
  }

  static bool TryParse(string headerValue, out string tag, out bool isWeak)
  {
    tag    = null;
    isWeak = false;

    if(headerValue.Length < 2) return false;
    int start = 1;
    char c = headerValue[0];
    if(c == 'W') // if the value starts with W/, that indicates a weak entity tag
    {
      if(headerValue[1] != '/' || headerValue.Length < 4) return false; // W must be followed by a slash
      c = headerValue[2];
      isWeak = true;
      start = 3;
    }

    if(c != '"' || headerValue[headerValue.Length-1] != '"') return false;

    if(!IsValidTag(headerValue, start, headerValue.Length-1)) return false;
    tag = headerValue.Substring(start, headerValue.Length-1 - start);
    return true;
  }
}
#endregion

#region HttpResponseExtensions
/// <summary>Adds extensions to the <see cref="HttpResponse"/> type.</summary>
public static class HttpResponseExtensions
{
  /// <summary>Sets the HTTP <c>Content-Encoding</c> header. This is not the same as setting <see cref="HttpResponse.ContentEncoding"/>.</summary>
  public static void SetContentEncodingHeader(this HttpResponse response, ContentEncoding encoding)
  {
    if(response == null) throw new ArgumentNullException();

    string value;
    switch(encoding)
    {
      case ContentEncoding.Deflate: value = "deflate"; break;
      case ContentEncoding.GZip: value = "gzip"; break;
      case ContentEncoding.Identity: value = null; break;
      default: throw new ArgumentException("Unknown content encoding: " + encoding.ToString());
    }

    if(value == null) response.Headers.Remove(DAVHeaders.ContentEncoding);
    else response.Headers[DAVHeaders.ContentEncoding] = value;
  }

  /// <summary>Sets the HTTP <c>Content-Type</c> header. This is preferred over simply setting <see cref="HttpResponse.ContentType"/>
  /// because sometimes ASP.NET or IIS override that.
  /// </summary>
  public static void SetContentType(this HttpResponse response, string contentType)
  {
    if(response == null) throw new ArgumentNullException();
    response.ContentType = contentType;
    // sometimes ASP.NET ignores the value of ContentType and fails to set the Content-Type header, or sets it incorrectly. this happens
    // when responding to HEAD requests, for instance. so we also have to mess with the response.Headers
    if(string.IsNullOrEmpty(contentType)) response.Headers.Remove(DAVHeaders.ContentType);
    else response.Headers[DAVHeaders.ContentType] = contentType;
  }

  /// <summary>Sets <see cref="HttpResponse.StatusCode"/> and <see cref="HttpResponse.StatusDescription"/> based on the given
  /// <see cref="HttpStatusCode"/>.
  /// </summary>
  public static void SetStatus(this HttpResponse response, HttpStatusCode httpStatusCode)
  {
    SetStatus(response, (int)httpStatusCode);
  }

  /// <summary>Sets <see cref="HttpResponse.StatusCode"/> and <see cref="HttpResponse.StatusDescription"/> based on the given HTTP status
  /// code.
  /// </summary>
  public static void SetStatus(this HttpResponse response, int httpStatusCode)
  {
    if(response == null) throw new ArgumentNullException();
    response.StatusCode        = httpStatusCode;
    response.StatusDescription = DAVUtility.GetStatusCodeMessage(httpStatusCode);
  }

  /// <summary>Sets <see cref="HttpResponse.StatusCode"/> and <see cref="HttpResponse.StatusDescription"/> based on the given
  /// <see cref="ConditionCode"/>.
  /// </summary>
  public static void SetStatus(this HttpResponse response, ConditionCode conditionCode)
  {
    if(conditionCode == null) throw new ArgumentNullException();
    response.SetStatus(conditionCode.StatusCode);
  }
}
#endregion

} // namespace AdamMil.WebDAV.Server
