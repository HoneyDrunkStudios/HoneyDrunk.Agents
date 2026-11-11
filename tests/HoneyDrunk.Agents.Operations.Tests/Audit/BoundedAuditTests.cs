using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests.Audit;

/// <summary>Audit source reads are bounded before projection, including dense and failing sources.</summary>
public sealed class BoundedAuditTests
{
    private static readonly TenantId Tenant = new("01JC0000000000000000000001");

    /// <summary>Three disjoint source pages preserve the global newest times without loading irrelevant targets.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Dense_scopes_return_newest_50_with_three_bounded_reads()
    {
        var audit = new FakeAuditQuery();
        var expected = new List<AuditEntry>();
        for (var index = 0; index < 100; index++)
        {
            AuditEntry[] entries =
            [
                Entry($"agent-{index:D3}", OperationsHost.Start.AddSeconds(-3 * index), "agent", "a"),
                Entry($"cost-{index:D3}", OperationsHost.Start.AddSeconds((-3 * index) - 1), "cost-scope", "a"),
                Entry($"prefixed-{index:D3}", OperationsHost.Start.AddSeconds((-3 * index) - 2), "approval", "agent:a"),
            ];
            foreach (var entry in entries)
            {
                audit.Add(entry);
                expected.Add(entry);
            }

            audit.Add(Entry($"unrelated-{index:D3}", OperationsHost.Start, "user", "a"));
        }

        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal("available", response.GetProperty("state").GetString());
        Assert.Equal(expected.OrderByDescending(entry => entry.OccurredAt).Take(50).Select(entry => entry.EventName), response.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("event").GetString()));
        Assert.Equal(0, audit.LegacyReads);
        Assert.Equal([50, 50, 50], audit.ReturnedSizes);
        Assert.Equal(3, audit.Filters.Count);
        Assert.Equal([("agent", "a"), ("cost-scope", "a"), ((string?)null, "agent:a")], audit.Filters.Select(filter => (targetType: filter.TargetType, targetId: filter.TargetId)));
        Assert.All(audit.Filters, filter =>
        {
            Assert.Equal(50, filter.Limit);
            Assert.Equal(OperationsHost.Start.AddDays(-7), filter.Since);
            Assert.Equal(OperationsHost.Start, filter.Until);
        });
    }

    /// <summary>Equal-time entries use the local source's ordinal audit-id order, including a full cutoff page.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Equal_time_page_keeps_latest_ids()
    {
        var audit = new FakeAuditQuery();
        for (var index = 0; index < 75; index++)
        {
            audit.Add(Entry($"event-{index:D3}", OperationsHost.Start, "agent", "a"));
        }

        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal(Enumerable.Range(25, 50).Reverse().Select(index => $"event-{index:D3}"), response.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("event").GetString()));
        Assert.Equal([50, 0, 0], audit.ReturnedSizes);
    }

    /// <summary>The seven-day boundary is inclusive, without widening or repeated reads.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Inclusive_boundaries_are_preserved()
    {
        var audit = new FakeAuditQuery();
        audit.Add(Entry("old", OperationsHost.Start.AddDays(-7).AddTicks(-1), "agent", "a"));
        audit.Add(Entry("since", OperationsHost.Start.AddDays(-7), "agent", "a"));
        audit.Add(Entry("until", OperationsHost.Start, "cost-scope", "a"));
        audit.Add(Entry("future", OperationsHost.Start.AddTicks(1), "agent", "a"));
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal(["until", "since"], response.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("event").GetString()));
        Assert.Equal(3, audit.Filters.Count);
    }

    /// <summary>A legacy-only provider is unavailable without issuing its unbounded query.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Legacy_provider_is_never_read()
    {
        var audit = Substitute.For<IAuditQuery>();
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal("unavailable", response.GetProperty("state").GetString());
        Assert.Empty(response.GetProperty("entries").EnumerateArray());
        await audit.DidNotReceive().ReadAsync(Arg.Any<AuditQueryFilter>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Wrong scopes and oversized source pages fail closed rather than hiding discarded matches.</summary>
    /// <param name="oversized">Whether the source violates the limit rather than the scope.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nonconforming_page_is_unavailable(bool oversized)
    {
        var audit = new FakeAuditQuery { IgnoreLimit = oversized, IgnoreTarget = !oversized };
        for (var index = 0; index < 51; index++)
        {
            audit.Add(Entry($"private-{index}", OperationsHost.Start, oversized ? "agent" : "user", "a"));
        }

        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal("unavailable", response.GetProperty("state").GetString());
        Assert.Empty(response.GetProperty("entries").EnumerateArray());
        Assert.Single(audit.Filters);
        Assert.Equal(0, audit.LegacyReads);
        Assert.DoesNotContain("private-", response.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A later failing scope cannot expose a partial successful audit history.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Later_source_failure_discards_earlier_page()
    {
        var audit = new FakeAuditQuery { FailOnCall = 2 };
        audit.Add(Entry("first-page", OperationsHost.Start, "agent", "a"));
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal("unavailable", response.GetProperty("state").GetString());
        Assert.Empty(response.GetProperty("entries").EnumerateArray());
        Assert.Equal(2, audit.Filters.Count);
        Assert.DoesNotContain("audit-secret", response.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(host.Logs.Lines, line => line.Contains("audit-secret", StringComparison.Ordinal));
    }

    private static AuditEntry Entry(string id, DateTimeOffset time, string targetType, string targetId) =>
        new(new AuditEntryId(id), time, "operator", id, AuditCategory.AgentAction, AuditOutcome.Succeeded, new AuditTarget(targetType, targetId), Tenant);
}
