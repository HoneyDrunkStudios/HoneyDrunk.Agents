using HoneyDrunk.Kernel.Abstractions.Agents;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Tests the input and authority boundaries of the public agent records.</summary>
public sealed class AgentRecordTests
{
    /// <summary>Rejects agent IDs that cannot identify a memory partition.</summary>
    /// <param name="id">The invalid ID.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Agent")]
    [InlineData("-agent")]
    [InlineData("agent\n")]
    [InlineData("agent name")]
    [InlineData("agent/other")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Descriptor_WithInvalidId_Throws(string id) =>
        Assert.Throws<ArgumentException>(() => new AgentDescriptor(id, "Worker", "automation", "1.0.0"));

    /// <summary>Projects standard Grid context without granting Kernel capabilities.</summary>
    [Fact]
    public void Descriptor_WithValidId_ProjectsStandardContext()
    {
        IAgentDescriptor descriptor = new AgentDescriptor("1.test_worker-2", "Worker", "automation", "1.0.0");

        Assert.Equal("1.test_worker-2", descriptor.AgentId);
        Assert.Equal(AgentContextScope.Standard, descriptor.ContextScope);
        Assert.Empty(descriptor.Capabilities);
        Assert.Empty(descriptor.Metadata);
        Assert.False(descriptor.HasCapability("example"));
    }

    /// <summary>Checks ordinal intersection and verifies that later edits cannot increase a registration's authority.</summary>
    [Fact]
    public void Narrow_WithDifferentGrants_PreservesOnlyRegisteredValues()
    {
        string[] scopes = ["a", "b"];
        var registered = new AgentGrants(scopes, ["reader", "worker"]);
        scopes[0] = "z";

        var effective = registered.Narrow(new AgentGrants(["a", "z"], ["reader", "Worker"]));

        Assert.Equal(["a"], effective.Scopes);
        Assert.Equal(["reader"], effective.Roles);
        Assert.Same(registered, registered.Narrow(null));
    }

    /// <summary>Snapshots request, tool, and output JSON before the original document is released.</summary>
    [Fact]
    public void JsonRecords_AfterDocumentDisposal_PreservePayloads()
    {
        var (request, call, result, toolResult) = CreateJsonRecords();

        Assert.Equal(42, request.Payload.GetProperty("count").GetInt32());
        Assert.Equal(42, call.Arguments.GetProperty("count").GetInt32());
        Assert.Equal(42, result.Output?.GetProperty("count").GetInt32());
        Assert.Equal(42, toolResult.Output?.GetProperty("count").GetInt32());
    }

    /// <summary>Rejects non-object capability arguments.</summary>
    [Fact]
    public void ToolCall_WithArrayArguments_Throws()
    {
        using var document = JsonDocument.Parse("[]");

        Assert.Throws<ArgumentException>(() => new ToolCall("echo", "1.0.0", document.RootElement));
    }

    /// <summary>Accidental formatting cannot expose request, tool, or memory content.</summary>
    [Fact]
    public void SensitiveRecords_WhenFormatted_OmitWorkloadData()
    {
        const string secret = "private-data-sentinel";
        var payload = JsonSerializer.SerializeToElement(new { secret });
        object[] records =
        [
            new AgentRequest(secret, payload),
            new ToolCall("echo", "1.0.0", payload),
            new AgentResult(AgentOutcome.Failed, payload, secret, secret),
            new ToolCallResult(ToolCallStatus.Failed, payload, secret, secret),
            new AgentMemoryNote(secret, secret, [secret]),
            new AgentMemoryItem(secret, secret, secret, [secret], DateTimeOffset.UnixEpoch, false),
            new AgentRecall(Kind: secret, AllTags: [secret]),
        ];

        Assert.All(records, record => Assert.DoesNotContain(secret, record.ToString(), StringComparison.Ordinal));
    }

    /// <summary>Rejects recalls outside the documented range.</summary>
    /// <param name="limit">The invalid limit.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Recall_WithInvalidLimit_Throws(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentRecall(limit));

    /// <summary>Rejects repeated keys and keeps a defensive copy of the registration bindings.</summary>
    [Fact]
    public void Registration_WithDuplicateBindings_Throws()
    {
        AgentToolBinding[] bindings = [new("echo", "1.0.0")];
        var agent = new ExampleAgent();
        var registration = new AgentRegistration(agent, AgentGrants.None, bindings);
        bindings[0] = new AgentToolBinding("echo", "2.0.0");

        Assert.Equal("1.0.0", Assert.Single(registration.Tools).Version);
        Assert.Throws<ArgumentException>(() => new AgentRegistration(agent, AgentGrants.None, [bindings[0], new("echo", "2.0.0")]));
    }

    private static (AgentRequest request, ToolCall call, AgentResult result, ToolCallResult toolResult) CreateJsonRecords()
    {
        using var document = JsonDocument.Parse("""{"count":42}""");
        return (
            new AgentRequest("example", document.RootElement),
            new ToolCall("echo", "1.0.0", document.RootElement),
            new AgentResult(AgentOutcome.Completed, document.RootElement, null, null),
            new ToolCallResult(ToolCallStatus.Succeeded, document.RootElement, null, null));
    }

    private sealed class ExampleAgent : IAgent
    {
        public IAgentDescriptor Descriptor { get; } = new AgentDescriptor("example", "Worker", "automation", "1.0.0");

        public Task<AgentResult> ExecuteAsync(IAgentRunContext context, AgentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
