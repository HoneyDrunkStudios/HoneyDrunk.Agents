using HoneyDrunk.Agents.Operations.LocalHost;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests.Audit;

/// <summary>The local host supplies a real bounded audit capability without changing legacy read order.</summary>
public sealed class LocalBoundedAuditTests
{
    /// <summary>Optional filters run before the limit, and legacy reads remain earliest first.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Local_source_filters_before_limiting_and_keeps_legacy_order()
    {
        var log = new LocalAuditLog(TimeProvider.System);
        var now = OperationsHost.Start;
        var tenant = new TenantId("01JC0000000000000000000001");
        var first = new AuditEntry(new AuditEntryId("first"), now.AddSeconds(-2), "actor", "event", AuditCategory.AgentAction, AuditOutcome.Succeeded, new AuditTarget("agent", "a"), tenant, CorrelationId: "correlation");
        await log.AppendAsync(first);
        await log.AppendAsync(first with { Id = new AuditEntryId("second"), OccurredAt = now.AddSeconds(-1) });
        await log.AppendAsync(first with { Id = new AuditEntryId("newer-wrong-type"), OccurredAt = now, Target = new AuditTarget("user", "a") });
        await log.AppendAsync(first with { Id = new AuditEntryId("newer-wrong-actor"), OccurredAt = now, Actor = "other" });
        var filter = new AuditQueryFilter(now.AddDays(-7), now, Actor: "actor", Category: AuditCategory.AgentAction, EventName: "event", TargetType: "agent", TargetId: "a", CorrelationId: "correlation", TenantId: tenant, Limit: 1);

        var latest = await log.ReadLatestAsync(filter);
        var earliest = await log.ReadAsync(filter);

        Assert.Equal("second", Assert.Single(latest).Id.Value);
        Assert.Equal("first", Assert.Single(earliest).Id.Value);
    }

    /// <summary>Invalid bounds are rejected instead of falling back to an unlimited read.</summary>
    /// <param name="limit">The required positive limit, or a deliberately invalid value.</param>
    /// <param name="reversed">Whether the time bounds are reversed.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public async Task Invalid_bounded_query_is_rejected(int limit, bool reversed)
    {
        var log = new LocalAuditLog(TimeProvider.System);
        var now = OperationsHost.Start;
        var filter = new AuditQueryFilter(now.AddDays(reversed ? 1 : -1), now, Limit: limit);

        await Assert.ThrowsAsync<ArgumentException>(() => log.ReadLatestAsync(filter));
    }

    /// <summary>A canceled bounded query observes cancellation before reading local storage.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Canceled_query_is_not_read()
    {
        var log = new LocalAuditLog(TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var filter = new AuditQueryFilter(OperationsHost.Start.AddDays(-1), OperationsHost.Start, Limit: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.ReadLatestAsync(filter, cancellation.Token));
    }
}
