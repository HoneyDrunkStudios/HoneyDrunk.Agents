using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Testing;

/// <summary>A decision policy that answers a set outcome and records what it was asked.</summary>
public sealed class ScriptedDecisionPolicy : IDecisionPolicy
{
    private readonly List<ActionContext> asked = [];

    /// <summary>Gets or sets the outcome every evaluation returns. Allow by default.</summary>
    public PolicyOutcome Outcome { get; set; } = PolicyOutcome.Allow;

    /// <summary>Gets the contexts evaluated, in order.</summary>
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

    /// <inheritdoc />
    public Task<PolicyDecision> EvaluateAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (this.asked)
        {
            this.asked.Add(context);
        }

        return Task.FromResult(new PolicyDecision(this.Outcome, "scripted", []));
    }
}
