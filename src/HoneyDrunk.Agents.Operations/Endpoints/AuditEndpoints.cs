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

    // The audit query reads earliest first, so the newest entries are found by widening the window: a busy agent
    // is answered from the last hour, and only a quiet one reads the whole week.
    private static readonly TimeSpan[] Windows = [TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromDays(7)];

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
        if (audit is null)
        {
            return Sources.Json(new AuditResponse(now, SectionState.Unavailable, []));
        }

        try
        {
            AuditEntryView[] entries = [];
            foreach (var window in Windows)
            {
                entries = await ReadAsync(audit, agent.AgentId, now - window, now, cancellationToken).ConfigureAwait(false);
                if (entries.Length >= MaxEntries)
                {
                    break;
                }
            }

            return Sources.Json(new AuditResponse(now, SectionState.Available, entries));
        }
        catch (Exception exception) when (Sources.IsFailure(exception, cancellationToken))
        {
            Sources.SourceFailed(loggers.CreateLogger(Sources.LogCategory), "audit", exception.GetType().Name);
            return Sources.Json(new AuditResponse(now, SectionState.Unavailable, []));
        }
    }

    private static async Task<AuditEntryView[]> ReadAsync(IAuditQuery audit, string agentId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        string[] targets = [agentId, Projection.AgentPrefix + agentId];
        var found = new List<AuditEntry>();
        foreach (var target in targets)
        {
            found.AddRange(await audit.ReadAsync(new AuditQueryFilter(since, until, TargetId: target), cancellationToken).ConfigureAwait(false));
        }

        // The source is asked for this agent and this window; what it returns is checked again before it is shown.
        return
        [
            .. found
                .Where(entry => entry?.Target is not null && targets.Contains(entry.Target.Id, StringComparer.Ordinal) && entry.OccurredAt >= since && entry.OccurredAt <= until)
                .DistinctBy(entry => (id: entry.Id, at: entry.OccurredAt, name: entry.EventName, actor: entry.Actor, target: entry.Target.Id))
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id.Value, StringComparer.Ordinal)
                .Take(MaxEntries)
                .Select(Projection.Audit),
        ];
    }
}
