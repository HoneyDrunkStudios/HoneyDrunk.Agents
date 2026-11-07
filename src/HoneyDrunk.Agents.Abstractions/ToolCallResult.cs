using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The result of an invocation within an execution.</summary>
/// <param name="Status">The disposition of the call.</param>
/// <param name="Output">The optional capability output.</param>
/// <param name="ErrorCode">The optional stable error code.</param>
/// <param name="ApprovalId">The optional approval reference.</param>
public sealed record ToolCallResult(
    ToolCallStatus Status,
    JsonElement? Output,
    string? ErrorCode,
    string? ApprovalId)
{
    /// <summary>Gets the optional capability output.</summary>
    public JsonElement? Output { get; } = Output?.Clone();

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => $"{nameof(ToolCallResult)} {{ Status = {this.Status} }}";
}
