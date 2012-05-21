using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Collections.Generic;

// TODO: add processing examples and documentation

namespace HiA.WebDAV.Server
{

/// <summary>Represents a <c>DELETE</c> request.</summary>
/// <remarks>The <c>DELETE</c> request is described in section 9.6 of RFC 4918.</remarks>
public class DeleteRequest : WebDAVRequest
{
  /// <summary>Initializes a new <see cref="DeleteRequest"/> based on a new WebDAV request.</summary>
  public DeleteRequest(WebDAVContext context) : base(context)
  {
    if(Depth == Depth.Unspecified) Depth = Depth.SelfAndDescendants; // see ParseRequest() for details
    FailedMembers = new FailedMemberCollection();
  }

  #region FailedMemberCollection
  /// <summary>A collection of <see cref="ResourceStatus"/> objects representing internal collection members that could not be deleted.</summary>
  public sealed class FailedMemberCollection : CollectionBase<ResourceStatus>
  {
    internal FailedMemberCollection() { }

    /// <summary>Adds a new <see cref="ResourceStatus"/> to the collection, given the absolute path to the resource and the status of the
    /// resource.
    /// </summary>
    public void Add(string absolutePath, ConditionCode status)
    {
      Add(new ResourceStatus(absolutePath, status));
    }
  }
  #endregion

  /// <summary>Gets a collection of <see cref="ResourceStatus"/> objects that should be filled with the members of the collection that
  /// could not be deleted, if the resource is a collection resource.
  /// </summary>
  public FailedMemberCollection FailedMembers { get; private set; }

  /// <summary>Gets or sets the <see cref="ConditionCode"/> representing the overall result of the deletion request. If
  /// <see cref="FailedMembers"/> is not empty, a 207 Multi-Status response will be sent instead of using the status from this property.
  /// Otherwise, if the status is null, the deletion is assumed to have been successful and <see cref="ConditionCodes.NoContent"/> is used.
  /// </summary>
  public ConditionCode Status { get; set; }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/ParseRequest/node()" />
  protected internal override void ParseRequest()
  {
    // require recursive DELETE requests, as per section 9.6.1 of RFC 4918
    if(Depth != Depth.SelfAndDescendants)
    {
      throw Exceptions.BadRequest("The Depth header must be infinity or unspecified for DELETE requests.");
    }
  }

  /// <include file="documentation.xml" path="/DAV/WebDAVRequest/WriteResponse/node()" />
  /// <remarks>The default implementation writes a multi-status response if <see cref="FailedMembers"/> is not empty, and outputs a
  /// response based on <see cref="Status"/> otherwise.
  /// </remarks>
  protected internal override void WriteResponse()
  {
    if(FailedMembers.Count == 0)
    {
      Context.WriteStatusResponse(Status ?? ConditionCodes.NoContent);
    }
    else
    {
      using(MultiStatusResponse response = Context.OpenMultiStatusResponse(null))
      {
        foreach(ResourceStatus member in FailedMembers)
        {
          response.Writer.WriteStartElement(Names.response.Name);
          response.Writer.WriteElementString(Names.href.Name, member.AbsolutePath);
          response.WriteStatus(member.Status);
          response.Writer.WriteEndElement();
        }
      }
    }
  }
}

} // namespace HiA.WebDAV.Server
