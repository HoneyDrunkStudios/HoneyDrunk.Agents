using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoneyDrunk.Agents;

/// <summary>
/// Tracks one open approval per tool request key, for one host. Opening is atomic, so concurrent proposals of the
/// same call raise one request. Each approval id authorizes at most one dispatch, and only before it expires. Ids
/// carry a time-ordered random nonce, so a restarted host never mints an id a persistent gate already holds. Request
/// keys cover the tenant and are keyed with a random per-host secret, so an approval never crosses tenants and a key
/// reveals nothing about the arguments. The ledger holds at most <see cref="DefaultCapacity"/> open approvals; when it
/// is full of live ones, new approvals are refused rather than dropping a live one. Thread-safe; register one per host.
/// </summary>
public sealed class ToolApprovalLedger
{
    /// <summary>The most open approvals a host keeps by default.</summary>
    public const int DefaultCapacity = 1024;

    private readonly TimeProvider timeProvider;
    private readonly int capacity;
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, OpenApproval> open = new(StringComparer.Ordinal);
    private readonly Lock sync = new();

    /// <summary>Initializes a new instance of the <see cref="ToolApprovalLedger"/> class.</summary>
    /// <param name="timeProvider">The clock.</param>
    public ToolApprovalLedger(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ToolApprovalLedger"/> class with a set capacity.</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="capacity">The most open approvals kept at once.</param>
    internal ToolApprovalLedger(TimeProvider timeProvider, int capacity)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    /// <summary>Computes the request key for a tool call under this host's secret.</summary>
    /// <param name="tenant">The grid tenant.</param>
    /// <param name="agentId">The agent id.</param>
    /// <param name="key">The tool key.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The request key.</returns>
    internal string RequestKeyFor(TenantId tenant, string agentId, CapabilityKey key, JsonElement arguments) =>
        ToolRequestKey.For(this.secret, tenant, agentId, key, arguments);

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
    /// <returns>The open approval id and whether this call opened it; a null id when the ledger is full.</returns>
    internal (string? approvalId, bool opened) GetOrOpen(string requestKey, DateTimeOffset expiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        lock (this.sync)
        {
            return this.Current(requestKey) is { } existing
                ? (approvalId: existing.ApprovalId, opened: false)
                : this.TryOpenLocked(requestKey, expiry);
        }
    }

    /// <summary>
    /// Replaces a stale approval with a new one, but only while the key still names <paramref name="staleApprovalId"/>
    /// or has none. Otherwise returns the approval another caller already opened.
    /// </summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="staleApprovalId">The approval id found to be stale.</param>
    /// <param name="expiry">When a newly opened approval stops being open.</param>
    /// <returns>The open approval id and whether this call opened it; a null id when the ledger is full.</returns>
    internal (string? approvalId, bool opened) Reopen(string requestKey, string staleApprovalId, DateTimeOffset expiry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(staleApprovalId);
        lock (this.sync)
        {
            var current = this.Current(requestKey);
            if (current is not null && !string.Equals(current.ApprovalId, staleApprovalId, StringComparison.Ordinal))
            {
                return (approvalId: current.ApprovalId, opened: false);
            }

            this.open.Remove(requestKey);
            return this.TryOpenLocked(requestKey, expiry);
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

    /// <summary>
    /// Removes the key's open approval only while it still names <paramref name="approvalId"/> and has not expired,
    /// so a gate reply that arrives after expiry never authorizes a dispatch.
    /// </summary>
    /// <param name="requestKey">The request key.</param>
    /// <param name="approvalId">The approval id to consume.</param>
    /// <returns>True at most once per approval id, and never after it expires.</returns>
    internal bool TryConsume(string requestKey, string approvalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        lock (this.sync)
        {
            return this.Current(requestKey) is { } entry
                && string.Equals(entry.ApprovalId, approvalId, StringComparison.Ordinal)
                && this.open.Remove(requestKey);
        }
    }

    private OpenApproval? Current(string requestKey) =>
        this.open.TryGetValue(requestKey, out var entry) && entry.Expiry > this.timeProvider.GetUtcNow() ? entry : null;

    private (string? approvalId, bool opened) TryOpenLocked(string requestKey, DateTimeOffset expiry)
    {
        var now = this.timeProvider.GetUtcNow();
        if (this.open.Count >= this.capacity)
        {
            foreach (var expired in this.open.Where(pair => pair.Value.Expiry <= now).Select(pair => pair.Key).ToArray())
            {
                this.open.Remove(expired);
            }

            if (this.open.Count >= this.capacity)
            {
                return (approvalId: null, opened: false);
            }
        }

        var approvalId = requestKey + "-" + Guid.CreateVersion7(now).ToString("N");
        this.open[requestKey] = new OpenApproval(approvalId, expiry);
        return (approvalId, opened: true);
    }

    private sealed class OpenApproval(string approvalId, DateTimeOffset expiry)
    {
        public string ApprovalId { get; } = approvalId;

        public DateTimeOffset Expiry { get; } = expiry;

        public bool Requested { get; set; }
    }
}
