namespace HoneyDrunk.Agents.Abstractions;

/// <summary>How an execution ended.</summary>
public enum AgentOutcome
{
    /// <summary>The agent ran and completed.</summary>
    Completed = 0,

    /// <summary>The execution was refused before or by the agent.</summary>
    Rejected = 1,

    /// <summary>A tool call waits for a human approval.</summary>
    AwaitingApproval = 2,

    /// <summary>A circuit breaker stopped the execution.</summary>
    Halted = 3,

    /// <summary>The execution failed.</summary>
    Failed = 4,
}
