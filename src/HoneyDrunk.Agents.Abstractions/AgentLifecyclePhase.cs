namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Where a registered agent is in its lifecycle.</summary>
public enum AgentLifecyclePhase
{
    /// <summary>Registered, not yet initialized.</summary>
    Registered = 0,

    /// <summary>Initialized; executions are accepted.</summary>
    Initialized = 1,

    /// <summary>Decommissioned; executions are refused.</summary>
    Decommissioned = 2,
}
