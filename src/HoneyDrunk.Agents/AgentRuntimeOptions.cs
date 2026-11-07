using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents;

/// <summary>Host settings for execution and approval limits.</summary>
public sealed class AgentRuntimeOptions
{
    /// <summary>Gets or sets the maximum tool requests per execution.</summary>
    public int MaxToolCallsPerExecution { get; set; } = 8;

    /// <summary>Gets or sets the deadline for cooperative execution.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how long a tool approval request is valid.</summary>
    public TimeSpan ToolApprovalTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets exception types that must reach the owning host unchanged.</summary>
    public IReadOnlyList<Type> PropagatedExceptionTypes { get; set; } = [];

    /// <summary>Gets or sets a factory for an operation over the initialized Grid context from the calling host.</summary>
    /// <remarks>Use this when Kernel's operation factory would otherwise resolve an uninitialized context from the child service scope.</remarks>
    public Func<IGridContext, string, IOperationContext>? CreateOperationContext { get; set; }

    internal AgentRuntimeOptions Snapshot()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.MaxToolCallsPerExecution);
        var maxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        if (this.ExecutionTimeout <= TimeSpan.Zero || this.ExecutionTimeout > maxDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(this.ExecutionTimeout));
        }

        if (this.ToolApprovalTtl <= TimeSpan.Zero || this.ToolApprovalTtl > maxDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(this.ToolApprovalTtl));
        }

        ArgumentNullException.ThrowIfNull(this.PropagatedExceptionTypes);
        var exceptions = this.PropagatedExceptionTypes.ToArray();
        if (exceptions.Any(type => type is null || !typeof(Exception).IsAssignableFrom(type)))
        {
            throw new ArgumentException("Only exception types may be propagated.", nameof(this.PropagatedExceptionTypes));
        }

        return new AgentRuntimeOptions
        {
            MaxToolCallsPerExecution = this.MaxToolCallsPerExecution,
            ExecutionTimeout = this.ExecutionTimeout,
            ToolApprovalTtl = this.ToolApprovalTtl,
            PropagatedExceptionTypes = Array.AsReadOnly(exceptions),
            CreateOperationContext = this.CreateOperationContext,
        };
    }
}
