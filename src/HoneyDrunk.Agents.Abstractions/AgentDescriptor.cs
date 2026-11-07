using HoneyDrunk.Kernel.Abstractions.Agents;
using System.Collections.ObjectModel;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Identifies an agent and its Grid context visibility.</summary>
/// <param name="AgentId">The stable registration ID.</param>
/// <param name="Name">The display name.</param>
/// <param name="AgentType">The agent category.</param>
/// <param name="Version">The agent version.</param>
public sealed record AgentDescriptor(string AgentId, string Name, string AgentType, string Version) : IAgentDescriptor
{
    /// <inheritdoc />
    public string AgentId { get; } = AgentValidation.AgentId(AgentId, nameof(AgentId));

    /// <inheritdoc />
    public string Name { get; } = AgentValidation.NonBlank(Name, nameof(Name));

    /// <inheritdoc />
    public string AgentType { get; } = AgentValidation.NonBlank(AgentType, nameof(AgentType));

    /// <inheritdoc />
    public string Version { get; } = AgentValidation.NonBlank(Version, nameof(Version));

    /// <inheritdoc />
    public IReadOnlyList<IAgentCapability> Capabilities => [];

    /// <inheritdoc />
    public AgentContextScope ContextScope => AgentContextScope.Standard;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Metadata => ReadOnlyDictionary<string, string>.Empty;

    /// <inheritdoc />
    public bool HasCapability(string capabilityName) => false;
}
