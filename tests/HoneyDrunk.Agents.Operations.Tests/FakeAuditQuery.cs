using HoneyDrunk.Audit.Abstractions;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>An in-memory audit source with separate legacy and bounded capabilities and explicit failure modes.</summary>
internal sealed class FakeAuditQuery : IBoundedAuditQuery
{
    private readonly List<AuditEntry> entries = [];
    private readonly List<AuditQueryFilter> filters = [];

    public bool Throw { get; set; }

    public bool IgnoreTarget { get; set; }

    public bool IgnoreLimit { get; set; }

    public int? FailOnCall { get; set; }

    public int LegacyReads { get; private set; }

    public IReadOnlyList<AuditQueryFilter> Filters => this.filters;

    public List<int> ReturnedSizes { get; } = [];

    public void Add(AuditEntry entry) => this.entries.Add(entry);

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        this.LegacyReads++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AuditEntry>>([.. this.Matching(filter).OrderBy(entry => entry.OccurredAt)]);
    }

    public Task<IReadOnlyList<AuditEntry>> ReadLatestAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.filters.Add(filter);
        if (this.Throw || this.FailOnCall == this.filters.Count)
        {
            throw new InvalidOperationException("audit-secret");
        }

        if (filter.Limit is not > 0)
        {
            throw new ArgumentException("A bounded audit query needs a positive limit.", nameof(filter));
        }

        var ordered = this.Matching(filter).OrderByDescending(entry => entry.OccurredAt).ThenByDescending(entry => entry.Id.Value, StringComparer.Ordinal);
        IReadOnlyList<AuditEntry> page = [.. this.IgnoreLimit ? ordered : ordered.Take(filter.Limit.Value)];
        this.ReturnedSizes.Add(page.Count);
        return Task.FromResult(page);
    }

    private IEnumerable<AuditEntry> Matching(AuditQueryFilter filter) => this.entries
        .Where(entry => entry.OccurredAt >= filter.Since && entry.OccurredAt <= filter.Until)
        .Where(entry => filter.Actor is null || entry.Actor == filter.Actor)
        .Where(entry => filter.Category is null || entry.Category == filter.Category)
        .Where(entry => filter.EventName is null || entry.EventName == filter.EventName)
        .Where(entry => this.IgnoreTarget || filter.TargetType is null || entry.Target.Type == filter.TargetType)
        .Where(entry => this.IgnoreTarget || filter.TargetId is null || entry.Target.Id == filter.TargetId)
        .Where(entry => filter.CorrelationId is null || entry.CorrelationId == filter.CorrelationId)
        .Where(entry => filter.TenantId is null || entry.TenantId == filter.TenantId);
}
