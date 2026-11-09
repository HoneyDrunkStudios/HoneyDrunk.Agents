using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/workloads/{id}/runs/{runId}</c>: one finished run.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="Run">The run.</param>
public sealed record RunDetail(DateTimeOffset GeneratedAt, RunSummary Run)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
