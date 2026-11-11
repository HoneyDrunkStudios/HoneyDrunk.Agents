using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>An approval desk, pause store and cost guard whose every call fails with a private message.</summary>
internal sealed class ThrowingOperator : IApprovalDesk, IAgentPauses, ICostGuard
{
    public const string Secret = "operator-secret";

    public Task<ApprovalDecision?> DecideAsync(string approvalId, ApprovalOutcome outcome, string approver, string reason, CancellationToken cancellationToken = default) => throw Failure();

    public Task<IReadOnlyList<PendingApproval>> ListPendingAsync(int limit = 100, CancellationToken cancellationToken = default) => throw Failure();

    public Task<PauseState> PauseAsync(string name, string pausedBy, string reason, CancellationToken cancellationToken = default) => throw Failure();

    public Task<bool> ResumeAsync(string name, string resumedBy, string reason, CancellationToken cancellationToken = default) => throw Failure();

    public Task<PauseState?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromException<PauseState?>(Failure());

    public Task<CostCheckResult> CheckBudgetAsync(string scope, string window, decimal amount, CancellationToken cancellationToken = default) => throw Failure();

    public Task RecordAsync(CostEvent costEvent, CancellationToken cancellationToken = default) => throw Failure();

    public Task<CostStatus> GetStatusAsync(string scope, string window, CancellationToken cancellationToken = default) => throw Failure();

    private static InvalidOperationException Failure() => new(Secret);
}
