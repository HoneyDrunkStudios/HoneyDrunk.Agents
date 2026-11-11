using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Operations.Tests.Approvals;

/// <summary>A legacy-only view of a real approval desk, without its optional scoped capability.</summary>
internal sealed class LegacyApprovalDesk(IApprovalDesk inner) : IApprovalDesk
{
    public Task<ApprovalDecision?> DecideAsync(string approvalId, ApprovalOutcome outcome, string approver, string reason, CancellationToken cancellationToken = default) =>
        inner.DecideAsync(approvalId, outcome, approver, reason, cancellationToken);

    public Task<IReadOnlyList<PendingApproval>> ListPendingAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        inner.ListPendingAsync(limit, cancellationToken);
}
