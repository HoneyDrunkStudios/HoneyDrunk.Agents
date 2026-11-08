using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents;

/// <summary>
/// One execution's context: Kernel's agent context plus the execution's bindings. After disposal, tools and memory
/// throw <see cref="ObjectDisposedException"/>, so an agent that keeps the context cannot act after it completes.
/// </summary>
internal sealed class AgentRunContext : IAgentRunContext, IDisposable
{
    private readonly HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext inner;
    private readonly CapabilityToolInvoker tools;
    private readonly MemoryAgentMemory memory;
    private int disposed;

    /// <summary>Initializes a new instance of the <see cref="AgentRunContext"/> class.</summary>
    /// <param name="inner">Kernel's agent context.</param>
    /// <param name="identity">Who the execution acts as.</param>
    /// <param name="tools">The execution's tool invoker.</param>
    /// <param name="memory">The execution's memory.</param>
    public AgentRunContext(
        HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext inner,
        ExecutionIdentity identity,
        CapabilityToolInvoker tools,
        MemoryAgentMemory memory)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(identity);
        this.tools = tools ?? throw new ArgumentNullException(nameof(tools));
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.AgentIdForLogs = identity.AgentId;
        this.ExecutionId = identity.ExecutionId;
        this.Grants = identity.Grants;
        this.ToolBindings = identity.Bindings;
    }

    /// <inheritdoc />
    public string ExecutionId { get; }

    /// <inheritdoc />
    public AgentGrants Grants { get; }

    /// <inheritdoc />
    public IReadOnlyList<AgentToolBinding> ToolBindings { get; }

    /// <inheritdoc />
    public IToolInvoker Tools
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
            return this.tools;
        }
    }

    /// <inheritdoc />
    public IAgentMemory Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
            return this.memory;
        }
    }

    /// <inheritdoc />
    public IAgentDescriptor Agent => this.inner.Agent;

    /// <inheritdoc />
    public IGridContext GridContext => this.inner.GridContext;

    /// <inheritdoc />
    public IOperationContext OperationContext => this.inner.OperationContext;

    /// <inheritdoc />
    public DateTimeOffset StartedAtUtc => this.inner.StartedAtUtc;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ExecutionMetadata => this.inner.ExecutionMetadata;

    /// <summary>Gets the registered agent id, which never changes during the execution.</summary>
    internal string AgentIdForLogs { get; }

    /// <inheritdoc />
    public void AddMetadata(string key, object? value) => this.inner.AddMetadata(key, value);

    /// <inheritdoc />
    public bool CanAccess(string resourceType, string resourceId) => this.inner.CanAccess(resourceType, resourceId);

    /// <summary>Ends the execution's use of tools and memory.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 0)
        {
            this.tools.Dispose();
            this.memory.Dispose();
        }
    }
}
