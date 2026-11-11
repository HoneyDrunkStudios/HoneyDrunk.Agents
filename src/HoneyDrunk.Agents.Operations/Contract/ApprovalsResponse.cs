using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/approvals</c>.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="State">Whether the approval desk answered. When unavailable, the list is empty and means nothing.</param>
/// <param name="Approvals">The pending approvals for agents this host lists, newest first.</param>
/// <param name="Truncated">
/// Whether the approval desk returned a full page. The desk lists the newest pending approvals for every subject, so
/// when its page is full an older approval for a listed agent may be missing, and an empty list is not "nothing is
/// waiting".
/// </param>
public sealed record ApprovalsResponse(DateTimeOffset GeneratedAt, SectionState State, IReadOnlyList<PendingApprovalView> Approvals, bool Truncated)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
