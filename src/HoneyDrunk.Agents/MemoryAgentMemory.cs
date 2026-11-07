using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Memory.Abstractions;

namespace HoneyDrunk.Agents;

internal sealed class MemoryAgentMemory(
    IMemoryStore store,
    IMemoryScope home,
    IMemoryScope read,
    ExecutionLease lease) : IAgentMemory
{
    public async Task<AgentMemoryItem> RememberAsync(AgentMemoryNote note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        using var linked = lease.Link(cancellationToken);
        var entry = await store.WriteAsync(home, new NewMemoryEntry(note.Kind, note.Content, note.Tags), linked.Token).ConfigureAwait(false);
        return this.Project(entry);
    }

    public async Task<IReadOnlyList<AgentMemoryItem>> RecallAsync(AgentRecall recall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recall);
        using var linked = lease.Link(cancellationToken);
        var entries = await store.ReadAsync(read, new MemoryQuery(recall.Limit, recall.Kind, recall.AllTags), linked.Token).ConfigureAwait(false);
        return entries.Select(this.Project).ToArray();
    }

    private AgentMemoryItem Project(MemoryEntry entry) =>
        new(entry.Id, entry.Kind, entry.Content, entry.Tags, entry.WrittenAt, entry.Scope == home.Home);
}
