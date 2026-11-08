using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents.Testing;

/// <summary>
/// Creates Kernel agent contexts with a fixed start time and records each operation context it is given or creates. It is the
/// deterministic clock hook for executions run through the harness.
/// </summary>
public sealed class TestAgentExecutionContextFactory : IAgentExecutionContextFactory
{
    private readonly List<IOperationContext> operations = [];

    /// <summary>Gets the operation contexts created, in order.</summary>
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

    /// <inheritdoc />
    public IAgentExecutionContext Create(IAgentDescriptor agent, IGridContext gridContext, IOperationContext? operationContext = null, IReadOnlyDictionary<string, object?>? executionMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(gridContext);
        if (operationContext is null)
        {
            var created = new TestOperationContext(gridContext, "Agent:" + agent.AgentId);
            this.Record(created);
            operationContext = created;
        }
        else
        {
            this.Record(operationContext);
        }

        return new TestKernelAgentContext(agent, gridContext, operationContext, executionMetadata);
    }

    private void Record(IOperationContext operation)
    {
        lock (this.operations)
        {
            this.operations.Add(operation);
        }
    }
}
