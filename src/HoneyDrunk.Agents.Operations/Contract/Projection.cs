using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>
/// Maps runtime, Operator and Audit records to the response records. Every mapping copies named fields only, so a
/// field added to a source record never reaches a response by accident.
/// </summary>
internal static class Projection
{
    /// <summary>The prefix of an agent's subject and pause name.</summary>
    public const string AgentPrefix = "agent:";

    /// <summary>Maps a spend status. The guard's "no limit" sentinel becomes a zero limit with no remaining amount.</summary>
    /// <param name="status">The spend status.</param>
    /// <returns>The budget view.</returns>
    public static BudgetView Budget(CostStatus status) => status.Limit > 0m
        ? new BudgetView(status.Window, status.Spent, status.Limit, status.Remaining)
        : new BudgetView(status.Window, status.Spent, 0m, null);

    /// <summary>Maps a pending approval whose subject is an agent. The request's context is not read.</summary>
    /// <param name="pending">The pending approval.</param>
    /// <returns>The view, or null when the subject is not <c>agent:</c> followed by a valid agent id.</returns>
    public static PendingApprovalView? Approval(PendingApproval pending)
    {
        var request = pending.Request;
        if (request?.Subject is not { } subject || !subject.StartsWith(AgentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var workloadId = subject[AgentPrefix.Length..];
        return Identifiers.IsValid(workloadId)
            ? new PendingApprovalView(request.ApprovalId, workloadId, request.Action, pending.RequestedAt, request.Expiry)
            : null;
    }
}
