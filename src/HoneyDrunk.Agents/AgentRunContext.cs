using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents;

internal sealed class AgentRunContext(
    IAgentExecutionContext inner,
    string executionId,
    AgentGrants grants,
    IReadOnlyList<AgentToolBinding> bindings,
    IToolInvoker tools,
    IAgentMemory memory,
    ExecutionLease lease) : IAgentRunContext
{
    public IAgentDescriptor Agent => inner.Agent;

    public IGridContext GridContext => inner.GridContext;

    public IOperationContext OperationContext => inner.OperationContext;

    public DateTimeOffset StartedAtUtc => inner.StartedAtUtc;

    public IReadOnlyDictionary<string, object?> ExecutionMetadata
    {
        get
        {
            lease.EnsureActive();
            return inner.ExecutionMetadata;
        }
    }

    public string ExecutionId => executionId;

    public AgentGrants Grants => grants;

    public IReadOnlyList<AgentToolBinding> ToolBindings => bindings;

    public IToolInvoker Tools
    {
        get
        {
            lease.EnsureActive();
            return tools;
        }
    }

    public IAgentMemory Memory
    {
        get
        {
            lease.EnsureActive();
            return memory;
        }
    }

    public void AddMetadata(string key, object? value)
    {
        lease.EnsureActive();
        inner.AddMetadata(key, value);
    }

    public bool CanAccess(string resourceType, string resourceId)
    {
        lease.EnsureActive();
        return inner.CanAccess(resourceType, resourceId);
    }
}
