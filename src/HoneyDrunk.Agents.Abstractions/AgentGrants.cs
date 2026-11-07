namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The scopes and roles issued by the registering host.</summary>
/// <param name="Scopes">The granted scopes.</param>
/// <param name="Roles">The granted roles.</param>
public sealed record AgentGrants(IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles)
{
    /// <summary>Gets grants with no scopes or roles.</summary>
    public static AgentGrants None { get; } = new([], []);

    /// <summary>Gets the granted scopes.</summary>
    public IReadOnlyList<string> Scopes { get; } = AgentValidation.NonBlankList(Scopes, nameof(Scopes));

    /// <summary>Gets the granted roles.</summary>
    public IReadOnlyList<string> Roles { get; } = AgentValidation.NonBlankList(Roles, nameof(Roles));

    /// <summary>Restricts these grants to their intersection with another set.</summary>
    /// <param name="narrowing">The allowed subset. A null value keeps these grants.</param>
    /// <returns>The effective grants.</returns>
    public AgentGrants Narrow(AgentGrants? narrowing) => narrowing is null
        ? this
        : new AgentGrants(
            this.Scopes.Intersect(narrowing.Scopes, StringComparer.Ordinal).ToArray(),
            this.Roles.Intersect(narrowing.Roles, StringComparer.Ordinal).ToArray());
}
