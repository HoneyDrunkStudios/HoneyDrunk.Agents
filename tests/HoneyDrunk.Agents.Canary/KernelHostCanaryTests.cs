using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Kernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoneyDrunk.Agents.Canary;

/// <summary>
/// The runtime under Kernel's real node registration (<c>AddHoneyDrunkNode</c>), as a background host runs it: the host
/// opens a scope, initializes that scope's grid context and sets it on <see cref="ScopedGridContextAccessor"/>.
/// </summary>
public sealed class KernelHostCanaryTests
{
    private static readonly AgentGrants Granted = new([CanaryGrid.Scope], []);

    /// <summary>An execution runs, with the host's grid context, even though its own scope's grid context is new.</summary>
    [Fact]
    public async Task Kernel_hosted_execution_runs_with_the_host_context()
    {
        using var grid = new CanaryGrid(kernelHost: true);
        ToolCallResult? tool = null;
        var agent = new ScriptedAgent(CanaryGrid.AgentId, async (context, token) =>
        {
            tool = await context.Tools.InvokeAsync(CanaryGrid.ToolCall(), token);
            await context.Memory.RememberAsync(new AgentMemoryNote("note", "hello", []), token);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        AgentResult result;
        await using (var hostScope = grid.Services.CreateAsyncScope())
        {
            var hostGrid = hostScope.ServiceProvider.GetRequiredService<GridContext>();
            hostGrid.Initialize("corr-host", tenantId: CanaryGrid.Tenant);
            using (ScopedGridContextAccessor.SetContext(hostGrid))
            {
                result = await grid.RunAsync(agent, Granted);
            }
        }

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        Assert.Equal(ToolCallStatus.Succeeded, tool!.Status);
        var context = Assert.Single(agent.Contexts);
        Assert.Equal("corr-host", context.GridContext.CorrelationId);
        Assert.Equal("corr-host", context.OperationContext.CorrelationId);
    }

    /// <summary>Without a host context, the execution reports it instead of throwing.</summary>
    [Fact]
    public async Task Kernel_hosted_execution_without_context_reports_missing()
    {
        using var grid = new CanaryGrid(kernelHost: true);
        var agent = new ScriptedAgent(CanaryGrid.AgentId);

        var result = await grid.RunAsync(agent, Granted);

        Assert.Equal(AgentOutcome.Failed, result.Outcome);
        Assert.Equal("grid_context_missing", result.ReasonCode);
        Assert.Empty(agent.Contexts);
    }
}
