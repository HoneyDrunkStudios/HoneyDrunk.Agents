using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/approvals</c>.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="State">Whether the approval desk answered. When unavailable, the list is empty and means nothing.</param>
/// <param name="Approvals">The pending approvals for agents this host lists, newest first.</param>
public sealed record ApprovalsResponse(DateTimeOffset GeneratedAt, SectionState State, IReadOnlyList<PendingApprovalView> Approvals)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
