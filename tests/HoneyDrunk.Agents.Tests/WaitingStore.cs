using HoneyDrunk.Memory.Abstractions;

namespace HoneyDrunk.Agents.Tests;

/// <summary>A memory store whose calls wait until their token is cancelled.</summary>
internal sealed class WaitingStore : IMemoryStore
{
    public async Task<MemoryEntry> WriteAsync(IMemoryScope scope, NewMemoryEntry entry, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    public Task<MemoryEntry?> GetAsync(IMemoryScope scope, string entryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public async Task<IReadOnlyList<MemoryEntry>> ReadAsync(IMemoryScope scope, MemoryQuery query, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    public Task<MemoryEntry> WriteSummaryAsync(IMemoryScope scope, NewMemoryEntry summary, IReadOnlyList<string> supersededIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(IMemoryScope scope, string entryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> DeleteAllAsync(IMemoryScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
