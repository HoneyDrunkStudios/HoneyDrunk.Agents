using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Abstractions.Approvals;

namespace HoneyDrunk.Agents.Operations.Tests.Approvals;

/// <summary>A bounded desk double that records which capability the endpoint invokes.</summary>
internal sealed class CapturingApprovalDesk : IApprovalDesk, ISubjectScopedApprovalDesk
{
    public List<PendingApprovalQuery> Queries { get; } = [];

    public int LegacyReads { get; private set; }

    public IReadOnlyList<PendingApproval> Results { get; set; } = [];

    public Task<ApprovalDecision?> DecideAsync(string approvalId, ApprovalOutcome outcome, string approver, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PendingApproval>> ListPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        this.LegacyReads++;
        return Task.FromResult(this.Results);
    }

    public Task<IReadOnlyList<PendingApproval>> ListPendingAsync(PendingApprovalQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Queries.Add(query);
        return Task.FromResult(this.Results);
    }
}
