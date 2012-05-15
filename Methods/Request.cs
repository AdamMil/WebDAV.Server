namespace HiA.WebDAV
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

#region WebDAVRequest
/// <summary>Represents a WebDAV request.</summary>
public abstract class WebDAVRequest
{
  internal WebDAVRequest(WebDAVContext context)
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
  protected internal abstract void ParseRequest();

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  protected internal abstract void WriteResponse();
}
#endregion

} // namespace HiA.WebDAV