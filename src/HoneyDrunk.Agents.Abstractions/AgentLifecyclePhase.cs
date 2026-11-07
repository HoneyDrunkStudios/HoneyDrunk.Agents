namespace HoneyDrunk.Agents.Abstractions;

/// <summary>The phase of a registered agent.</summary>
public enum AgentLifecyclePhase
{
    /// <summary>The host registered the agent.</summary>
    Registered = 0,

    /// <summary>The agent can accept executions.</summary>
    Initialized = 1,

    /// <summary>The host retired the registration.</summary>
    Decommissioned = 2,
}
