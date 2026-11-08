namespace HoneyDrunk.Agents.Abstractions;

/// <summary>How a tool call ended.</summary>
public enum ToolCallStatus
{
    /// <summary>The tool ran and succeeded.</summary>
    Succeeded = 0,

    /// <summary>The agent is not bound to this tool at this version.</summary>
    NotBound = 1,

    /// <summary>No tool is registered under this name and version.</summary>
    NotFound = 2,

    /// <summary>Policy, an approver or authorization denied the call.</summary>
    Denied = 3,

    /// <summary>The call waits for a human approval.</summary>
    AwaitingApproval = 4,

    /// <summary>A circuit breaker stopped the call.</summary>
    Halted = 5,

    /// <summary>The execution reached its tool-call limit.</summary>
    LimitReached = 6,

    /// <summary>The tool rejected the arguments.</summary>
    InvalidArguments = 7,

    /// <summary>The call failed.</summary>
    Failed = 8,
}
