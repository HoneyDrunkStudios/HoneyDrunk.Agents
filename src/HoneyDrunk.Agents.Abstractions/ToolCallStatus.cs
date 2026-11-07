namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The disposition of a tool invocation.</summary>
public enum ToolCallStatus
{
    /// <summary>The capability completed.</summary>
    Succeeded = 0,

    /// <summary>The agent was not registered for this capability.</summary>
    NotBound = 1,

    /// <summary>The registry did not resolve the capability.</summary>
    NotFound = 2,

    /// <summary>A policy or guard denied the call.</summary>
    Denied = 3,

    /// <summary>The call requires approval.</summary>
    AwaitingApproval = 4,

    /// <summary>A safety control prevented dispatch.</summary>
    Halted = 5,

    /// <summary>The execution exhausted its tool call allowance.</summary>
    LimitReached = 6,

    /// <summary>The capability rejected the arguments.</summary>
    InvalidArguments = 7,

    /// <summary>The invocation failed.</summary>
    Failed = 8,
}
