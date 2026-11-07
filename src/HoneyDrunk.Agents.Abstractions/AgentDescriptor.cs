using HoneyDrunk.Kernel.Abstractions.Agents;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>
/// Describes an agent. Tool rights live in capability descriptors and <see cref="AgentToolBinding"/>, never in
/// Kernel's capability list, so <see cref="Capabilities"/> is always empty.
/// </summary>
/// <param name="AgentId">The agent id: 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.</param>
/// <param name="Name">The display name.</param>
/// <param name="AgentType">The agent type.</param>
/// <param name="Version">The agent version.</param>
public sealed record AgentDescriptor(string AgentId, string Name, string AgentType, string Version) : IAgentDescriptor
{
    private static readonly IReadOnlyList<IAgentCapability> NoCapabilities = Array.Empty<IAgentCapability>();

    private static readonly IReadOnlyDictionary<string, string> NoMetadata = new Dictionary<string, string>(StringComparer.Ordinal).AsReadOnly();

    /// <summary>Gets the agent id.</summary>
    public string AgentId { get; } = AgentValidation.AgentId(AgentId, nameof(AgentId));

    /// <summary>Gets the display name.</summary>
    public string Name { get; } = AgentValidation.Text(Name, nameof(Name));

    /// <summary>Gets the agent type.</summary>
    public string AgentType { get; } = AgentValidation.Text(AgentType, nameof(AgentType));

    /// <summary>Gets the agent version.</summary>
    public string Version { get; } = AgentValidation.Text(Version, nameof(Version));

    /// <summary>Gets Kernel's capability list, which is always empty.</summary>
    public IReadOnlyList<IAgentCapability> Capabilities => NoCapabilities;

    /// <summary>Gets the context scope, which is always <see cref="AgentContextScope.Standard"/>.</summary>
    public AgentContextScope ContextScope => AgentContextScope.Standard;

    /// <summary>Gets the metadata, which is always empty.</summary>
    public IReadOnlyDictionary<string, string> Metadata => NoMetadata;

    /// <summary>Always false: tool rights are not Kernel capabilities.</summary>
    /// <param name="capabilityName">The capability name.</param>
    /// <returns>False.</returns>
    public bool HasCapability(string capabilityName)
    {
        ArgumentNullException.ThrowIfNull(capabilityName);
        return false;
    }
}
