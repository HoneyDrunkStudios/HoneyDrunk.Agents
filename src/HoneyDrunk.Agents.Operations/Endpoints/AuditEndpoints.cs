using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Audit.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents.Operations.Endpoints;

/// <summary>The recent-audit read endpoint.</summary>
internal static class AuditEndpoints
{
    private const int MaxEntries = 50;

    // The target types whose id is an agent id: the agent itself, and Operator's spend scope for it.
    private static readonly HashSet<string> AgentKeyedTargetTypes = new(StringComparer.Ordinal) { "agent", "cost-scope" };

    /// <summary>Maps the route.</summary>
    /// <param name="group">The <c>/v1</c> group.</param>
    public static void Map(RouteGroupBuilder group) => group.MapGet("/workloads/{id}/audit", ListAsync);

    private static async Task<IResult> ListAsync(
        string id,
        IAgentDirectory directory,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken,
        [FromServices] IAuditQuery? audit = null)
    {
        if (Projection.Find(directory, id) is not { } agent)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        if (audit is not IBoundedAuditQuery bounded)
        {
            return Sources.Json(new AuditResponse(now, SectionState.Unavailable, []));
        }

        try
        {
            var entries = await ReadAsync(bounded, agent.AgentId, now.AddDays(-7), now, cancellationToken).ConfigureAwait(false);

            return Sources.Json(new AuditResponse(now, SectionState.Available, entries));
        }
        catch (Exception exception) when (Sources.IsFailure(exception, cancellationToken))
        {
            Sources.SourceFailed(loggers.CreateLogger(Sources.LogCategory), "audit", exception.GetType().Name);
            return Sources.Json(new AuditResponse(now, SectionState.Unavailable, []));
        }
    }

    private static async Task<AuditEntryView[]> ReadAsync(IBoundedAuditQuery audit, string agentId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var prefixed = Projection.AgentPrefix + agentId;
        var found = new List<AuditEntry>();

        // Each disjoint scope contributes at most 50 candidates to the newest 50 globally.
        AuditQueryFilter[] filters =
        [
            new(since, until, TargetType: "agent", TargetId: agentId, Limit: MaxEntries),
            new(since, until, TargetType: "cost-scope", TargetId: agentId, Limit: MaxEntries),
            new(since, until, TargetId: prefixed, Limit: MaxEntries),
        ];
        foreach (var filter in filters)
        {
            var page = await audit.ReadLatestAsync(filter, cancellationToken).ConfigureAwait(false);
            if (page.Count > MaxEntries || page.Any(entry => !Matches(entry, filter)))
            {
                throw new InvalidOperationException("The audit source violated the bounded query contract.");
            }

            found.AddRange(page);
        }

        // The source is asked for this agent and this window; what it returns is checked again before it is shown.
        return
        [
            .. found
                .Where(entry => entry?.Target is not null && IsAbout(entry.Target, agentId, prefixed) && entry.OccurredAt >= since && entry.OccurredAt <= until)
                .DistinctBy(entry => (id: entry.Id, at: entry.OccurredAt, name: entry.EventName, actor: entry.Actor, target: entry.Target.Id))
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id.Value, StringComparer.Ordinal)
                .Take(MaxEntries)
                .Select(Projection.Audit),
        ];
    }

    private static bool Matches(AuditEntry? entry, AuditQueryFilter filter) =>
        entry?.Target is not null
        && entry.OccurredAt >= filter.Since && entry.OccurredAt <= filter.Until
        && string.Equals(entry.Target.Id, filter.TargetId, StringComparison.Ordinal)
        && (filter.TargetType is null || string.Equals(entry.Target.Type, filter.TargetType, StringComparison.Ordinal));

    // "agent:{id}" names an agent whatever the target type. The bare id names one only under a type that is keyed
    // by agent id; a user, an approval or anything else that happens to share the id is not the agent.
    private static bool IsAbout(AuditTarget target, string agentId, string prefixed) =>
        string.Equals(target.Id, prefixed, StringComparison.Ordinal)
        || (string.Equals(target.Id, agentId, StringComparison.Ordinal) && AgentKeyedTargetTypes.Contains(target.Type));
}
