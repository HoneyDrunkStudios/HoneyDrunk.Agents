using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>An agent's result. Hosts decide which fields may leave the workload.</summary>
/// <param name="Outcome">The disposition of the execution.</param>
/// <param name="Output">The optional workload output.</param>
/// <param name="ReasonCode">The optional stable outcome code.</param>
/// <param name="ApprovalId">The optional approval reference.</param>
public sealed record AgentResult(
    AgentOutcome Outcome,
    JsonElement? Output,
    string? ReasonCode,
    string? ApprovalId)
{
    /// <summary>Gets the optional workload output.</summary>
    public JsonElement? Output { get; } = Output?.Clone();

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => $"{nameof(AgentResult)} {{ Outcome = {this.Outcome} }}";
}
