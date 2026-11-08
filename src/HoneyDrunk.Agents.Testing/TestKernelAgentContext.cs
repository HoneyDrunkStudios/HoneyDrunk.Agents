using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents.Testing;

/// <summary>Kernel's agent context with a fixed start time.</summary>
/// <param name="agent">The agent descriptor.</param>
/// <param name="gridContext">The grid context.</param>
/// <param name="operationContext">The operation context.</param>
/// <param name="executionMetadata">Initial metadata, if any.</param>
internal sealed class TestKernelAgentContext(
    IAgentDescriptor agent,
    IGridContext gridContext,
    IOperationContext operationContext,
    IReadOnlyDictionary<string, object?>? executionMetadata) : IAgentExecutionContext
{
    private readonly Dictionary<string, object?> metadata = executionMetadata is null
        ? new(StringComparer.Ordinal)
        : new(executionMetadata, StringComparer.Ordinal);

    public IAgentDescriptor Agent { get; } = agent;

    public IGridContext GridContext { get; } = gridContext;

    public IOperationContext OperationContext { get; } = operationContext;

    public DateTimeOffset StartedAtUtc => TestAgentRunContext.FixedStart;

    public IReadOnlyDictionary<string, object?> ExecutionMetadata => this.metadata;

    public void AddMetadata(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.metadata[key] = value;
    }

    public bool CanAccess(string resourceType, string resourceId) => false;
}
