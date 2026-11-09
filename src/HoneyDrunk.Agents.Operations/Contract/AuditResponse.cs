using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/workloads/{id}/audit</c>.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="State">Whether an audit source answered. When unavailable, the list is empty and means nothing.</param>
/// <param name="Entries">The last 7 days of entries about the agent, newest first, at most 50.</param>
public sealed record AuditResponse(DateTimeOffset GeneratedAt, SectionState State, IReadOnlyList<AuditEntryView> Entries)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
