using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Abstractions.Approvals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents.Operations.Endpoints;

/// <summary>The pending-approvals read endpoint.</summary>
internal static class ApprovalEndpoints
{
    private const int MaxApprovals = 100;

    /// <summary>Maps the route.</summary>
    /// <param name="group">The <c>/v1</c> group.</param>
    public static void Map(RouteGroupBuilder group) => group.MapGet("/approvals", ListAsync);

    private static async Task<IResult> ListAsync(
        IAgentDirectory directory,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken,
        [FromServices] IApprovalDesk? desk = null)
    {
        var now = time.GetUtcNow();
        if (desk is null)
        {
            return Sources.Json(new ApprovalsResponse(now, SectionState.Unavailable, [], false));
        }

        try
        {
            var listed = Projection.Listed(directory).Take(Projection.MaxListedWorkloads).Select(agent => agent.AgentId).ToHashSet(StringComparer.Ordinal);
            if (listed.Count == 0)
            {
                return Sources.Json(new ApprovalsResponse(now, SectionState.Available, [], false));
            }

            var scoped = desk as ISubjectScopedApprovalDesk;
            var pending = scoped is not null
                ? await scoped.ListPendingAsync(new PendingApprovalQuery([.. listed.Select(id => Projection.AgentPrefix + id)], MaxApprovals), cancellationToken).ConfigureAwait(false)
                : await desk.ListPendingAsync(MaxApprovals, cancellationToken).ConfigureAwait(false);
            if (pending.Count > MaxApprovals)
            {
                throw new InvalidOperationException("The approval desk exceeded the requested page size.");
            }

            // Keep the released completeness signal for both scoped and legacy pages: a full page may omit older approvals.
            var truncated = pending.Count == MaxApprovals;
            var approvals = pending
                .Select(Projection.Approval)
                .OfType<PendingApprovalView>()
                .Where(approval => listed.Contains(approval.WorkloadId))
                .DistinctBy(approval => approval.ApprovalId, StringComparer.Ordinal)
                .OrderByDescending(approval => approval.RequestedAt)
                .ThenBy(approval => approval.ApprovalId, StringComparer.Ordinal)
                .Take(MaxApprovals)
                .ToArray();
            if (scoped is not null && approvals.Length != pending.Count)
            {
                throw new InvalidOperationException("The approval desk violated the workload-scoped query contract.");
            }

            return Sources.Json(new ApprovalsResponse(now, SectionState.Available, approvals, truncated));
        }
        catch (Exception exception) when (Sources.IsFailure(exception, cancellationToken))
        {
            Sources.SourceFailed(loggers.CreateLogger(Sources.LogCategory), "approvals", exception.GetType().Name);
            return Sources.Json(new ApprovalsResponse(now, SectionState.Unavailable, [], false));
        }
    }
}
