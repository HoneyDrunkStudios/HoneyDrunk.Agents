namespace HoneyDrunk.Agents;

/// <summary>Observes safe lifecycle metadata inside the owning host.</summary>
public interface IAgentLifecycleObserver
{
    /// <summary>Observes a lifecycle transition. Implementations may be called concurrently.</summary>
    /// <param name="transition">The metadata for the transition.</param>
    void OnTransition(AgentLifecycleTransition transition);
}
