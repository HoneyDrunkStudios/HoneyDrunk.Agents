using HoneyDrunk.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HoneyDrunk.Agents;

/// <summary>Registers the in-process agent execution services.</summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>Adds the agent lifecycle. The workload host composes the supporting Grid nodes.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional host runtime settings.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHoneyDrunkAgents(this IServiceCollection services, Action<AgentRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new AgentRuntimeOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options.Snapshot());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new ToolApprovalLedger(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IAgentLifecycle>(sp => new InProcessAgentLifecycle(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ToolApprovalLedger>(),
            sp.GetServices<IAgentLifecycleObserver>(),
            sp.GetRequiredService<AgentRuntimeOptions>().Snapshot(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
