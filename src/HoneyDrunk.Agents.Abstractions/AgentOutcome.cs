namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The disposition of an agent execution.</summary>
public enum AgentOutcome
{
    /// <summary>The agent completed its request.</summary>
    Completed = 0,

    /// <summary>A request restriction prevented execution.</summary>
    Rejected = 1,

    /// <summary>An operation needs approval.</summary>
    AwaitingApproval = 2,

    /// <summary>A host safety control stopped execution.</summary>
    Halted = 3,

    /// <summary>The agent or runtime failed.</summary>
    Failed = 4,
}
