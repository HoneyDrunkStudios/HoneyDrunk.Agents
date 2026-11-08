using HoneyDrunk.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HoneyDrunk.Agents;

/// <summary>Registers the agent runtime.</summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Adds the in-process lifecycle and the approval ledger as singletons, and <see cref="TimeProvider.System"/>
    /// unless the host registered a clock. The host composes Kernel (grid context, agent context factory,
    /// telemetry), Capabilities, Memory, Operator and logging.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally configures the runtime.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHoneyDrunkAgents(this IServiceCollection services, Action<AgentRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<AgentRuntimeOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => new ToolApprovalLedger(provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IAgentLifecycle, InProcessAgentLifecycle>();
        return services;
    }
}
