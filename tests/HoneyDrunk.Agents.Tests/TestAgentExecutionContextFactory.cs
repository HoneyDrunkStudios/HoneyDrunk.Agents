using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Creates Kernel agent contexts with a recorded operation context.</summary>
internal sealed class TestAgentExecutionContextFactory : IAgentExecutionContextFactory
{
    private readonly List<IOperationContext> operations = [];

    public IReadOnlyList<IOperationContext> Operations
    {
        get
        {
            lock (this.operations)
            {
                return [.. this.operations];
            }
        }
    }

    public IAgentExecutionContext Create(IAgentDescriptor agent, IGridContext gridContext, IOperationContext? operationContext = null, IReadOnlyDictionary<string, object?>? executionMetadata = null)
    {
        var operation = operationContext ?? new TestOperationContext(gridContext, "Agent:" + agent.AgentId);
        lock (this.operations)
        {
            this.operations.Add(operation);
        }

        return new KernelContext(agent, gridContext, operation, executionMetadata);
    }

    private sealed class KernelContext(IAgentDescriptor agent, IGridContext gridContext, IOperationContext operationContext, IReadOnlyDictionary<string, object?>? metadata) : IAgentExecutionContext
    {
        private readonly Dictionary<string, object?> metadata = metadata is null ? new(StringComparer.Ordinal) : new(metadata, StringComparer.Ordinal);

        public IAgentDescriptor Agent { get; } = agent;

        public IGridContext GridContext { get; } = gridContext;

        public IOperationContext OperationContext { get; } = operationContext;

        public DateTimeOffset StartedAtUtc => DateTimeOffset.UnixEpoch;

        public IReadOnlyDictionary<string, object?> ExecutionMetadata => this.metadata;

        public void AddMetadata(string key, object? value) => this.metadata[key] = value;

        public bool CanAccess(string resourceType, string resourceId) => false;
    }
}
