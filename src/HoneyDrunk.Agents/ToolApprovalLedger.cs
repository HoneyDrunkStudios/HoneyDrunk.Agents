using System.Collections.Concurrent;

namespace HoneyDrunk.Agents;

/// <summary>
/// Tracks one open approval per tool request key. Each approval id authorizes at most one dispatch. Ids carry a
/// time-ordered random nonce, so a restarted host never mints an id a persistent gate already holds. Thread-safe;
/// register one per host.
/// </summary>
/// <param name="timeProvider">The clock.</param>
public sealed class ToolApprovalLedger(TimeProvider timeProvider)
{
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ConcurrentDictionary<string, OpenApproval> open = new(StringComparer.Ordinal);

    /// <summary>Gets the key's open approval id, or null when there is none or it has expired.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <returns>The open approval id, or null.</returns>
    public string? OpenApprovalFor(string requestKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        return this.open.TryGetValue(requestKey, out var entry) && entry.Expiry > this.timeProvider.GetUtcNow()
            ? entry.ApprovalId
            : null;
    }

    /// <summary>Makes a new approval id and records it as the key's open approval, replacing any older one.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="expiry">When the approval stops being open.</param>
    /// <returns>The new approval id.</returns>
    public string Open(string requestKey, DateTimeOffset expiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        var now = this.timeProvider.GetUtcNow();
        this.RemoveExpired(now);
        var approvalId = requestKey + "-" + Guid.CreateVersion7(now).ToString("N");
        this.open[requestKey] = new OpenApproval(approvalId, expiry);
        return approvalId;
    }

    /// <summary>Removes the key's open approval only while it still names <paramref name="approvalId"/>.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id to consume.</param>
    /// <returns>True at most once per approval id.</returns>
    public bool TryConsume(string requestKey, string approvalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        return this.open.TryGetValue(requestKey, out var entry)
            && string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal)
            && this.open.TryRemove(KeyValuePair.Create(requestKey, entry));
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var pair in this.open)
        {
            if (pair.Value.Expiry <= now)
            {
                this.open.TryRemove(pair);
            }
        }
    }

    private sealed record OpenApproval(string ApprovalId, DateTimeOffset Expiry);
}
