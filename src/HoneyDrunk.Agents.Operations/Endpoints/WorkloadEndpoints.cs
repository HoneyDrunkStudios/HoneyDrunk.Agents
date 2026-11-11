using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Agents.Operations.Runs;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace HoneyDrunk.Agents.Operations.Endpoints;

/// <summary>The workload and run read endpoints.</summary>
internal static class WorkloadEndpoints
{
    private const int MaxWorkloads = 20;
    private const int MaxRunsPerWorkload = 10;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    /// <summary>Maps the routes.</summary>
    /// <param name="group">The <c>/v1</c> group.</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/workloads", ListAsync);
        group.MapGet("/workloads/{id}", GetAsync);
        group.MapGet("/workloads/{id}/runs", RunsAsync);
        group.MapGet("/workloads/{id}/runs/{runId}", RunAsync);
    }

    private static async Task<IResult> ListAsync(
        IAgentDirectory directory,
        IAgentRunJournal journal,
        IOptions<AgentOperationsOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var environment = options.Value.Environment;
        var workloads = new List<WorkloadSummary>();
        foreach (var agent in Projection.Listed(directory).Take(MaxWorkloads))
        {
            var runs = await journal.RecentAsync(agent.AgentId, MaxRunsPerWorkload, null, finishedOnly: true, cancellationToken).ConfigureAwait(false);
            workloads.Add(new WorkloadSummary(agent.AgentId, Projection.DisplayName(agent), environment, Projection.Runs(runs, MaxRunsPerWorkload)));
        }

        return Sources.Json(new WorkloadsResponse(time.GetUtcNow(), workloads));
    }

    private static async Task<IResult> GetAsync(
        string id,
        IAgentDirectory directory,
        IOptions<AgentOperationsOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken,
        [FromServices] IAgentPauses? pauses = null,
        [FromServices] ICostGuard? costGuard = null)
    {
        if (Projection.Find(directory, id) is not { } agent)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var logger = loggers.CreateLogger(Sources.LogCategory);
        var pause = await Sources.ReadAsync(
            pauses,
            "pause",
            async source => Projection.Pause(await source.GetAsync(Projection.AgentPrefix + agent.AgentId, cancellationToken).ConfigureAwait(false)),
            logger,
            cancellationToken).ConfigureAwait(false);
        var budget = await Sources.ReadAsync<ICostGuard, BudgetView>(
            costGuard,
            "budget",
            async source => Projection.Budget(await source.GetStatusAsync(agent.AgentId, CostWindows.Month(now), cancellationToken).ConfigureAwait(false)),
            logger,
            cancellationToken).ConfigureAwait(false);
        return Sources.Json(new WorkloadDetail(
            now,
            agent.AgentId,
            Projection.DisplayName(agent),
            options.Value.Environment,
            Projection.Phase(agent.Phase),
            pause,
            budget,
            []));
    }

    private static async Task<IResult> RunsAsync(
        string id,
        string? limit,
        string? before,
        IAgentDirectory directory,
        IAgentRunJournal journal,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Projection.Find(directory, id) is not { } agent)
        {
            return TypedResults.NotFound();
        }

        var size = DefaultPageSize;
        if (limit is not null && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > MaxPageSize))
        {
            return TypedResults.Problem("limit must be a whole number from 1 to 50.", statusCode: StatusCodes.Status400BadRequest);
        }

        string? cursor = null;
        if (before is not null)
        {
            if (!Guid.TryParseExact(before, "N", out var parsed))
            {
                return TypedResults.Problem("before must be a run id: 32 hexadecimal digits.", statusCode: StatusCodes.Status400BadRequest);
            }

            cursor = parsed.ToString("N");
        }

        var records = await journal.RecentAsync(agent.AgentId, size, cursor, finishedOnly: true, cancellationToken).ConfigureAwait(false);
        var runs = Projection.Runs(records, size);
        var next = records.Count >= size && runs.Count > 0 ? runs[^1].Id : null;
        return Sources.Json(new RunsPage(time.GetUtcNow(), runs, next));
    }

    private static async Task<IResult> RunAsync(
        string id,
        string runId,
        IAgentDirectory directory,
        IAgentRunJournal journal,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Projection.Find(directory, id) is not { } agent || !Guid.TryParseExact(runId, "N", out var parsed))
        {
            return TypedResults.NotFound();
        }

        var record = await journal.GetAsync(agent.AgentId, parsed.ToString("N"), cancellationToken).ConfigureAwait(false);
        var run = record is not null && string.Equals(record.AgentId, agent.AgentId, StringComparison.Ordinal) ? Projection.Run(record) : null;
        return run is null ? TypedResults.NotFound() : Sources.Json(new RunDetail(time.GetUtcNow(), run));
    }
}
