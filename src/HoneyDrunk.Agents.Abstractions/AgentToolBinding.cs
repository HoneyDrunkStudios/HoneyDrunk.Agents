namespace HoneyDrunk.Agents.Abstractions;

/// <summary>An exact capability name and version that a host binds to an agent.</summary>
/// <param name="Name">The capability name.</param>
/// <param name="Version">The capability version.</param>
public sealed record AgentToolBinding(string Name, string Version)
{
    /// <summary>Gets the bound capability name.</summary>
    public string Name { get; } = AgentValidation.ToolName(Name, nameof(Name));

    /// <summary>Gets the bound capability version.</summary>
    public string Version { get; } = AgentValidation.ToolVersion(Version, nameof(Version));
}
