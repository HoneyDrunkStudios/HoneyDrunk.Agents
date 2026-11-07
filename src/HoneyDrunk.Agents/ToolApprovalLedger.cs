using HoneyDrunk.Capabilities.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoneyDrunk.Agents;

/// <summary>
/// Tracks one open approval per tool request key, for one host. Opening is atomic, so concurrent proposals of the
/// same call raise one request. Each approval id authorizes at most one dispatch. Ids carry a time-ordered random
/// nonce, so a restarted host never mints an id a persistent gate already holds. Request keys are keyed with a
/// random per-host secret, so they reveal nothing about the arguments. Thread-safe; register one per host.
/// </summary>
/// <param name="timeProvider">The clock.</param>
public sealed class ToolApprovalLedger(TimeProvider timeProvider)
{
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, OpenApproval> open = new(StringComparer.Ordinal);
    private readonly Lock sync = new();

    /// <summary>Computes the request key for a tool call under this host's secret.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="key">The tool key.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The request key.</returns>
    internal string RequestKeyFor(string agentId, CapabilityKey key, JsonElement arguments) => ToolRequestKey.For(this.secret, agentId, key, arguments);

    /// <summary>Gets the key's open approval id, or null when there is none or it has expired.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <returns>The open approval id, or null.</returns>
    internal string? OpenApprovalFor(string requestKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        lock (this.sync)
        {
            return this.Current(requestKey)?.ApprovalId;
        }
    }

    /// <summary>Returns the key's open approval, or opens one. Only the caller that opened it should raise a request.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="expiry">When a newly opened approval stops being open.</param>
    /// <returns>The open approval id, and whether this call opened it.</returns>
    internal (string approvalId, bool opened) GetOrOpen(string requestKey, DateTimeOffset expiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        lock (this.sync)
        {
            return this.Current(requestKey) is { } existing ? (approvalId: existing.ApprovalId, opened: false) : (approvalId: this.OpenLocked(requestKey, expiry), opened: true);
        }
    }

    /// <summary>
    /// Replaces a stale approval with a new one, but only while the key still names <paramref name="staleApprovalId"/>
    /// or has none. Otherwise returns the approval another caller already opened.
    /// </summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="staleApprovalId">The approval id found to be stale.</param>
    /// <param name="expiry">When a newly opened approval stops being open.</param>
    /// <returns>The open approval id, and whether this call opened it.</returns>
    internal (string approvalId, bool opened) Reopen(string requestKey, string staleApprovalId, DateTimeOffset expiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(staleApprovalId);
        lock (this.sync)
        {
            var current = this.Current(requestKey);
            return current is not null && !string.Equals(current.ApprovalId, staleApprovalId, StringComparison.Ordinal)
                ? (approvalId: current.ApprovalId, opened: false)
                : (approvalId: this.OpenLocked(requestKey, expiry), opened: true);
        }
    }

    /// <summary>
    /// Gets whether the gate has the request for this approval. False only while the caller that opened it is still
    /// raising it, so others wait instead of reading an unknown id as stale.
    /// </summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id.</param>
    /// <returns>False while the request is being raised.</returns>
    internal bool IsRequested(string requestKey, string approvalId)
    {
        lock (this.sync)
        {
            return this.Current(requestKey) is not { } entry
                || !string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal)
                || entry.Requested;
        }
    }

    /// <summary>Records that the gate has received the request for an open approval.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id.</param>
    internal void MarkRequested(string requestKey, string approvalId)
    {
        lock (this.sync)
        {
            if (this.open.TryGetValue(requestKey, out var entry) && string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal))
            {
                entry.Requested = true;
            }
        }
    }

    /// <summary>Drops an open approval whose request never reached the gate, so the next proposal tries again.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id.</param>
    internal void Abandon(string requestKey, string approvalId)
    {
        lock (this.sync)
        {
            if (this.open.TryGetValue(requestKey, out var entry) && string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal))
            {
                this.open.Remove(requestKey);
            }
        }
    }

    /// <summary>Removes the key's open approval only while it still names <paramref name="approvalId"/>.</summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id to consume.</param>
    /// <returns>True at most once per approval id.</returns>
    internal bool TryConsume(string requestKey, string approvalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        lock (this.sync)
        {
            return this.open.TryGetValue(requestKey, out var entry)
                && string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal)
                && this.open.Remove(requestKey);
        }
    }

    private OpenApproval? Current(string requestKey) =>
        this.open.TryGetValue(requestKey, out var entry) && entry.Expiry > this.timeProvider.GetUtcNow() ? entry : null;

    private string OpenLocked(string requestKey, DateTimeOffset expiry)
    {
        var now = this.timeProvider.GetUtcNow();
        foreach (var expired in this.open.Where(pair => pair.Value.Expiry <= now).Select(pair => pair.Key).ToArray())
        {
            this.open.Remove(expired);
        }

        var approvalId = requestKey + "-" + Guid.CreateVersion7(now).ToString("N");
        this.open[requestKey] = new OpenApproval(approvalId, expiry);
        return approvalId;
    }

    private sealed class OpenApproval(string approvalId, DateTimeOffset expiry)
    {
        public string ApprovalId { get; } = approvalId;

        public DateTimeOffset Expiry { get; } = expiry;

        public bool Requested { get; set; }
    }
}
