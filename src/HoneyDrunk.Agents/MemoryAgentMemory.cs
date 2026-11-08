using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory.Abstractions;
using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents;

/// <summary>
/// One execution's memory over the Memory node. Writes go through the home scope; reads go through the read scope.
/// The Memory node validates entries and checks scopes. Telemetry and logs carry the operation and a count only.
/// </summary>
internal sealed class MemoryAgentMemory : IAgentMemory, IDisposable
{
    private readonly IMemoryStore store;
    private readonly IMemoryScope home;
    private readonly IMemoryScope read;
    private readonly ExecutionIdentity identity;
    private readonly ITelemetryActivityFactory telemetry;
    private readonly ILogger logger;
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;

    /// <summary>Initializes a new instance of the <see cref="MemoryAgentMemory"/> class.</summary>
    /// <param name="store">The scope-checked memory store.</param>
    /// <param name="home">The home scope.</param>
    /// <param name="read">The read scope.</param>
    /// <param name="identity">Who the execution acts as.</param>
    /// <param name="telemetry">Kernel's activity factory.</param>
    /// <param name="logger">The logger.</param>
    public MemoryAgentMemory(IMemoryStore store, IMemoryScope home, IMemoryScope read, ExecutionIdentity identity, ITelemetryActivityFactory telemetry, ILogger logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.home = home ?? throw new ArgumentNullException(nameof(home));
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AgentMemoryItem> RememberAsync(AgentMemoryNote note, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(note);
        using var activity = this.Start("remember");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token);
        var entry = await this.store.WriteAsync(this.home, new NewMemoryEntry(note.Kind, note.Content, note.Tags), linked.Token).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.MemoryResultCountTag, 1);
        AgentLog.MemoryOperation(this.logger, this.identity.AgentId, "remember", 1);
        return this.Map(entry);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMemoryItem>> RecallAsync(AgentRecall recall, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(recall);
        using var activity = this.Start("recall");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.lifetime.Token);
        var entries = await this.store.ReadAsync(this.read, new MemoryQuery(recall.Limit, recall.Kind, recall.AllTags), linked.Token).ConfigureAwait(false);
        activity?.SetTag(AgentTelemetry.MemoryResultCountTag, entries.Count);
        AgentLog.MemoryOperation(this.logger, this.identity.AgentId, "recall", entries.Count);
        return entries.Select(this.Map).ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // An in-flight call is cancelled, so nothing is written after the execution ends.
        if (Interlocked.Exchange(ref this.disposed, 1) == 0)
        {
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }
    }

    private AgentMemoryItem Map(MemoryEntry entry) =>
        new(entry.Id, entry.Kind, entry.Content, entry.Tags, entry.WrittenAt, entry.Scope == this.home.Home);

    private System.Diagnostics.Activity? Start(string operation) =>
        this.telemetry.Start(
            AgentTelemetry.MemoryActivity,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [AgentTelemetry.AgentIdTag] = this.identity.AgentId,
                [AgentTelemetry.ExecutionIdTag] = this.identity.ExecutionId,
                [AgentTelemetry.MemoryOperationTag] = operation,
            });
}
