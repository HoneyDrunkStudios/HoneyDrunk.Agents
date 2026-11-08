using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>
/// Where one execution remembers and recalls. It writes only to the home project; reading the other projects
/// needs the memory node's escalation, which is authorized before the agent runs.
/// </summary>
/// <param name="Tenant">The tenant; must match the grid context's tenant.</param>
/// <param name="Home">The project the execution writes to and reads from.</param>
/// <param name="AlsoRead">Other projects the execution may read; distinct and never the home project.</param>
public sealed record AgentMemoryBinding(TenantId Tenant, ProjectId Home, IReadOnlyList<ProjectId> AlsoRead)
{
    /// <summary>Gets a private copy of the other readable projects.</summary>
    public IReadOnlyList<ProjectId> AlsoRead { get; } = Validate(AlsoRead, Home);

    private static IReadOnlyList<ProjectId> Validate(IReadOnlyList<ProjectId> alsoRead, ProjectId home)
    {
        ArgumentNullException.ThrowIfNull(alsoRead);
        if (alsoRead.Contains(home))
        {
            throw new ArgumentException("The home project is always readable and must not be listed again.", nameof(alsoRead));
        }

        if (alsoRead.Distinct().Count() != alsoRead.Count)
        {
            throw new ArgumentException("Each project may be listed once.", nameof(alsoRead));
        }

        return alsoRead.ToArray().AsReadOnly();
    }
}
