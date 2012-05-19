using System;
using System.Collections.Generic;
using System.Xml;

// TODO: add processing examples and documentation
// TODO: add support for conditional requests (e.g. If-Match, etc. headers)

namespace HiA.WebDAV.Server
{

#region Depth
/// <summary>Represents the recursion depth requested by the client.</summary>
public enum Depth
{
  /// <summary>The request did not specify a recursion depth, and there is no default defined by the WebDAV specification.</summary>
  Unspecified,
  /// <summary>The request should apply only to the resource named by the request URI.</summary>
  Self,
  /// <summary>The request should apply to the resource named by the request URI and its immediate children.</summary>
  SelfAndChildren,
  /// <summary>The request should apply to the resource named by the request URI and all of its descendants.</summary>
  SelfAndDescendants
}
#endregion

#region PropertyNameSet
/// <summary>A read-only collection of property names referenced by the client.</summary>
public sealed class PropertyNameSet : AccessLimitedCollectionBase<XmlQualifiedName>
{
  internal PropertyNameSet() { }

  /// <inheritdoc/>
  public override bool IsReadOnly
  {
    get { return true; }
  }

  /// <inheritdoc/>
  public new bool Contains(XmlQualifiedName qname)
  {
    return qname != null && names != null && names.Contains(qname);
  }

  internal void Add(XmlQualifiedName qname)
  {
    if(qname == null || qname.IsEmpty) throw new ArgumentException("The name must not be null or empty.");
    if(names == null) names = new HashSet<XmlQualifiedName>();
    if(!names.Add(qname)) throw Exceptions.BadRequest("Duplicate property name " + qname.ToString());
    Items.Add(qname);
  }

  internal void AddRange(IEnumerable<XmlQualifiedName> names)
  {
    foreach(XmlQualifiedName name in names) Add(name);
  }

  internal static readonly PropertyNameSet Empty = new PropertyNameSet();

  HashSet<XmlQualifiedName> names;
}
#endregion

#region ResourceStatus
/// <summary>Represents a path to a resource and a <see cref="ConditionCode"/> describing the status of the resource, in the context of
/// some operation.
/// </summary>
public sealed class ResourceStatus
{
  /// <summary>Initializes a new <see cref="ResourceStatus"/>, given the absolute path to the resource and the status of the resource.</summary>
  public ResourceStatus(string absolutePath, ConditionCode status)
  {
    if(string.IsNullOrEmpty(absolutePath)) throw new ArgumentException("The resource path cannot be null or empty.");
    if(status == null) throw new ArgumentNullException();
    AbsolutePath = absolutePath;
    Status       = status;
  }

  /// <summary>Gets the absolute path to the resource.</summary>
  public string AbsolutePath { get; private set; }

  /// <summary>Gets the status of the resource.</summary>
  public ConditionCode Status { get; private set; }
}
#endregion

#region WebDAVRequest
/// <summary>Represents a WebDAV request.</summary>
public abstract class WebDAVRequest
{
  /// <summary>Initializes a new <see cref="WebDAVRequest"/> based on a new WebDAV request.</summary>
  protected WebDAVRequest(WebDAVContext context)
  {
    Context    = context;
    MethodName = context.Request.HttpMethod;

    string depth = context.Request.Headers["Depth"];
    if(depth != null)
    {
      if("0".OrdinalEquals(depth)) Depth = Depth.Self;
      else if("1".OrdinalEquals(depth)) Depth = Depth.SelfAndChildren;
      else if("infinity".OrdinalEquals(depth)) Depth = Depth.SelfAndDescendants;
      else throw Exceptions.BadRequest("The Depth header must be 0, 1, or infinity.");
    }
  }

  /// <summary>Gets the <see cref="WebDAVContext"/> in which the request is being executed.</summary>
  public WebDAVContext Context { get; private set; }

  /// <summary>Gets the recursion depth requested by the client.</summary>
  public Depth Depth { get; protected set; }

  /// <summary>Gets the HTTP method name sent by the client.</summary>
  public string MethodName { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  /// <remarks>The default implementation does nothing.</remarks>
  protected internal virtual void ParseRequest() { }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal abstract void WriteResponse();
}
#endregion

#region SimpleRequest
/// <summary>Represents a simple WebDAV request that doesn't need any special parameters.</summary>
public abstract class SimpleRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="SimpleRequest"/> based on a new WebDAV request.</summary>
  protected SimpleRequest(WebDAVContext context) : base(context) { }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> representing the overall result of the request. If the status is null, the
  /// request is assumed to have been successful and <see cref="ConditionCodes.NoContent"/> is used.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>The default implementation sets the response status based on <see cref="Status"/>, using
  /// <see cref="ConditionCodes.NoContent"/> if the status is null.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
  }
}
#endregion

} // namespace HiA.WebDAV.Server
