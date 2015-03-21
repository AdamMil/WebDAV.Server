This project contains some example WebDAV services implemented using the .NET
WebDAV server from http://www.adammil.net. The included examples are:

* ZipFileService - This example demonstrates how to implement a full-featured
  WebDAV service that serves data from a .zip file. It supports the full set
  of WebDAV operations including creating, updating, and deleting files and
  directories, shared and exclusive locking, strongly typed dead properties,
  partial GETs, partial PUTs, conditional requests, and copying and moving
  data to/from other types of WebDAV services (such as the built-in
  FileSystemService). It also supports read-only operation. Most of the
  features listed require little or no code to support, since they're
  built into the AdamMil.WebDAV.Server framework.
* XmlService - This example demonstrates a read-only WebDAV service that
  serves hierarchical data from an .xml file. It supports strongly typed dead
  properties, partial GETs, conditional requests, and copying data to other
  types of WebDAV services (such as a ZipFileService or FileSystemService).
  This example exists to demonstrate how easily a read-only WebDAV service
  can be implemented.
