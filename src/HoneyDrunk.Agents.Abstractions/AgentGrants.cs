namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The scopes and roles an agent holds. Every tool and memory caller is built from these.</summary>
/// <param name="Scopes">The granted scopes.</param>
/// <param name="Roles">The granted roles.</param>
public sealed record AgentGrants(IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles)
{
    /// <summary>Gets grants that hold nothing.</summary>
    public static AgentGrants None { get; } = new([], []);

    /// <summary>Gets the granted scopes.</summary>
    public IReadOnlyList<string> Scopes { get; } = AgentValidation.NonBlankList(Scopes, nameof(Scopes));

    /// <summary>Gets the granted roles.</summary>
    public IReadOnlyList<string> Roles { get; } = AgentValidation.NonBlankList(Roles, nameof(Roles));

    /// <summary>
    /// Narrows these grants to those also named in <paramref name="narrowing"/> (ordinal intersection). The result
    /// is never wider than these grants.
    /// </summary>
    /// <param name="narrowing">The narrowing, or null to keep these grants.</param>
    /// <returns>The effective grants.</returns>
    public AgentGrants Narrow(AgentGrants? narrowing) =>
        narrowing is null
            ? this
            : new(
                this.Scopes.Where(scope => narrowing.Scopes.Contains(scope, StringComparer.Ordinal)).ToArray(),
                this.Roles.Where(role => narrowing.Roles.Contains(role, StringComparer.Ordinal)).ToArray());
}
