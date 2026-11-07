using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;
using IAgentExecutionContext = HoneyDrunk.Agents.Abstractions.IAgentExecutionContext;

namespace HoneyDrunk.Agents.Testing;

/// <summary>
/// An execution context for calling an agent directly in a unit test, without the lifecycle. It starts at
/// <see cref="FixedStart"/> and uses in-memory tools and memory unless others are given.
/// </summary>
public sealed class TestAgentExecutionContext : IAgentExecutionContext
{
    private readonly Dictionary<string, object?> metadata = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="TestAgentExecutionContext"/> class.</summary>
    /// <param name="agent">The agent descriptor.</param>
    /// <param name="tools">The tools; an empty <see cref="InMemoryToolInvoker"/> by default.</param>
    /// <param name="memory">The memory; an empty <see cref="InMemoryAgentMemory"/> by default.</param>
    /// <param name="grants">The grants; none by default.</param>
    /// <param name="toolBindings">The tool bindings; none by default.</param>
    public TestAgentExecutionContext(
        IAgentDescriptor agent,
        IToolInvoker? tools = null,
        IAgentMemory? memory = null,
        AgentGrants? grants = null,
        IReadOnlyList<AgentToolBinding>? toolBindings = null)
    {
        this.Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        this.Tools = tools ?? new InMemoryToolInvoker();
        this.Memory = memory ?? new InMemoryAgentMemory();
        this.Grants = grants ?? AgentGrants.None;
        this.ToolBindings = toolBindings ?? [];
        this.GridContext = new TestGridContext(TenantId.Internal);
        this.OperationContext = new TestOperationContext(this.GridContext, "Agent:" + agent.AgentId);
    }

    /// <summary>Gets the start time every test context reports.</summary>
    public static DateTimeOffset FixedStart { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public string ExecutionId { get; init; } = "test-execution";

    /// <inheritdoc />
    public AgentGrants Grants { get; }

    /// <inheritdoc />
    public IReadOnlyList<AgentToolBinding> ToolBindings { get; }

    /// <inheritdoc />
    public IToolInvoker Tools { get; }

    /// <inheritdoc />
    public IAgentMemory Memory { get; }

    /// <inheritdoc />
    public IAgentDescriptor Agent { get; }

    /// <inheritdoc />
    public IGridContext GridContext { get; }

    /// <inheritdoc />
    public IOperationContext OperationContext { get; }

    /// <inheritdoc />
    public DateTimeOffset StartedAtUtc => FixedStart;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ExecutionMetadata => this.metadata;

    /// <inheritdoc />
    public void AddMetadata(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.metadata[key] = value;
    }

    /// <inheritdoc />
    public bool CanAccess(string resourceType, string resourceId) => false;
}
