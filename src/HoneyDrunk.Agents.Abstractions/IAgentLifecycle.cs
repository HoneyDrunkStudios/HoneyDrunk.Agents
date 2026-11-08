namespace HoneyDrunk.Agents.Abstractions;

/// <summary>
/// Registers, initializes, runs and decommissions agents in one host. Host-facing, so it sits outside the
/// contract-shape canary (ADR-0020 D10). Each execution runs in its own dependency-injection scope.
/// </summary>
public interface IAgentLifecycle
{
    /// <summary>Registers an agent. The agent id must be unique in this host.</summary>
    /// <param name="registration">The registration.</param>
    void Register(AgentRegistration registration);

    /// <summary>Initializes a registered agent, checking that every bound tool exists.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>A task that completes when the agent accepts executions.</returns>
    Task InitializeAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Runs one execution of an initialized agent.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="request">What the execution is asked to do.</param>
    /// <param name="options">Per-execution settings.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The result.</returns>
    Task<AgentResult> ExecuteAsync(string agentId, AgentRequest request, AgentExecutionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Decommissions an agent; later executions are refused.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="cancellationToken">Cancels decommissioning.</param>
    /// <returns>A task that completes when the agent is decommissioned.</returns>
    Task DecommissionAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Gets an agent's phase.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <returns>The phase.</returns>
    AgentLifecyclePhase GetPhase(string agentId);
}
