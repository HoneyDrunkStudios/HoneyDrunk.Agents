using HoneyDrunk.Operator.Abstractions;
using System.Collections.Concurrent;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An approval gate whose replies are set per request, and whose stored decisions a test can change.</summary>
internal sealed class ScriptedApprovalGate : IApprovalGate
{
    private readonly ConcurrentDictionary<string, ApprovalOutcome> decisions = new(StringComparer.Ordinal);
    private readonly List<ApprovalRequest> received = [];

    public ApprovalOutcome DefaultOutcome { get; set; } = ApprovalOutcome.Pending;

    public Exception? Throw { get; set; }

    public IReadOnlyList<ApprovalRequest> Received
    {
        get
        {
            lock (this.received)
            {
                return [.. this.received];
            }
        }
    }

    public void Decide(string approvalId, ApprovalOutcome outcome) => this.decisions[approvalId] = outcome;

    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        if (this.Throw is not null)
        {
            throw this.Throw;
        }

        lock (this.received)
        {
            this.received.Add(request);
        }

        this.decisions[request.ApprovalId] = this.DefaultOutcome;
        return Task.FromResult(new ApprovalDecision(request.ApprovalId, this.DefaultOutcome, "scripted", DateTimeOffset.UnixEpoch, null));
    }

    public Task<ApprovalDecision?> CheckStatusAsync(string approvalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.decisions.TryGetValue(approvalId, out var outcome)
            ? new ApprovalDecision(approvalId, outcome, "scripted", DateTimeOffset.UnixEpoch, null)
            : null);
}
