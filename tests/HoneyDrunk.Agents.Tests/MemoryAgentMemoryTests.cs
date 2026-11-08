using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Memory.Abstractions;
using Xunit;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Agent memory writes to the home scope and reads home plus authorized projects.</summary>
public sealed class MemoryAgentMemoryTests
{
    private const string Sentinel = "sk-secret-value";

    /// <summary>A note lands in the home scope.</summary>
    [Fact]
    public async Task Remember_writes_to_the_home_scope()
    {
        var fixture = new MemoryFixture();
        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA));

        var item = await memory.RememberAsync(new AgentMemoryNote("note", "hello", ["a"]));

        Assert.True(item.FromHomeScope);
        Assert.Equal(fixture.Time.GetUtcNow(), item.WrittenAt);
        var home = fixture.Resolver.ResolveAgentScope(new MemoryScopeKey(MemoryFixture.Tenant, MemoryFixture.ProjectA, InvokerFixture.AgentId), new MemoryCaller("test", [], []));
        var stored = Assert.Single(await fixture.Store.ReadAsync(home, new MemoryQuery()));
        Assert.Equal(item.Id, stored.Id);
    }

    /// <summary>Without extra projects, recall sees only the home scope.</summary>
    [Fact]
    public async Task Recall_reads_home_only_without_also_read()
    {
        var fixture = new MemoryFixture();
        using (var other = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectB)))
        {
            await other.RememberAsync(new AgentMemoryNote("note", "from b", []));
        }

        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA));
        await memory.RememberAsync(new AgentMemoryNote("note", "from a", []));

        var items = await memory.RecallAsync(new AgentRecall());

        Assert.Equal(["from a"], items.Select(item => item.Content));
    }

    /// <summary>Granted extra projects are readable.</summary>
    [Fact]
    public async Task Recall_includes_also_read_projects_when_granted()
    {
        var fixture = new MemoryFixture();
        using (var other = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectB)))
        {
            await other.RememberAsync(new AgentMemoryNote("note", "from b", []));
        }

        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA, MemoryFixture.ProjectB), MemoryFixture.ReadScope(MemoryFixture.ProjectB));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await memory.RememberAsync(new AgentMemoryNote("note", "from a", []));

        var items = await memory.RecallAsync(new AgentRecall());

        Assert.Equal(["from a", "from b"], items.Select(item => item.Content));
    }

    /// <summary>Only home entries are marked as home.</summary>
    [Fact]
    public async Task Recall_marks_entries_from_home()
    {
        var fixture = new MemoryFixture();
        using (var other = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectB)))
        {
            await other.RememberAsync(new AgentMemoryNote("note", "from b", []));
        }

        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA, MemoryFixture.ProjectB), MemoryFixture.ReadScope(MemoryFixture.ProjectB));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await memory.RememberAsync(new AgentMemoryNote("note", "from a", []));

        var items = await memory.RecallAsync(new AgentRecall());

        Assert.Equal([true, false], items.Select(item => item.FromHomeScope));
    }

    /// <summary>Reading another project without the read scope is denied before any read.</summary>
    [Fact]
    public async Task Escalation_without_read_scope_throws_denied()
    {
        var fixture = new MemoryFixture();

        await Assert.ThrowsAsync<MemoryEscalationDeniedException>(() => fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA, MemoryFixture.ProjectB)));
    }

    /// <summary>The Memory node's validation applies to notes.</summary>
    [Fact]
    public async Task Memory_validation_errors_propagate()
    {
        var fixture = new MemoryFixture();
        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA));
        var tags = Enumerable.Range(0, 17).Select(index => "t" + index).ToArray();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => memory.RememberAsync(new AgentMemoryNote("note", "x", tags)));
    }

    /// <summary>Content and tags never reach telemetry or logs.</summary>
    [Fact]
    public async Task Content_and_tags_never_in_telemetry_or_logs()
    {
        var fixture = new MemoryFixture();
        using var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA));

        await memory.RememberAsync(new AgentMemoryNote("note", Sentinel, [Sentinel]));
        await memory.RecallAsync(new AgentRecall(AllTags: [Sentinel]));

        Assert.Contains(fixture.Telemetry.Activities, activity => activity.OperationName == "agents.memory");
        Assert.DoesNotContain(fixture.Telemetry.Tags, tag => (tag.Value?.ToString() ?? string.Empty).Contains(Sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.message.Contains(Sentinel, StringComparison.Ordinal));
    }

    /// <summary>Disposal cancels a memory call still in flight.</summary>
    [Fact]
    public async Task Dispose_cancels_an_in_flight_memory_call()
    {
        var fixture = new MemoryFixture();
        var home = fixture.Resolver.ResolveAgentScope(new MemoryScopeKey(MemoryFixture.Tenant, MemoryFixture.ProjectA, InvokerFixture.AgentId), new MemoryCaller("agent:" + InvokerFixture.AgentId, [], []));
        var identity = new ExecutionIdentity(InvokerFixture.AgentId, "exec-1", "corr-1", AgentGrants.None, [], MemoryFixture.Tenant);
        using var memory = new MemoryAgentMemory(new WaitingStore(), home, home, identity, fixture.Telemetry, fixture.Log);

        var write = memory.RememberAsync(new AgentMemoryNote("note", "late", []));
        memory.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
    }

    /// <summary>Using memory after its execution ends throws.</summary>
    [Fact]
    public async Task Use_after_dispose_throws()
    {
        var fixture = new MemoryFixture();
        var memory = await fixture.OpenAsync(MemoryFixture.Binding(MemoryFixture.ProjectA));
        memory.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => memory.RecallAsync(new AgentRecall()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => memory.RememberAsync(new AgentMemoryNote("note", "x", [])));
    }
}
