using HoneyDrunk.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Service registration and the runtime's dependency direction.</summary>
public sealed class RegistrationTests
{
    /// <summary>The lifecycle resolves as a singleton with options applied.</summary>
    [Fact]
    public void Registers_resolvable_lifecycle()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHoneyDrunkAgents(options => options.MaxToolCallsPerExecution = 3);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var lifecycle = provider.GetRequiredService<IAgentLifecycle>();
        Assert.IsType<InProcessAgentLifecycle>(lifecycle);
        Assert.Same(lifecycle, provider.GetRequiredService<IAgentLifecycle>());
        Assert.Same(provider.GetRequiredService<ToolApprovalLedger>(), provider.GetRequiredService<ToolApprovalLedger>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>A host clock is kept.</summary>
    [Fact]
    public void Keeps_a_host_clock()
    {
        var clock = new MemoryFixture().Time;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHoneyDrunkAgents();

        using var provider = services.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>The runtime references no AI or Auth package (invariants 132 and 133).</summary>
    [Fact]
    public void Runtime_references_no_ai_or_auth_package()
    {
        var references = typeof(InProcessAgentLifecycle).Assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("HoneyDrunk.AI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("HoneyDrunk.Auth", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal));
        Assert.All(
            references.Where(name => name.StartsWith("HoneyDrunk.", StringComparison.Ordinal) && name != "HoneyDrunk.Agents.Abstractions"),
            name => Assert.EndsWith(".Abstractions", name, StringComparison.Ordinal));
    }
}
