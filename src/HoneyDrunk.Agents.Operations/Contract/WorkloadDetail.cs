using System.Text.Json.Serialization;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The response of <c>GET /v1/workloads/{id}</c>.</summary>
/// <param name="GeneratedAt">When the response was built.</param>
/// <param name="Id">The agent id.</param>
/// <param name="DisplayName">The agent's name.</param>
/// <param name="Environment">The host's environment identifier.</param>
/// <param name="Phase">The lifecycle phase: <c>registered</c>, <c>initialized</c> or <c>decommissioned</c>.</param>
/// <param name="Pause">The agent's manual pause; an available section with no value means it is not paused.</param>
/// <param name="Budget">The agent's spend in the current monthly window.</param>
/// <param name="SupportedActions">The actions this host accepts for the agent. Always empty in this version.</param>
public sealed record WorkloadDetail(
    DateTimeOffset GeneratedAt,
    string Id,
    string DisplayName,
    string Environment,
    string Phase,
    Section<PauseView> Pause,
    Section<BudgetView> Budget,
    IReadOnlyList<string> SupportedActions)
{
    /// <summary>Gets the contract's schema version.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion => OperationsJson.SchemaVersion;
}
