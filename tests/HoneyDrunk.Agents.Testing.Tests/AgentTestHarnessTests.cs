using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Operator.Abstractions;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Testing.Tests;

/// <summary>The harness runs real executions, and the in-memory fixtures behave as documented.</summary>
public sealed class AgentTestHarnessTests
{
    private static readonly AgentGrants Grants = new([CapabilityFixtures.Scope], []);

    /// <summary>Register, initialize and execute: the agent remembers, recalls and calls one tool.</summary>
    [Fact]
    public async Task Harness_runs_an_agent_end_to_end()
    {
        using var harness = new AgentTestHarness();
        var handler = new RecordingCapabilityHandler(CapabilityResult.Success(Json("""{"ok":true}""")));
        harness.Capabilities.Register(CapabilityFixtures.Descriptor(), handler);
        var agent = new ScriptedAgent("agent-1", async (context, token) =>
        {
            await context.Memory.RememberAsync(new AgentMemoryNote("note", "hello", []), token);
            var recalled = await context.Memory.RecallAsync(new AgentRecall(), token);
            var tool = await context.Tools.InvokeAsync(new ToolCall(CapabilityFixtures.Name, CapabilityFixtures.Version, Json("{}")), token);
            return new AgentResult(AgentOutcome.Completed, Json($$"""{"recalled":{{recalled.Count}},"tool":"{{tool.Status}}"}"""), null, null);
        });

        harness.Lifecycle.Register(new AgentRegistration(agent, Grants, [new(CapabilityFixtures.Name, CapabilityFixtures.Version)]));
        await harness.Lifecycle.InitializeAsync("agent-1");
        var result = await harness.Lifecycle.ExecuteAsync("agent-1", new AgentRequest("run", Json("{}")), harness.ExecutionOptions());

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.Output!.Value.GetProperty("recalled").GetInt32());
        Assert.Equal("Succeeded", result.Output.Value.GetProperty("tool").GetString());
        Assert.Single(handler.Invocations);
        Assert.Equal(["register", "initialize", "execute", "complete"], harness.Observer.Transitions.Select(transition => transition.Transition));
    }

    /// <summary>A tool that needs approval leaves the call waiting.</summary>
    [Fact]
    public async Task Harness_reports_awaiting_approval()
    {
        using var harness = new AgentTestHarness();
        harness.Policy.Outcome = PolicyOutcome.RequireApproval;
        harness.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        var handler = new RecordingCapabilityHandler(CapabilityResult.Success(null));
        harness.Capabilities.Register(CapabilityFixtures.Descriptor(), handler);
        var agent = new ScriptedAgent("agent-1", async (context, token) =>
        {
            var tool = await context.Tools.InvokeAsync(new ToolCall(CapabilityFixtures.Name, CapabilityFixtures.Version, Json("{}")), token);
            return new AgentResult(AgentOutcome.AwaitingApproval, null, tool.ErrorCode, tool.ApprovalId);
        });
        harness.Lifecycle.Register(new AgentRegistration(agent, Grants, [new(CapabilityFixtures.Name, CapabilityFixtures.Version)]));
        await harness.Lifecycle.InitializeAsync("agent-1");

        var result = await harness.Lifecycle.ExecuteAsync("agent-1", new AgentRequest("run", Json("{}")), harness.ExecutionOptions());

        Assert.Equal(AgentOutcome.AwaitingApproval, result.Outcome);
        Assert.NotNull(result.ApprovalId);
        Assert.Empty(handler.Invocations);
        Assert.Single(harness.Gate.ReceivedRequests);
    }

    /// <summary>A scripted agent records each context it was given.</summary>
    [Fact]
    public async Task Scripted_agent_records_its_context()
    {
        var agent = new ScriptedAgent("agent-1");
        var context = new TestAgentRunContext(agent.Descriptor);

        var result = await agent.ExecuteAsync(context, new AgentRequest("run", Json("{}")), CancellationToken.None);

        Assert.Equal(AgentOutcome.Completed, result.Outcome);
        Assert.Same(context, Assert.Single(agent.Contexts));
        Assert.Equal(TestAgentRunContext.FixedStart, context.StartedAtUtc);
    }

    /// <summary>The in-memory tool invoker returns scripted results and records calls.</summary>
    [Fact]
    public async Task In_memory_tool_invoker_returns_scripted_results()
    {
        var tools = new InMemoryToolInvoker();
        tools.Script("search", "1.0.0", new ToolCallResult(ToolCallStatus.Succeeded, Json("[1]"), null, null));

        var hit = await tools.InvokeAsync(new ToolCall("search", "1.0.0", Json("{}")));
        var miss = await tools.InvokeAsync(new ToolCall("other", "1.0.0", Json("{}")));

        Assert.Equal(ToolCallStatus.Succeeded, hit.Status);
        Assert.Equal(ToolCallStatus.NotFound, miss.Status);
        Assert.Equal(2, tools.Calls.Count);
    }

    /// <summary>The in-memory memory recalls newest first with kind and tag filters.</summary>
    [Fact]
    public async Task In_memory_memory_recalls_newest_first_with_filters()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var memory = new InMemoryAgentMemory(time);
        await memory.RememberAsync(new AgentMemoryNote("note", "one", ["a"]));
        time.Advance(TimeSpan.FromMinutes(1));
        await memory.RememberAsync(new AgentMemoryNote("note", "two", ["a", "b"]));
        time.Advance(TimeSpan.FromMinutes(1));
        await memory.RememberAsync(new AgentMemoryNote("diary", "three", ["b"]));

        Assert.Equal(["three", "two", "one"], (await memory.RecallAsync(new AgentRecall())).Select(item => item.Content));
        Assert.Equal(["two", "one"], (await memory.RecallAsync(new AgentRecall(Kind: "note"))).Select(item => item.Content));
        Assert.Equal(["two"], (await memory.RecallAsync(new AgentRecall(AllTags: ["a", "b"]))).Select(item => item.Content));
        Assert.Equal(["three"], (await memory.RecallAsync(new AgentRecall(Limit: 1))).Select(item => item.Content));
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
