namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Manages registrations and executions within a host.</summary>
public interface IAgentLifecycle
{
    /// <summary>Registers an agent and the maximum access this host grants it.</summary>
    /// <param name="registration">The agent, grants, and tool bindings.</param>
    void Register(AgentRegistration registration);

    /// <summary>Initializes a registration so it can execute requests.</summary>
    /// <param name="agentId">The registration ID.</param>
    /// <param name="cancellationToken">The initialization cancellation token.</param>
    /// <returns>The initialization operation.</returns>
    Task InitializeAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Runs a request through an initialized registration.</summary>
    /// <param name="agentId">The registration ID.</param>
    /// <param name="request">The workload request.</param>
    /// <param name="options">The restrictions on this execution.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<AgentResult> ExecuteAsync(string agentId, AgentRequest request, AgentExecutionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Retires a registration so it can no longer start executions.</summary>
    /// <param name="agentId">The registration ID.</param>
    /// <param name="cancellationToken">The decommission cancellation token.</param>
    /// <returns>The decommission operation.</returns>
    Task DecommissionAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current phase of a registration.</summary>
    /// <param name="agentId">The registration ID.</param>
    /// <returns>The registration phase.</returns>
    AgentLifecyclePhase GetPhase(string agentId);
}
