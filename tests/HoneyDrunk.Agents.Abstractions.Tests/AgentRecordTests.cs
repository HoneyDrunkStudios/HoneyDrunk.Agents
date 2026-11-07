using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Identity;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Validation and behavior of the Agents-owned records.</summary>
public sealed class AgentRecordTests
{
    /// <summary>Agent ids follow the memory coordinate format.</summary>
    /// <param name="agentId">A bad id.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("agent\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Descriptor_rejects_bad_agent_ids(string agentId)
    {
        Assert.Throws<ArgumentException>(() => new AgentDescriptor(agentId, "Name", "type", "1.0.0"));
    }

    /// <summary>A valid id is kept as given.</summary>
    [Fact]
    public void Descriptor_accepts_a_valid_id()
    {
        var descriptor = new AgentDescriptor("agent-1.a_b", "Name", "type", "1.0.0");

        Assert.Equal("agent-1.a_b", descriptor.AgentId);
    }

    /// <summary>Tool rights never live in Kernel's capability list.</summary>
    [Fact]
    public void Descriptor_declares_no_kernel_capabilities()
    {
        IAgentDescriptor descriptor = new AgentDescriptor("agent", "Name", "type", "1.0.0");

        Assert.Empty(descriptor.Capabilities);
        Assert.False(descriptor.HasCapability("access:anything"));
        Assert.Equal(AgentContextScope.Standard, descriptor.ContextScope);
        Assert.Empty(descriptor.Metadata);
    }

    /// <summary>Two descriptors with the same values are equal.</summary>
    [Fact]
    public void Equal_descriptors_compare_equal()
    {
        Assert.Equal(new AgentDescriptor("agent", "Name", "type", "1.0.0"), new AgentDescriptor("agent", "Name", "type", "1.0.0"));
    }

    /// <summary>A narrowing keeps only what was already granted.</summary>
    [Fact]
    public void Narrow_never_widens()
    {
        var grants = new AgentGrants(["a", "b"], ["r1", "r2"]);

        var narrowed = grants.Narrow(new AgentGrants(["a", "z"], ["r2", "r9"]));

        Assert.Equal(["a"], narrowed.Scopes);
        Assert.Equal(["r2"], narrowed.Roles);
    }

    /// <summary>No narrowing keeps the grants.</summary>
    [Fact]
    public void Narrow_with_null_keeps_grants()
    {
        var grants = new AgentGrants(["a"], ["r"]);

        Assert.Same(grants, grants.Narrow(null));
    }

    /// <summary>Grants reject blank entries.</summary>
    [Fact]
    public void Grants_reject_blank_entries()
    {
        Assert.Throws<ArgumentException>(() => new AgentGrants([" "], []));
        Assert.Throws<ArgumentException>(() => new AgentGrants([], [string.Empty]));
    }

    /// <summary>The payload is a private copy.</summary>
    [Fact]
    public void Request_payload_survives_source_disposal()
    {
        AgentRequest request;
        using (var document = JsonDocument.Parse("""{"x":1}"""))
        {
            request = new AgentRequest("kind", document.RootElement);
        }

        Assert.Equal(1, request.Payload.GetProperty("x").GetInt32());
    }

    /// <summary>A request needs a payload.</summary>
    [Fact]
    public void Request_rejects_undefined_payload()
    {
        Assert.Throws<ArgumentException>(() => new AgentRequest("kind", default));
    }

    /// <summary>Tool arguments are a JSON object.</summary>
    [Fact]
    public void Tool_call_rejects_non_object_arguments()
    {
        using var document = JsonDocument.Parse("[1]");

        Assert.Throws<ArgumentException>(() => new ToolCall("tool", "1.0.0", document.RootElement));
        Assert.Throws<ArgumentException>(() => new ToolCall("tool", "1.0.0", default));
    }

    /// <summary>Recall limits follow Memory's bounds.</summary>
    /// <param name="limit">A bad limit.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Recall_rejects_limit_out_of_range(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentRecall(limit));
    }

    /// <summary>A registration binds each tool name once.</summary>
    [Fact]
    public void Bindings_reject_duplicates()
    {
        var agent = new StubAgent();

        Assert.Throws<ArgumentException>(() => new AgentRegistration(agent, AgentGrants.None, [new("tool", "1.0.0"), new("tool", "2.0.0")]));
    }

    /// <summary>The memory binding never lists its home as an extra read.</summary>
    [Fact]
    public void Memory_binding_rejects_home_in_also_read()
    {
        var home = ProjectId.NewId();

        Assert.Throws<ArgumentException>(() => new AgentMemoryBinding(TenantId.NewId(), home, [home]));
    }

    /// <summary>Text representations carry no content, arguments or output.</summary>
    [Fact]
    public void Records_never_print_their_content()
    {
        using var document = JsonDocument.Parse("""{"secret":"sk-secret-value"}""");
        var element = document.RootElement;
        var texts = new object[]
        {
            new AgentRequest("kind", element),
            new ToolCall("tool", "1.0.0", element),
            new ToolCallResult(ToolCallStatus.Succeeded, element, null, null),
            new AgentResult(AgentOutcome.Completed, element, null, null),
            new AgentMemoryNote("note", "sk-secret-value", ["sk-secret-value"]),
            new AgentMemoryItem("id", "note", "sk-secret-value", ["sk-secret-value"], DateTimeOffset.UnixEpoch, true),
        };

        Assert.All(texts, text => Assert.DoesNotContain("sk-secret-value", text.ToString(), StringComparison.Ordinal));
    }

    private sealed class StubAgent : IAgent
    {
        public IAgentDescriptor Descriptor { get; } = new AgentDescriptor("stub", "Stub", "stub", "1.0.0");

        public Task<AgentResult> ExecuteAsync(IAgentExecutionContext context, AgentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentResult(AgentOutcome.Completed, null, null, null));
    }
}
