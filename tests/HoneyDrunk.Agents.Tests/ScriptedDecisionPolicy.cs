using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Tests;

/// <summary>A decision policy that answers a set outcome and records what it was asked.</summary>
internal sealed class ScriptedDecisionPolicy : IDecisionPolicy
{
    private readonly List<ActionContext> asked = [];

    public PolicyOutcome Outcome { get; set; } = PolicyOutcome.Allow;

    public Exception? Throw { get; set; }

    public IReadOnlyList<ActionContext> Asked
    {
        get
        {
            lock (this.asked)
            {
                return [.. this.asked];
            }
        }
    }

    public Task<PolicyDecision> EvaluateAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        lock (this.asked)
        {
            this.asked.Add(context);
        }

        if (this.Throw is not null)
        {
            throw this.Throw;
        }

        return Task.FromResult(new PolicyDecision(this.Outcome, "scripted", []));
    }
}
