namespace HoneyDrunk.Agents.Testing;

/// <summary>Records lifecycle transitions: the execution log for assertions.</summary>
public sealed class RecordingLifecycleObserver : IAgentLifecycleObserver
{
    private readonly List<AgentLifecycleTransition> transitions = [];

    /// <summary>Gets the transitions seen, in order.</summary>
    public IReadOnlyList<AgentLifecycleTransition> Transitions
    {
        get
        {
            lock (this.transitions)
            {
                return [.. this.transitions];
            }
        }
    }

    /// <inheritdoc />
    public void OnTransition(AgentLifecycleTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (this.transitions)
        {
            this.transitions.Add(transition);
        }
    }
}
