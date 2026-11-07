using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Operator.Abstractions;
using Xunit;

namespace HoneyDrunk.Agents.Canary;

/// <summary>The runtime against real Kernel, Capabilities, Memory and Auth behavior (repo integration-points canary list).</summary>
public sealed class GridCanaryTests
{
    private static readonly AgentGrants Granted = new([CanaryGrid.Scope], []);

    /// <summary>Kernel's grid context and descriptor reach the agent unchanged.</summary>
    [Fact]
    public async Task Kernel_context_flows_into_the_agent_context()
    {
        using var grid = new CanaryGrid();
        var agent = new ScriptedAgent(CanaryGrid.AgentId);

        var result = await grid.RunAsync(agent, Granted);

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        var context = Assert.Single(agent.Contexts);
        Assert.Equal("corr-canary", context.GridContext.CorrelationId);
        Assert.Same(grid.GridContext, context.GridContext);
        Assert.Same(agent.Descriptor, context.Agent);
    }

    /// <summary>A bound, registered tool is invocable through the real Auth guard.</summary>
    [Fact]
    public async Task Registered_tool_is_discoverable_and_invocable()
    {
        using var grid = new CanaryGrid();
        ToolCallResult? tool = null;
        var agent = new ScriptedAgent(CanaryGrid.AgentId, async (context, token) =>
        {
            tool = await context.Tools.InvokeAsync(CanaryGrid.ToolCall(), token);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        await grid.RunAsync(agent, Granted);

        Assert.Equal(ToolCallStatus.Succeeded, tool!.Status);
        Assert.Single(grid.Handler.Invocations);
        Assert.Contains(grid.Audit.Entries, entry => entry.Outcome == AuditOutcome.Succeeded && entry.Target.Id == "capability/test-tool/1.0.0");
    }

    /// <summary>A narrowing that drops the needed scope is denied by real Auth.</summary>
    [Fact]
    public async Task Narrowed_grants_are_denied_by_real_auth()
    {
        using var grid = new CanaryGrid();
        ToolCallResult? tool = null;
        var agent = new ScriptedAgent(CanaryGrid.AgentId, async (context, token) =>
        {
            tool = await context.Tools.InvokeAsync(CanaryGrid.ToolCall(), token);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        await grid.RunAsync(agent, Granted, new AgentExecutionOptions(new AgentMemoryBinding(CanaryGrid.Tenant, CanaryGrid.Home, []), AgentGrants.None));

        Assert.Equal(ToolCallStatus.Denied, tool!.Status);
        Assert.Equal("MissingScope", tool.ErrorCode);
        Assert.Empty(grid.Handler.Invocations);
    }

    /// <summary>Memory writes and reads go through the real scope checks.</summary>
    [Fact]
    public async Task Agent_memory_writes_and_reads_scoped_entries()
    {
        using var grid = new CanaryGrid();
        IReadOnlyList<AgentMemoryItem>? recalled = null;
        var agent = new ScriptedAgent(CanaryGrid.AgentId, async (context, token) =>
        {
            await context.Memory.RememberAsync(new AgentMemoryNote("note", "hello", ["canary"]), token);
            recalled = await context.Memory.RecallAsync(new AgentRecall(AllTags: ["canary"]), token);
            return new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        await grid.RunAsync(agent, Granted);

        var item = Assert.Single(recalled!);
        Assert.Equal("hello", item.Content);
        Assert.True(item.FromHomeScope);
    }

    /// <summary>Reading another project without Auth's read scope rejects the execution before it runs.</summary>
    [Fact]
    public async Task Escalation_without_scope_rejects_execution()
    {
        using var grid = new CanaryGrid();
        var agent = new ScriptedAgent(CanaryGrid.AgentId);

        var result = await grid.RunAsync(agent, Granted, new AgentExecutionOptions(new AgentMemoryBinding(CanaryGrid.Tenant, CanaryGrid.Home, [CanaryGrid.Other])));

        Assert.Equal(AgentOutcome.Rejected, result.Outcome);
        Assert.Equal("memory_escalation_denied", result.ReasonCode);
        Assert.Empty(agent.Contexts);
        Assert.Contains(grid.Audit.Entries, entry => entry.Outcome == AuditOutcome.Denied);
    }

    /// <summary>A required, pending approval stops the tool call.</summary>
    [Fact]
    public async Task Approval_gate_halts_execution_when_required()
    {
        using var grid = new CanaryGrid();
        grid.Policy.Outcome = PolicyOutcome.RequireApproval;
        grid.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        var agent = new ScriptedAgent(CanaryGrid.AgentId, async (context, token) =>
        {
            var tool = await context.Tools.InvokeAsync(CanaryGrid.ToolCall(), token);
            return tool.Status == ToolCallStatus.AwaitingApproval
                ? new AgentResult(AgentOutcome.AwaitingApproval, null, tool.ErrorCode, tool.ApprovalId)
                : new AgentResult(AgentOutcome.Completed, null, null, null);
        });

        var result = await grid.RunAsync(agent, Granted);

        Assert.Equal(AgentOutcome.AwaitingApproval, result.Outcome);
        Assert.NotNull(result.ApprovalId);
        Assert.Empty(grid.Handler.Invocations);
        Assert.Single(grid.Gate.ReceivedRequests);
    }
}
