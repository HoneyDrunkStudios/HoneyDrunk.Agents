using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Operator.Abstractions;
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
            return Sources.Json(new ApprovalsResponse(now, SectionState.Unavailable, []));
        }

        try
        {
            var pending = await desk.ListPendingAsync(MaxApprovals, cancellationToken).ConfigureAwait(false);
            var listed = Projection.Listed(directory).Select(agent => agent.AgentId).ToHashSet(StringComparer.Ordinal);
            var approvals = pending
                .Select(Projection.Approval)
                .OfType<PendingApprovalView>()
                .Where(approval => listed.Contains(approval.WorkloadId))
                .DistinctBy(approval => approval.ApprovalId, StringComparer.Ordinal)
                .Take(MaxApprovals)
                .ToArray();
            return Sources.Json(new ApprovalsResponse(now, SectionState.Available, approvals));
        }
        catch (Exception exception) when (Sources.IsFailure(exception, cancellationToken))
        {
            Sources.SourceFailed(loggers.CreateLogger(Sources.LogCategory), "approvals", exception.GetType().Name);
            return Sources.Json(new ApprovalsResponse(now, SectionState.Unavailable, []));
        }
    }
}
