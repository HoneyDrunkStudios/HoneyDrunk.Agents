using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents;

internal sealed class ToolApprovalLedger(TimeProvider timeProvider)
{
    private const int MaxPendingApprovals = 1024;

    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly Dictionary<string, PendingApproval> _pending = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    internal async Task<ToolCallResult?> CheckAsync(
        string key,
        ApprovalRequest request,
        IApprovalGate gate,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var stripe = this._stripes[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % this._stripes.Length];
        await stripe.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = this.Find(key);
            if (pending is not null)
            {
                var decision = await gate.CheckStatusAsync(pending.Id, cancellationToken).ConfigureAwait(false);
                if (decision is not null && decision.Outcome != ApprovalOutcome.Expired && pending.ExpiresAt > timeProvider.GetUtcNow())
                {
                    return this.ApplyDecision(key, pending, decision);
                }

                this.Remove(key);
            }

            var created = this.Open(key, ttl);
            if (created is null)
            {
                return new ToolCallResult(ToolCallStatus.Failed, null, "approval_capacity", null);
            }

            try
            {
                var decision = await gate.RequestAsync(
                    request with { ApprovalId = created.Id, Expiry = created.ExpiresAt }, cancellationToken).ConfigureAwait(false);
                return this.ApplyDecision(key, created, decision);
            }
            catch
            {
                this.Remove(key);
                throw;
            }
        }
        finally
        {
            stripe.Release();
        }
    }

    private ToolCallResult? ApplyDecision(string key, PendingApproval pending, ApprovalDecision decision)
    {
        if (!string.Equals(decision.ApprovalId, pending.Id, StringComparison.Ordinal))
        {
            return new ToolCallResult(ToolCallStatus.Failed, null, "gate_error", null);
        }

        if (pending.ExpiresAt <= timeProvider.GetUtcNow())
        {
            this.Remove(key);
            return new ToolCallResult(ToolCallStatus.AwaitingApproval, null, "approval_expired", pending.Id);
        }

        switch (decision.Outcome)
        {
            case ApprovalOutcome.Approved:
                this.Remove(key);
                return null;
            case ApprovalOutcome.Denied:
                return new ToolCallResult(ToolCallStatus.Denied, null, "approval_denied", null);
            case ApprovalOutcome.Pending:
            case ApprovalOutcome.Expired:
                return new ToolCallResult(ToolCallStatus.AwaitingApproval, null, null, pending.Id);
            default:
                return new ToolCallResult(ToolCallStatus.Failed, null, "gate_error", null);
        }
    }

    private PendingApproval? Find(string key)
    {
        lock (this._sync)
        {
            if (!this._pending.TryGetValue(key, out var approval))
            {
                return null;
            }

            if (approval.ExpiresAt > timeProvider.GetUtcNow())
            {
                return approval;
            }

            this._pending.Remove(key);
            return null;
        }
    }

    private PendingApproval? Open(string key, TimeSpan ttl)
    {
        lock (this._sync)
        {
            var now = timeProvider.GetUtcNow();
            if (this._pending.Count >= MaxPendingApprovals)
            {
                foreach (var expired in this._pending.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                {
                    this._pending.Remove(expired);
                }

                if (this._pending.Count >= MaxPendingApprovals)
                {
                    return null;
                }
            }

            var approval = new PendingApproval($"{key}-{Guid.CreateVersion7(now):N}", now + ttl);
            this._pending.Add(key, approval);
            return approval;
        }
    }

    private void Remove(string key)
    {
        lock (this._sync)
        {
            this._pending.Remove(key);
        }
    }

    private sealed record PendingApproval(string Id, DateTimeOffset ExpiresAt);
}
