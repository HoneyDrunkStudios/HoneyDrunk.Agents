using System.Globalization;
using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>What an execution is asked to do.</summary>
/// <param name="Kind">The request kind.</param>
/// <param name="Payload">The request payload; copied.</param>
public sealed record AgentRequest(string Kind, JsonElement Payload)
{
    /// <summary>Gets the request kind.</summary>
    public string Kind { get; } = AgentValidation.Text(Kind, nameof(Kind));

    /// <summary>Gets a private copy of the payload.</summary>
    public JsonElement Payload { get; } = AgentValidation.Defined(Payload, nameof(Payload));

    /// <summary>Describes the request without its payload.</summary>
    /// <returns>The kind and the payload's JSON kind.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"AgentRequest {{ Kind = {this.Kind}, Payload = {this.Payload.ValueKind} }}");
}
