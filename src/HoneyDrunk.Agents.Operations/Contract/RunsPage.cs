using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/workloads/{id}/runs</c>: one page of finished runs, newest first.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="Runs">The page.</param>
/// <param name="NextCursor">The <c>before</c> value for the next page; null when this page was not full.</param>
public sealed record RunsPage(DateTimeOffset GeneratedAt, IReadOnlyList<RunSummary> Runs, string? NextCursor)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
