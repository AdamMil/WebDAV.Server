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

namespace AdamMil.WebDAV.Server
{

/// <summary>Provides methods to get appropriate file extensions for MIME types and guess appropriate MIME types for file names.</summary>
static class MimeTypes
{
	static MimeTypes()
	{
		extToMime = new Dictionary<string,string>();
		mimeToExt = new Dictionary<string,string>();

		// TODO: it'd be better if these came from an external file somehow
		#region Map MIME types
		AddMimeMap("application/atom+xml", "atom");
		AddMimeMap("application/directx", "x");
		AddMimeMap("application/ecmascript", "es");
		AddMimeMap("application/exi", "exi");
		AddMimeMap("application/envoy", "evy");
		AddMimeMap("application/fractals", "fif");
		AddMimeMap("application/futuresplash", "spl");
		AddMimeMap("application/hta", "hta");
		AddMimeMap("application/internet-property-stream", "acx");
		AddMimeMap("application/java-archive", "jar");
		AddMimeMap("application/javascript", "js");
		AddMimeMap("application/json", "json");
		AddMimeMap("application/liquidmotion", "jck");
		AddMimeMap("application/liquidmotion", "jcz");
		AddMimeMap("application/mac-binhex40", "hqx");
		AddMimeMap("application/mathematica", "ma");
		AddMimeMap("application/mathematica", "mb", true);
		AddMimeMap("application/mathematica", "nb");
		AddMimeMap("application/mathml+xml", "mml");
		AddMimeMap("application/msaccess", "accdb", true);
		AddMimeMap("application/msaccess", "accde");
		AddMimeMap("application/msaccess", "accdt");
		AddMimeMap("application/msword", "doc", true);
		AddMimeMap("application/msword", "dot");
		AddMimeMap("application/octet-stream", "bin", true);
		AddMimeMap("application/oda", "oda");
		AddMimeMap("application/olescript", "axs");
		AddMimeMap("application/onenote", "one", true);
		AddMimeMap("application/onenote", "onea");
		AddMimeMap("application/onenote", "onepkg");
		AddMimeMap("application/onenote", "onetmp");
		AddMimeMap("application/onenote", "onetoc");
		AddMimeMap("application/onenote", "onetoc2");
		AddMimeMap("application/opensearchdescription+xml", "osdx");
		AddMimeMap("application/pdf", "pdf");
		AddMimeMap("application/pics-rules", "prf");
		AddMimeMap("application/pkcs10", "p10");
		AddMimeMap("application/pkcs7-mime", "p7c");
		AddMimeMap("application/pkcs7-mime", "p7m", true);
		AddMimeMap("application/pkcs7-signature", "p7s");
		AddMimeMap("application/pkix-crl", "crl");
		AddMimeMap("application/postscript", "ai");
		AddMimeMap("application/postscript", "eps");
		AddMimeMap("application/postscript", "ps", true);
		AddMimeMap("application/rtf", "rtf");
		AddMimeMap("application/set-payment-initiation", "setpay");
		AddMimeMap("application/set-registration-initiation", "setreg");
		AddMimeMap("application/sgml", "sgml");
		AddMimeMap("application/streamingmedia", "ssm");
		AddMimeMap("application/vcard+xml", "xml", true, false);
		AddMimeMap("application/vnd.fdf", "fdf");
		AddMimeMap("application/vnd.ms-cab-compressed", "cab");
		AddMimeMap("application/vnd.ms-excel", "xla");
		AddMimeMap("application/vnd.ms-excel", "xlc");
		AddMimeMap("application/vnd.ms-excel", "xlm");
		AddMimeMap("application/vnd.ms-excel", "xls", true);
		AddMimeMap("application/vnd.ms-excel", "xlt");
		AddMimeMap("application/vnd.ms-excel", "xlw");
		AddMimeMap("application/vnd.ms-excel.12", "xlsx", false, false);
		AddMimeMap("application/vnd.ms-excel.addin.12", "xlam");
		AddMimeMap("application/vnd.ms-excel.binary.12", "xlsb");
		AddMimeMap("application/vnd.ms-excel.macroenabled.12", "xlsm");
		AddMimeMap("application/vnd.ms-excel.macroenabledtemplate.12", "xltm");
		AddMimeMap("application/vnd.ms-excel.template.12", "xltx", false, false);
		AddMimeMap("application/vnd.ms-office.calx", "calx");
		AddMimeMap("application/vnd.ms-officetheme", "thmx");
		AddMimeMap("application/vnd.ms-pki.certstore", "sst");
		AddMimeMap("application/vnd.ms-pki.pko", "pko");
		AddMimeMap("application/vnd.ms-pki.seccat", "cat");
		AddMimeMap("application/vnd.ms-pki.stl", "stl");
		AddMimeMap("application/vnd.ms-powerpoint", "pot");
		AddMimeMap("application/vnd.ms-powerpoint", "pps");
		AddMimeMap("application/vnd.ms-powerpoint", "ppt", true);
		AddMimeMap("application/vnd.ms-powerpoint.addin.12", "ppam");
		AddMimeMap("application/vnd.ms-powerpoint.macroenabled.12", "pptm");
		AddMimeMap("application/vnd.ms-powerpoint.presentation.12", "pptx", false, false);
		AddMimeMap("application/vnd.ms-powerpoint.show.12", "ppsx", false, false);
		AddMimeMap("application/vnd.ms-powerpoint.show.macroenabled.12", "ppsm");
		AddMimeMap("application/vnd.ms-powerpoint.slide.macroenabled.12", "sldm");
		AddMimeMap("application/vnd.ms-powerpoint.template.12", "potx", false, false);
		AddMimeMap("application/vnd.ms-powerpoint.template.macroenabled.12", "potm");
		AddMimeMap("application/vnd.ms-project", "mpp");
		AddMimeMap("application/vnd.ms-visio.viewer", "vdx");
		AddMimeMap("application/vnd.ms-word.document.12", "docx", false, false);
		AddMimeMap("application/vnd.ms-word.document.macroenabled.12", "docm");
		AddMimeMap("application/vnd.ms-word.template.12", "dotx", false, false);
		AddMimeMap("application/vnd.ms-word.template.macroenabled.12", "dotm");
		AddMimeMap("application/vnd.ms-works", "wcm");
		AddMimeMap("application/vnd.ms-works", "wdb");
		AddMimeMap("application/vnd.ms-works", "wks", true);
		AddMimeMap("application/vnd.ms-works", "wps");
		AddMimeMap("application/vnd.ms-xpsdocument", "xps");
		AddMimeMap("application/vnd.oasis.opendocument.presentation", "odp");
		AddMimeMap("application/vnd.oasis.opendocument.spreadsheet", "ods");
		AddMimeMap("application/vnd.oasis.opendocument.text", "odt");
		AddMimeMap("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.spreadsheetml.template", "xltx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.presentationml.presentation", "pptx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.presentationml.slide", "sldx");
		AddMimeMap("application/vnd.openxmlformats-officedocument.presentationml.slideshow", "ppsx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.presentationml.template", "potx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx", true, true);
		AddMimeMap("application/vnd.openxmlformats-officedocument.wordprocessingml.template", "dotx", true, true);
		AddMimeMap("application/vnd.rn-realmedia", "rm");
		AddMimeMap("application/vnd.visio", "vsd", true);
		AddMimeMap("application/vnd.visio", "vss");
		AddMimeMap("application/vnd.visio", "vst");
		AddMimeMap("application/vnd.visio", "vsw");
		AddMimeMap("application/vnd.visio", "vsx");
		AddMimeMap("application/vnd.visio", "vtx");
		AddMimeMap("application/vnd.wap.wmlc", "wmlc");
		AddMimeMap("application/vnd.wap.wmlscriptc", "wmlsc");
		AddMimeMap("application/winhlp", "hlp");
		AddMimeMap("application/x-bcpio", "bcpio");
		AddMimeMap("application/x-bittorrent", "torrent");
		AddMimeMap("application/x-cdf", "cdf");
		AddMimeMap("application/x-chm", "chm");
		AddMimeMap("application/x-compress", "z");
		AddMimeMap("application/x-compressed", "tgz");
		AddMimeMap("application/x-cpio", "cpio");
		AddMimeMap("application/x-csh", "csh");
		AddMimeMap("application/x-director", "dcr");
		AddMimeMap("application/x-director", "dir", true);
		AddMimeMap("application/x-director", "dxr");
		AddMimeMap("application/x-dvi", "dvi");
		AddMimeMap("application/x-font-ttf", "ttf");
		AddMimeMap("application/x-gtar", "gtar");
		AddMimeMap("application/x-gzip", "gz");
		AddMimeMap("application/x-hdf", "hdf");
		AddMimeMap("application/x-internet-signup", "ins");
		AddMimeMap("application/x-internet-signup", "isp");
		AddMimeMap("application/x-iphone", "iii");
		AddMimeMap("application/x-java-applet", "class");
		AddMimeMap("application/x-javascript", "js");
		AddMimeMap("application/x-latex", "latex");
		AddMimeMap("application/x-lzh-compressed", "lzh");
		AddMimeMap("application/x-miva-compiled", "mvc");
		AddMimeMap("application/x-ms-application", "application");
		AddMimeMap("application/x-ms-manifest", "manifest");
		AddMimeMap("application/x-ms-reader", "lit");
		AddMimeMap("application/x-ms-vsto", "vsto");
		AddMimeMap("application/x-ms-wmd", "wmd");
		AddMimeMap("application/x-ms-wmz", "wmz");
		AddMimeMap("application/x-ms-xbap", "xbap");
		AddMimeMap("application/x-msaccess", "mdb");
		AddMimeMap("application/x-mscardfile", "crd");
		AddMimeMap("application/x-msclip", "clp");
		AddMimeMap("application/x-msdownload", "dll");
		AddMimeMap("application/x-msi", "msi");
		AddMimeMap("application/x-msmediaview", "m13");
		AddMimeMap("application/x-msmediaview", "m14");
		AddMimeMap("application/x-msmediaview", "mvb");
		AddMimeMap("application/x-msmetafile", "wmf");
		AddMimeMap("application/x-msmoney", "mny");
		AddMimeMap("application/x-mspublisher", "pub");
		AddMimeMap("application/x-msschedule", "scd");
		AddMimeMap("application/x-msterminal", "trm");
		AddMimeMap("application/x-mswrite", "wri");
		AddMimeMap("application/x-netcdf", "nc");
		AddMimeMap("application/x-oleobject", "hhc");
		AddMimeMap("application/x-perfmon", "pma");
		AddMimeMap("application/x-perfmon", "pmc");
		AddMimeMap("application/x-perfmon", "pml");
		AddMimeMap("application/x-perfmon", "pmr");
		AddMimeMap("application/x-perfmon", "pmw");
		AddMimeMap("application/x-pkcs12", "p12");
		AddMimeMap("application/x-pkcs12", "pfx");
		AddMimeMap("application/x-pkcs7-certificates", "p7b");
		AddMimeMap("application/x-pkcs7-certificates", "spc");
		AddMimeMap("application/x-pkcs7-certreqresp", "p7r");
		AddMimeMap("application/x-quicktimeplayer", "qtl");
		AddMimeMap("application/x-rar-compressed", "rar");
		AddMimeMap("application/x-sh", "sh");
		AddMimeMap("application/x-shar", "shar");
		AddMimeMap("application/x-shockwave-flash", "swf");
		AddMimeMap("application/x-silverlight-app", "xap");
		AddMimeMap("application/x-skype", "skype");
		AddMimeMap("application/x-smaf", "mmf");
		AddMimeMap("application/x-stuffit", "sit");
		AddMimeMap("application/x-sv4cpio", "sv4cpio");
		AddMimeMap("application/x-sv4crc", "sv4crc");
		AddMimeMap("application/x-tar", "tar");
		AddMimeMap("application/x-tcl", "tcl");
		AddMimeMap("application/x-tex", "tex");
		AddMimeMap("application/x-texinfo", "texi");
		AddMimeMap("application/x-texinfo", "texinfo");
		AddMimeMap("application/x-troff", "roff");
		AddMimeMap("application/x-troff", "t");
		AddMimeMap("application/x-troff", "tr");
		AddMimeMap("application/x-troff-man", "man");
		AddMimeMap("application/x-troff-me", "me");
		AddMimeMap("application/x-troff-ms", "ms");
		AddMimeMap("application/x-ustar", "ustar");
		AddMimeMap("application/x-wais-source", "src");
		AddMimeMap("application/x-x509-ca-cert", "cer", true);
		AddMimeMap("application/x-x509-ca-cert", "crt");
		AddMimeMap("application/x-x509-ca-cert", "der");
		AddMimeMap("application/x-xpinstall", "xpi");
		AddMimeMap("application/x-zip-compressed", "zip", true, false);
		AddMimeMap("application/xaml+xml", "xaml");
		AddMimeMap("application/xhtml+xml", "xhtml", true);
		AddMimeMap("application/xhtml+xml", "xht");
		AddMimeMap("application/xslt+xml", "xsl");
		AddMimeMap("application/xslt+xml", "xslt", true);
		AddMimeMap("application/xml", "disco");
		AddMimeMap("application/xml", "mno");
		AddMimeMap("application/xml", "resx");
		AddMimeMap("application/xml", "vml");
		AddMimeMap("application/xml", "wsdl");
		AddMimeMap("application/xml-dtd", "dtd");
		AddMimeMap("application/zip", "zip", true);
		AddMimeMap("audio/ac3", "ac3");
		AddMimeMap("audio/aiff", "aif");
		AddMimeMap("audio/aiff", "aifc");
		AddMimeMap("audio/aiff", "aiff", true);
		AddMimeMap("audio/basic", "au", true);
		AddMimeMap("audio/basic", "snd");
		AddMimeMap("audio/mid", "mid", true);
		AddMimeMap("audio/mid", "midi");
		AddMimeMap("audio/mid", "rmi");
		AddMimeMap("audio/midi", "midi");
		AddMimeMap("audio/mp4", "mp4", true);
		AddMimeMap("audio/mp4", "mpg4");
		AddMimeMap("audio/mpeg", "mp3");
		AddMimeMap("audio/mpegurl", "m3u");
		AddMimeMap("audio/ogg", "ogg");
		AddMimeMap("audio/wav", "wav");
		AddMimeMap("audio/vnd.rn-realaudio", "ra");
		AddMimeMap("audio/vnd.rn-realmedia", "rm");
		AddMimeMap("audio/x-mpegurl", "m3u");
		AddMimeMap("audio/x-ms-wax", "wax");
		AddMimeMap("audio/x-ms-wma", "wma");
		AddMimeMap("audio/x-pn-realaudio", "ram");
		AddMimeMap("audio/x-pn-realaudio-plugin", "rpm");
		AddMimeMap("audio/x-smd", "smd", true);
		AddMimeMap("audio/x-smd", "smx");
		AddMimeMap("audio/x-smd", "smz");
		AddMimeMap("drawing/x-dwf", "dwf");
		AddMimeMap("image/bmp", "bmp", true);
		AddMimeMap("image/bmp", "dib");
		AddMimeMap("image/cis-cod", "cod");
		AddMimeMap("image/gif", "gif");
		AddMimeMap("image/ief", "ief");
		AddMimeMap("image/jpeg", "jpe");
		AddMimeMap("image/jpeg", "jpeg", true);
		AddMimeMap("image/jpeg", "jpg");
		AddMimeMap("image/pjpeg", "jfif");
		AddMimeMap("image/png", "png");
		AddMimeMap("image/tiff", "tif");
		AddMimeMap("image/tiff", "tiff", true);
		AddMimeMap("image/vnd.adobe.photoshop", "psd");
		AddMimeMap("image/vnd.djvu", "djv");
		AddMimeMap("image/vnd.djvu", "djvu", true);
		AddMimeMap("image/vnd.microsoft.icon", "ico");
		AddMimeMap("image/vnd.rn-realflash", "rf");
		AddMimeMap("image/vnd.rn-realpix", "rp");
		AddMimeMap("image/vnd.svf", "svf");
		AddMimeMap("image/vnd.wap.wbmp", "wbmp");
		AddMimeMap("image/vnd.xiff", "xif", true); // .xif is mentioned in the IANA registration form
		AddMimeMap("image/vnd.xiff", "xiff");
		AddMimeMap("image/x-cmu-raster", "ras");
		AddMimeMap("image/x-cmx", "cmx");
		AddMimeMap("image/x-jg", "art");
		AddMimeMap("image/x-portable-anymap", "pnm");
		AddMimeMap("image/x-portable-bitmap", "pbm");
		AddMimeMap("image/x-portable-graymap", "pgm");
		AddMimeMap("image/x-portable-pixmap", "ppm");
		AddMimeMap("image/x-rgb", "rgb");
		AddMimeMap("image/x-xbitmap", "xbm");
		AddMimeMap("image/x-xpixmap", "xpm");
		AddMimeMap("image/x-xwindowdump", "xwd");
		AddMimeMap("message/rfc822", "eml", true);
		AddMimeMap("message/rfc822", "mht");
		AddMimeMap("message/rfc822", "mhtml");
		AddMimeMap("message/rfc822", "nws");
		AddMimeMap("text/css", "css");
		AddMimeMap("text/csv", "csv");
		AddMimeMap("text/dlm", "dlm");
		AddMimeMap("text/h323", "323");
		AddMimeMap("text/html", "htm");
		AddMimeMap("text/html", "html", true);
		AddMimeMap("text/iuls", "uls");
		AddMimeMap("text/plain", "asm");
		AddMimeMap("text/plain", "bas");
		AddMimeMap("text/plain", "c");
		AddMimeMap("text/plain", "cs");
		AddMimeMap("text/plain", "cnf");
		AddMimeMap("text/plain", "cpp");
		AddMimeMap("text/plain", "h");
		AddMimeMap("text/plain", "hpp");
		AddMimeMap("text/plain", "log");
		AddMimeMap("text/plain", "map");
		AddMimeMap("text/plain", "txt", true);
		AddMimeMap("text/plain", "vb");
		AddMimeMap("text/plain", "vcs");
		AddMimeMap("text/plain", "xdr");
		AddMimeMap("text/richtext", "rtx");
		AddMimeMap("text/scriptlet", "sct");
		AddMimeMap("text/sgml", "sgml");
		AddMimeMap("text/tab-separated-values", "tsv");
		AddMimeMap("text/vbscript", "vbs");
		AddMimeMap("text/vnd.rn-realtext", "rt");
		AddMimeMap("text/vnd.wap.wml", "wml");
		AddMimeMap("text/vnd.wap.wmlscript", "wmls");
		AddMimeMap("text/webviewhtml", "htt");
		AddMimeMap("text/x-component", "htc");
		AddMimeMap("text/x-hdml", "hdml");
		AddMimeMap("text/x-java-source", "java");
		AddMimeMap("text/x-ms-odc", "odc");
		AddMimeMap("text/x-setext", "etx");
		AddMimeMap("text/x-vcard", "vcf");
		AddMimeMap("text/xml", "dll.config");
		AddMimeMap("text/xml", "exe.config");
		AddMimeMap("text/xml", "xml", true);
		AddMimeMap("text/xml", "xsd");
		AddMimeMap("text/xml", "xsf");
		AddMimeMap("video/mpeg", "m1v");
		AddMimeMap("video/mpeg", "mp2");
		AddMimeMap("video/mpeg", "mpa");
		AddMimeMap("video/mpeg", "mpe");
		AddMimeMap("video/mpeg", "mpeg", true);
		AddMimeMap("video/mpeg", "mpg");
		AddMimeMap("video/mpeg", "mpv2");
		AddMimeMap("video/mp4", "mp4", true);
		AddMimeMap("video/mp4", "mpg4");
		AddMimeMap("video/ogg", "ogv");
		AddMimeMap("video/quicktime", "mov", true);
		AddMimeMap("video/quicktime", "qt");
		AddMimeMap("video/vivo", "vivo");
		AddMimeMap("video/vnd.dvb.file", "dvb");
		AddMimeMap("video/x-flv", "flv");
		AddMimeMap("video/x-ivf", "ivf");
		AddMimeMap("video/x-la-asf", "lsf", true);
		AddMimeMap("video/x-la-asf", "lsx");
		AddMimeMap("video/x-ms-asf", "asf", true);
		AddMimeMap("video/x-ms-asf", "asr");
		AddMimeMap("video/x-ms-asf", "asx");
		AddMimeMap("video/x-ms-asf", "nsc");
		AddMimeMap("video/x-ms-wm", "wm");
		AddMimeMap("video/x-ms-wmp", "wmp");
		AddMimeMap("video/x-ms-wmv", "wmv");
		AddMimeMap("video/x-ms-wmx", "wmx");
		AddMimeMap("video/x-ms-wvx", "wvx");
		AddMimeMap("video/x-msvideo", "avi");
		AddMimeMap("video/x-sgi-movie", "movie");
		AddMimeMap("x-world/x-vrml", "flr");
		AddMimeMap("x-world/x-vrml", "wrl");
		AddMimeMap("x-world/x-vrml", "wrz");
		AddMimeMap("x-world/x-vrml", "xaf");
		AddMimeMap("x-world/x-vrml", "xof");
		#endregion

		CanonicalizeExtensions();
	}

	/// <summary>Returns an appropriate file extension for the given MIME type, including the initial period.
	/// If an appropriate extension is not known, null is returned.
	/// </summary>
	public static string GetFileExtension(string mimeType)
	{
		if(string.IsNullOrEmpty(mimeType)) throw new ArgumentException();
		mimeType = mimeType.ToLowerInvariant();
		string extension;
		mimeToExt.TryGetValue(mimeType, out extension);
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

    return null;
	}

	static void AddMimeMap(string mimeType, string extension)
	{
		AddMimeMap(mimeType, extension, false, true);
	}

	static void AddMimeMap(string mimeType, string extension, bool canonicalExtension)
	{
		AddMimeMap(mimeType, extension, canonicalExtension, true);
	}

	static void AddMimeMap(string mimeType, string extension, bool canonicalExtension, bool canonicalMimeType)
	{
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
}

} // namespace AdamMil.WebDAV.Server
