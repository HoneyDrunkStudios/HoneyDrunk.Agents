using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Checks the dependency boundary of the consumer assembly.</summary>
public sealed class DependencyTests
{
    /// <summary>Only Kernel.Abstractions is allowed outside the framework.</summary>
    [Fact]
    public void Abstractions_References_OnlyFrameworkAndKernel()
    {
        var external = typeof(IAgent).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name != "netstandard" && !name.StartsWith("System.", StringComparison.Ordinal));

        Assert.Equal("HoneyDrunk.Kernel.Abstractions", Assert.Single(external));
    }

    /// <summary>The context preserves the original Kernel execution contract.</summary>
    [Fact]
    public void ExecutionContext_Extends_KernelContext() =>
        Assert.Contains(typeof(Kernel.Abstractions.Agents.IAgentExecutionContext), typeof(IAgentExecutionContext).GetInterfaces());
}
