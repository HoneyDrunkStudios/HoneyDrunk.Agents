namespace HoneyDrunk.Agents.Tests;

/// <summary>Records lifecycle transitions.</summary>
internal sealed class RecordingObserver : IAgentLifecycleObserver
{
    private readonly List<AgentLifecycleTransition> transitions = [];

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

    public bool Throw { get; set; }

    public void OnTransition(AgentLifecycleTransition transition)
    {
        lock (this.transitions)
        {
            this.transitions.Add(transition);
        }

        if (this.Throw)
        {
            throw new InvalidOperationException("observer failed");
        }
    }
}
