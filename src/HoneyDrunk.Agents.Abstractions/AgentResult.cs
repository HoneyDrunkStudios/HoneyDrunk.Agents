using System.Globalization;
using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The result of one execution. It carries codes, never message text.</summary>
/// <param name="Outcome">How the execution ended.</param>
/// <param name="Output">The agent's output, if any; copied.</param>
/// <param name="ReasonCode">A stable reason code, if any.</param>
/// <param name="ApprovalId">The approval id a waiting tool call is keyed by, if any.</param>
public sealed record AgentResult(AgentOutcome Outcome, JsonElement? Output, string? ReasonCode, string? ApprovalId)
{
    /// <summary>Gets a private copy of the output.</summary>
    public JsonElement? Output { get; } = AgentValidation.Optional(Output);

    /// <summary>Describes the result without its output.</summary>
    /// <returns>The outcome, codes and whether output is present.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"AgentResult {{ Outcome = {this.Outcome}, HasOutput = {this.Output.HasValue}, ReasonCode = {this.ReasonCode}, ApprovalId = {this.ApprovalId} }}");
}
