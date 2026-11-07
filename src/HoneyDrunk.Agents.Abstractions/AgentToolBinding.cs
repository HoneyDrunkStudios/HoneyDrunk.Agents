namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A tool an agent may call: a capability name at one version.</summary>
/// <param name="Name">The capability name.</param>
/// <param name="Version">The capability version.</param>
public sealed record AgentToolBinding(string Name, string Version)
{
    /// <summary>Gets the capability name.</summary>
    public string Name { get; } = AgentValidation.Text(Name, nameof(Name));

    /// <summary>Gets the capability version.</summary>
    public string Version { get; } = AgentValidation.Text(Version, nameof(Version));
}
