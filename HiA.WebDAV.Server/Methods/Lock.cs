using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Collections.Generic;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/* Conditions:
 *  Success, item locked -- Status == 200 or 201, NewLock == new lock
 *  Failed because the item couldn't be created -- FailedMembers empty, Status == 409 (or whatever), NewLock == null
 *  Failed because the resource is directly locked -- FailedMembers empty, Status == 423, NewLock == null
 *  Failed because the resource is indirectly locked or a child resource has a lock -- FailedMembers containing the resources
 *  Failed because the lock submitted for refresh does not control the request URI -- Status == 412
 */

/// <summary>Represents a <c>LOCK</c> request.</summary>
/// <remarks>The <c>LOCK</c> request is described in section 9.6 of RFC 4918.</remarks>
public class LockRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="DeleteRequest"/> based on a new WebDAV request.</summary>
  public LockRequest(WebDAVContext context) : base(context)
  {
    // section 9.10.3 of RFC 4918 says a depth of infinity is the default, and a depth of 1 is disallowed
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants;
    else if(Depth == Depth.SelfAndChildren) throw Exceptions.BadRequest("The Depth header cannot be 1 for DELETE requests.");

    FailedResources = new FailedResourceCollection();

    // parse the Timeout header if specified
    string value = context.Request.Headers[HttpHeaders.Timeout];
    if(string.IsNullOrEmpty(value))
    {
      RequestedTimeouts = new ReadOnlyListWrapper<uint>(new uint[0]);
    }
    else
    {
      List<uint> requestedTimeouts = new List<uint>();
      foreach(string timeoutStr in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
      {
        if(timeoutStr.OrdinalEquals("Infinite"))
        {
          requestedTimeouts.Add(0);
        }
        else if(timeoutStr.StartsWith("Second-", StringComparison.Ordinal))
        {
          uint timeoutSeconds;
          if(uint.TryParse(timeoutStr.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out timeoutSeconds))
          {
            requestedTimeouts.Add(timeoutSeconds);
          }
        }
      }
      RequestedTimeouts = new ReadOnlyListWrapper<uint>(requestedTimeouts);
    }
  }

  /// <summary>Gets a collection that should be filled with <see cref="ResourceStatus"/> objects representing resources that prevented the
  /// request resource from being locked. A <see cref="ResourceStatus"/> object representing the request resource itself can also be added,
  /// but if that is the only resource with an error, it is better to represent the error by setting <see cref="WebDAVRequest.Status"/> and
  /// leaving the collection empty.
  /// </summary>
  public FailedResourceCollection FailedResources { get; private set; }

  /// <summary>Gets or sets an <see cref="ActiveLock"/> object representing the lock created or refreshed by the lock request. This should
  /// be set when a lock was successfully created or refreshed.
  /// </summary>
  public ActiveLock NewLock { get; set; }

  /// <summary>Gets a collection of lock timeout values requested by the client, from most to least preferred. The expected behavior is
  /// that the server should use the first timeout value it finds acceptable, but the server is not required to use any of them.
  /// If empty, no timeout value was suggested by the client, and the server should use a default timeout value (usually infinite).
  /// </summary>
  public ReadOnlyListWrapper<uint> RequestedTimeouts { get; private set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    XmlDocument xml = Context.LoadRequestXml();
    if(xml == null)
    {
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>This implementation writes a multi-status response if <see cref="FailedResources"/> is not empty, and outputs a
  /// response based on <see cref="WebDAVRequest.Status"/> otherwise.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedResources.Count == 0) Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    else Context.WriteFailedMembers(FailedResources);
  }
}

} // namespace HiA.WebDAV.Server
