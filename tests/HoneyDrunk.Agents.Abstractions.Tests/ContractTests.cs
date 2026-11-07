using HoneyDrunk.Agents.Abstractions.Tests.ContractShape;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>How the contracts relate to Kernel and to the canary.</summary>
public sealed class ContractTests
{
    /// <summary>The Agents context is Kernel's agent context plus execution-scoped bindings.</summary>
    [Fact]
    public void Context_extends_the_kernel_context()
    {
        Assert.Contains(typeof(HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext), typeof(IAgentExecutionContext).GetInterfaces());
    }

    /// <summary>The lifecycle and its records can change without a canary bump (ADR-0020 D10).</summary>
    [Fact]
    public void Lifecycle_is_outside_the_frozen_closure()
    {
        var shape = ContractShapeRenderer.Render(ContractShapeTests.Roots);

        Assert.DoesNotContain(nameof(IAgentLifecycle), shape, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AgentRegistration), shape, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AgentExecutionOptions), shape, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AgentLifecyclePhase), shape, StringComparison.Ordinal);
    }

    /// <summary>The frozen closure reaches the records the contracts use.</summary>
    [Fact]
    public void Closure_reaches_the_contract_records()
    {
        var shape = ContractShapeRenderer.Render(ContractShapeTests.Roots);

        Assert.Contains("type HoneyDrunk.Agents.Abstractions.ToolCallResult:", shape, StringComparison.Ordinal);
        Assert.Contains("type HoneyDrunk.Agents.Abstractions.AgentMemoryItem:", shape, StringComparison.Ordinal);
        Assert.Contains("HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext", shape, StringComparison.Ordinal);
    }
}
