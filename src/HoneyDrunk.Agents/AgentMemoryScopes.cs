using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Memory.Abstractions;

namespace HoneyDrunk.Agents;

internal static class AgentMemoryScopes
{
    internal static async Task<(IMemoryScope home, IMemoryScope read)> OpenAsync(
        IMemoryScopeResolver resolver,
        string agentId,
        AgentGrants grants,
        AgentMemoryBinding binding,
        CancellationToken cancellationToken)
    {
        var caller = new MemoryCaller($"agent:{agentId}", grants.Scopes, grants.Roles);
        var homeKey = new MemoryScopeKey(binding.Tenant, binding.Home, agentId);
        var home = resolver.ResolveAgentScope(homeKey, caller);
        var extra = binding.AlsoRead.Where(project => project != binding.Home)
            .Distinct()
            .Select(project => new MemoryScopeKey(binding.Tenant, project, agentId))
            .ToArray();
        var read = extra.Length == 0
            ? home
            : await home.EscalateAsync(extra, cancellationToken).ConfigureAwait(false);
        return (home, read);
    }
}
