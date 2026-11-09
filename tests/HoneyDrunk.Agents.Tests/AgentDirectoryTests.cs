using HoneyDrunk.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>The read-only directory of the agents registered in this process.</summary>
public sealed class AgentDirectoryTests
{
    /// <summary>Agents are listed by id, ordinal, with their name and phase.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Lists_registered_agents_by_id_with_name_and_phase()
    {
        using var fixture = new LifecycleFixture();
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(agentId: "b"), AgentGrants.None, []));
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(agentId: "a"), AgentGrants.None, []));
        await fixture.Lifecycle.InitializeAsync("a");

        var listed = ((IAgentDirectory)fixture.Lifecycle).List();

        Assert.Equal(
            [new AgentListing("a", "Test", AgentLifecyclePhase.Initialized), new AgentListing("b", "Test", AgentLifecyclePhase.Registered)],
            listed);
    }

    /// <summary>A decommissioned agent stays listed, with its phase.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Decommissioned_agent_stays_listed()
    {
        using var fixture = new LifecycleFixture();
        fixture.Lifecycle.Register(new AgentRegistration(new TestAgent(agentId: "a"), AgentGrants.None, []));
        await fixture.Lifecycle.DecommissionAsync("a");

        var listed = Assert.Single(((IAgentDirectory)fixture.Lifecycle).List());

        Assert.Equal(AgentLifecyclePhase.Decommissioned, listed.Phase);
    }

    /// <summary>The directory resolves as the same singleton as the lifecycle.</summary>
    [Fact]
    public void Directory_is_the_lifecycle_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHoneyDrunkAgents();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Same(provider.GetRequiredService<IAgentLifecycle>(), provider.GetRequiredService<IAgentDirectory>());
    }

    /// <summary>A lifecycle with no agents lists nothing.</summary>
    [Fact]
    public void Empty_directory_lists_nothing()
    {
        using var fixture = new LifecycleFixture();

        Assert.Empty(((IAgentDirectory)fixture.Lifecycle).List());
    }
}
