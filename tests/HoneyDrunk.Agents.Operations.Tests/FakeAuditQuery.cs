using HoneyDrunk.Audit.Abstractions;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>An audit query over a list, earliest first, as the contract says. It can be made to fail.</summary>
internal sealed class FakeAuditQuery : IAuditQuery
{
    private readonly List<AuditEntry> entries = [];
    private readonly List<AuditQueryFilter> filters = [];

    public bool Throw { get; set; }

    /// <summary>Gets or sets a value indicating whether the query ignores the target filter, as a faulty source might.</summary>
    public bool IgnoreTarget { get; set; }

    public IReadOnlyList<AuditQueryFilter> Filters => this.filters;

    public void Add(AuditEntry entry) => this.entries.Add(entry);

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        if (this.Throw)
        {
            throw new InvalidOperationException("audit-secret");
        }

        this.filters.Add(filter);
        IReadOnlyList<AuditEntry> matching =
        [
            .. this.entries
                .Where(entry => entry.OccurredAt >= filter.Since && entry.OccurredAt <= filter.Until)
                .Where(entry => this.IgnoreTarget || filter.TargetId is null || entry.Target.Id == filter.TargetId)
                .OrderBy(entry => entry.OccurredAt),
        ];
        return Task.FromResult(matching);
    }
}
