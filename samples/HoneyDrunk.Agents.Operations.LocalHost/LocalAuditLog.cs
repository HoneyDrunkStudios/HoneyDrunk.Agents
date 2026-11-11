using HoneyDrunk.Audit.Abstractions;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>An audit log kept in memory, so the entries Operator writes can be read back through the audit route.</summary>
/// <param name="timeProvider">Stamps an entry that arrives without a time.</param>
public sealed class LocalAuditLog(TimeProvider timeProvider) : IAuditLog, IAuditQuery
{
    private readonly Lock gate = new();
    private readonly List<AuditEntry> entries = [];

    /// <inheritdoc />
    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var normalized = entry.NormalizeForAppend(timeProvider.GetUtcNow());
        lock (this.gate)
        {
            this.entries.Add(normalized);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ReadAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        AuditEntry[] snapshot;
        lock (this.gate)
        {
            snapshot = [.. this.entries];
        }

        var matching = snapshot
            .Where(entry => entry.OccurredAt >= filter.Since && entry.OccurredAt <= filter.Until)
            .Where(entry => filter.Actor is null || entry.Actor == filter.Actor)
            .Where(entry => filter.Category is null || entry.Category == filter.Category)
            .Where(entry => filter.EventName is null || entry.EventName == filter.EventName)
            .Where(entry => filter.TargetType is null || entry.Target.Type == filter.TargetType)
            .Where(entry => filter.TargetId is null || entry.Target.Id == filter.TargetId)
            .Where(entry => filter.CorrelationId is null || entry.CorrelationId == filter.CorrelationId)
            .Where(entry => filter.TenantId is null || entry.TenantId == filter.TenantId)
            .OrderBy(entry => entry.OccurredAt);
        IReadOnlyList<AuditEntry> page = filter.Limit is { } limit ? [.. matching.Take(limit)] : [.. matching];
        return Task.FromResult(page);
    }
}
