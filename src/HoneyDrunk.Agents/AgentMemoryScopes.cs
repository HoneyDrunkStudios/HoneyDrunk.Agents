using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Memory.Abstractions;

namespace HoneyDrunk.Agents;

/// <summary>Opens one execution's memory scopes: the home scope, and a read scope widened by authorized escalation.</summary>
internal static class AgentMemoryScopes
{
    /// <summary>
    /// Resolves the home scope for the agent in the home project. When other projects are listed, escalates to
    /// them; a denied escalation throws <see cref="MemoryEscalationDeniedException"/>.
    /// </summary>
    /// <param name="resolver">The memory scope resolver.</param>
    /// <param name="identity">Who the execution acts as.</param>
    /// <param name="binding">The memory binding.</param>
    /// <param name="cancellationToken">Cancels the escalation.</param>
    /// <returns>The home scope and the read scope.</returns>
    public static async Task<(IMemoryScope home, IMemoryScope read)> OpenAsync(
        IMemoryScopeResolver resolver,
        ExecutionIdentity identity,
        AgentMemoryBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(binding);
        var caller = new MemoryCaller(identity.Subject, identity.Grants.Scopes, identity.Grants.Roles);
        var home = resolver.ResolveAgentScope(new MemoryScopeKey(binding.Tenant, binding.Home, identity.AgentId), caller);
        if (binding.AlsoRead.Count == 0)
        {
            return (home, home);
        }

        var keys = binding.AlsoRead.Select(project => new MemoryScopeKey(binding.Tenant, project, identity.AgentId)).ToArray();
        var read = await home.EscalateAsync(keys, cancellationToken).ConfigureAwait(false);
        return (home, read);
    }
}
