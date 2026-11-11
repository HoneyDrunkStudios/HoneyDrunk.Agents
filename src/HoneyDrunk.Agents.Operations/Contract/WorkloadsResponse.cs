using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/workloads</c>.</summary>
/// <param name="GeneratedAt">When the response was built, so a consumer can judge staleness.</param>
/// <param name="Workloads">The first 20 agents by id.</param>
public sealed record WorkloadsResponse(DateTimeOffset GeneratedAt, IReadOnlyList<WorkloadSummary> Workloads)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
