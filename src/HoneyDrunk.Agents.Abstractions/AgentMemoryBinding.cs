using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The home project and any additional projects the host requests to read within the same tenant.</summary>
/// <param name="Tenant">The tenant that owns the projects.</param>
/// <param name="Home">The project's home scope for reads and writes.</param>
/// <param name="AlsoRead">Other project scopes to request read access to.</param>
public sealed record AgentMemoryBinding(TenantId Tenant, ProjectId Home, IReadOnlyList<ProjectId> AlsoRead)
{
    /// <summary>Gets the requested additional projects. The runtime checks authorization.</summary>
    public IReadOnlyList<ProjectId> AlsoRead { get; } = Array.AsReadOnly(
        (AlsoRead ?? throw new ArgumentNullException(nameof(AlsoRead))).ToArray());
}
