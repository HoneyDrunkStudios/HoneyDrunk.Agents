namespace HoneyDrunk.Agents;

/// <summary>Sees each lifecycle transition. An observer that throws is logged and ignored.</summary>
public interface IAgentLifecycleObserver
{
    /// <summary>Called after a transition.</summary>
    /// <param name="transition">The transition.</param>
    void OnTransition(AgentLifecycleTransition transition);
}
