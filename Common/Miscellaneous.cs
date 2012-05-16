using System.Collections.Generic;
using System.Text;
using System.Xml;
using System;
using System.Globalization;

namespace HiA.WebDAV
{

static class DAVUtility
{
  /// <summary>Gets the canonical message corresponding to an HTTP status code.</summary>
  public static string GetStatusCodeMessage(int httpStatusCode)
  {
    return statusMessages.TryGetValue(httpStatusCode);
  }

  /// <summary>Quotes a string in accordance with the <c>quoted-string</c> format defined in RFC 2616.</summary>
  public static string QuoteString(string value)
  {
    if(value != null)
    {
      for(int i=0; i<value.Length; i++)
      {
        char c = value[i];
        if(c == '"' || c == '\\')
        {
          StringBuilder sb = new StringBuilder(value.Length + 10);
          sb.Append(value, 0, i);
          for(; i<value.Length; i++)
          {
            c = value[i];
            if(c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
          }
          value = sb.ToString();
          break;
        }
      }
    }
    return value;
  }

  public static bool ValidateValueType(ref object value, XmlQualifiedName expectedType)
  {
    if(value == null)
    {
      return true;
    }
    else if(expectedType == Names.xsString)
    {
      if(!(value is string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
      return true;
    }
    if(expectedType == Names.xsDateTime || expectedType == Names.xsDate)
    {
      if(value is DateTime || value is DateTimeOffset) return true;
    }
    else if(expectedType == Names.xsInt)
    {
      long intValue;
      if(!ValidateSignedInteger(value, int.MinValue, int.MaxValue, out intValue)) return false;
      value = (int)intValue;
      return true;
    }
    else if(expectedType == Names.xsULong)
    {
      ulong intValue;
      if(!ValidateUnsignedInteger(value, ulong.MaxValue, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsLong)
    {
      long intValue;
      if(!ValidateSignedInteger(value, long.MinValue, long.MaxValue, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsBoolean)
    {
      if(value is bool) return true;
    }
    else if(expectedType == Names.xsUri)
    {
      Uri uri = value as Uri;
      if(uri == null && value is string && Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) value = uri;
      if(uri != null) return true;
    }
    else if(expectedType == Names.xsDouble)
    {
      if(value is double) return true;
      if(value is float || IsInteger(value))
      {
        value = Convert.ToDouble(value);
        return true;
      }
    }
    else if(expectedType == Names.xsFloat)
    {
      if(value is float) return true;
      if(IsInteger(value))
      {
        value = Convert.ToSingle(value);
        return true;
      }
    }
    else if(expectedType == Names.xsDecimal)
    {
      if(value is decimal) return true;
      if(value is double || value is float || IsInteger(value))
      {
        value = Convert.ToDecimal(value);
        return true;
      }
    }
    else if(expectedType == Names.xsUInt)
    {
      ulong intValue;
      if(!ValidateUnsignedInteger(value, uint.MaxValue, out intValue)) return false;
      value = (uint)intValue;
      return true;
    }
    else if(expectedType == Names.xsShort)
    {
      long intValue;
      if(!ValidateSignedInteger(value, short.MinValue, short.MaxValue, out intValue)) return false;
      value = (short)intValue;
      return true;
    }
    else if(expectedType == Names.xsUShort)
    {
      ulong intValue;
      if(!ValidateUnsignedInteger(value, ushort.MaxValue, out intValue)) return false;
      value = (ushort)intValue;
      return true;
    }
    else if(expectedType == Names.xsUByte)
    {
      ulong intValue;
      if(!ValidateUnsignedInteger(value, byte.MaxValue, out intValue)) return false;
      value = (byte)intValue;
      return true;
    }
    else if(expectedType == Names.xsSByte)
    {
      long intValue;
      if(!ValidateSignedInteger(value, sbyte.MinValue, sbyte.MaxValue, out intValue)) return false;
      value = (sbyte)intValue;
      return true;
    }
    else if(expectedType == Names.xsDuration)
    {
      if(value is XmlDuration || value is TimeSpan) return true;
    }
    else if(expectedType == Names.xsB64Binary || expectedType == Names.xsHexBinary)
    {
      if(value is byte[]) return true;
    }
    else
    {
      return true; // we don't know how to validate it, so assume it's valid
    }

    return false;
  }

  public static bool TryParseValue(string str, out object value, XmlQualifiedName expectedType)
  {
    if(string.IsNullOrEmpty(str))
    {
      value = null;
      return true;
    }
    else if(expectedType == Names.xsString)
    {
      value = str;
      return true;
    }
    if(expectedType == Names.xsDateTime || expectedType == Names.xsDate)
    {
      return XmlUtility.TryParseDateTime(str, out value);
    }
    else if(expectedType == Names.xsInt)
    {
      int intValue;
      if(!int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsULong)
    {
      ulong intValue;
      if(!ulong.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsLong)
    {
      long intValue;
      if(!ValidateSignedInteger(value, long.MinValue, long.MaxValue, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsBoolean)
    {
      bool boolValue;
      if(!XmlUtility.TryParseBoolean(str, out boolValue)) return false;
      value = boolValue;
      return true;
    }
    else if(expectedType == Names.xsUri)
    {
      Uri uri;
      if(!Uri.TryCreate((string)value, UriKind.RelativeOrAbsolute, out uri)) return false;
      value = uri;
      return true;
    }
    else if(expectedType == Names.xsDouble)
    {
      double doubleValue;
      if(!double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)) return false;
      value = doubleValue;
      return true;
    }
    else if(expectedType == Names.xsFloat)
    {
      float floatValue;
      if(!float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue)) return false;
      value = floatValue;
      return true;
    }
    else if(expectedType == Names.xsDecimal)
    {
      decimal decimalValue;
      if(!decimal.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out decimalValue)) return false;
      value = decimalValue;
      return true;
    }
    else if(expectedType == Names.xsUInt)
    {
      uint intValue;
      if(!uint.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsShort)
    {
      short intValue;
      if(!short.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsUShort)
    {
      ushort intValue;
      if(!ushort.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsUByte)
    {
      byte intValue;
      if(!byte.TryParse(str, NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out intValue))
      {
        return false;
      }
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsSByte)
    {
      sbyte intValue;
      if(!sbyte.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return false;
      value = intValue;
      return true;
    }
    else if(expectedType == Names.xsDuration)
    {
      XmlDuration duration;
      if(!XmlDuration.TryParse(str, out duration)) return false;
      value = duration;
      return true;
    }
    else if(expectedType == Names.xsB64Binary)
    {
      try { value = Convert.FromBase64String(str); }
      catch(FormatException) { return false; }
      return true;
    }
    else if(expectedType == Names.xsHexBinary)
    {
      byte[] binary;
      if(!BinaryUtility.TryParseHex(str, out binary)) return false;
      value = binary;
      return true;
    }

    value = null;
    return false;
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

  static bool ValidateSignedInteger(object value, long min, long max, out long intValue)
  {
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

      if(intValue >= min && intValue <= max) return true;
    }
    catch(OverflowException)
    {
    }

    failed:
    intValue = 0;
    return false;
  }

  static bool ValidateUnsignedInteger(object value, ulong max, out ulong intValue)
  {
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

      if(intValue <= max) return true;
    }
    catch(OverflowException)
    {
    }

    failed:
    intValue = 0;
    return false;
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
}

} // namespace HiA.WebDAV