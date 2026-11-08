using System.Globalization;
using System.Text.Json;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A tool call an agent proposes.</summary>
/// <param name="Name">The capability name.</param>
/// <param name="Version">The capability version.</param>
/// <param name="Arguments">The arguments; a JSON object, copied.</param>
public sealed record ToolCall(string Name, string Version, JsonElement Arguments)
{
    /// <summary>Gets the capability name.</summary>
    public string Name { get; } = AgentValidation.Text(Name, nameof(Name));

    /// <summary>Gets the capability version.</summary>
    public string Version { get; } = AgentValidation.Text(Version, nameof(Version));

    /// <summary>Gets a private copy of the arguments.</summary>
    public JsonElement Arguments { get; } = AgentValidation.JsonObject(Arguments, nameof(Arguments));

    /// <summary>Describes the call without its arguments.</summary>
    /// <returns>The tool name and version.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"ToolCall {{ Name = {this.Name}, Version = {this.Version} }}");
}
