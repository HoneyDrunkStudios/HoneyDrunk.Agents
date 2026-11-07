using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>One request to an agent.</summary>
/// <param name="Kind">The workload-defined request kind.</param>
/// <param name="Payload">The request JSON, which is copied.</param>
public sealed record AgentRequest(string Kind, JsonElement Payload)
{
    /// <summary>Gets the request kind.</summary>
    public string Kind { get; } = AgentValidation.NonBlank(Kind, nameof(Kind));

    /// <summary>Gets the request JSON.</summary>
    public JsonElement Payload { get; } = Payload.ValueKind != JsonValueKind.Undefined
        ? Payload.Clone()
        : throw new ArgumentException("The request payload must be JSON.", nameof(Payload));

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => nameof(AgentRequest);
}
