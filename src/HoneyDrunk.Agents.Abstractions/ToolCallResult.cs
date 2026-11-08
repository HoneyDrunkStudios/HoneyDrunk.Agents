using System.Globalization;
using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The result of one tool call. It carries codes, never message text.</summary>
/// <param name="Status">How the call ended.</param>
/// <param name="Output">The tool's output, if any; copied.</param>
/// <param name="ErrorCode">A stable error code, if any.</param>
/// <param name="ApprovalId">The approval id the call waits on, if any.</param>
public sealed record ToolCallResult(ToolCallStatus Status, JsonElement? Output, string? ErrorCode, string? ApprovalId)
{
    /// <summary>Gets a private copy of the output.</summary>
    public JsonElement? Output { get; } = AgentValidation.Optional(Output);

    /// <summary>Describes the result without its output.</summary>
    /// <returns>The status, codes and whether output is present.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"ToolCallResult {{ Status = {this.Status}, HasOutput = {this.Output.HasValue}, ErrorCode = {this.ErrorCode}, ApprovalId = {this.ApprovalId} }}");
}
