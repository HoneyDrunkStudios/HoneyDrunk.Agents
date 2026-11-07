using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A request to invoke one exact capability.</summary>
/// <param name="Name">The capability name.</param>
/// <param name="Version">The capability version.</param>
/// <param name="Arguments">The JSON object to pass to the capability.</param>
public sealed record ToolCall(string Name, string Version, JsonElement Arguments)
{
    /// <summary>Gets the capability name.</summary>
    public string Name { get; } = AgentValidation.ToolName(Name, nameof(Name));

    /// <summary>Gets the capability version.</summary>
    public string Version { get; } = AgentValidation.ToolVersion(Version, nameof(Version));

    /// <summary>Gets the copied argument object.</summary>
    public JsonElement Arguments { get; } = Arguments.ValueKind == JsonValueKind.Object
        ? Arguments.Clone()
        : throw new ArgumentException("Tool arguments must be a JSON object.", nameof(Arguments));

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => nameof(ToolCall);
}
