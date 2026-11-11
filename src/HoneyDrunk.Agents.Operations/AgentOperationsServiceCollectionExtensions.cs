using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Agents.Operations.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HoneyDrunk.Agents.Operations;

/// <summary>Registers the services behind the operations endpoints.</summary>
public static class AgentOperationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the operations options, the run journal and the lifecycle observer that feeds it. The journal is the
    /// in-memory one unless the host registered an <see cref="IAgentRunJournal"/> first. Call
    /// <c>AddHoneyDrunkAgents</c> as well; Operator and Audit services are optional, and a part of a response whose
    /// source is not registered is reported as unavailable.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options; <see cref="AgentOperationsOptions.Environment"/> is required.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAgentOperations(this IServiceCollection services, Action<AgentOperationsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<AgentOperationsOptions>()
            .Configure(configure)
            .Validate(
                static options => Identifiers.IsValid(options.Environment),
                "AgentOperationsOptions.Environment is required: 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentRunJournal>(static _ => new InMemoryAgentRunJournal());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentLifecycleObserver, RunJournalObserver>());
        return services;
    }
}
