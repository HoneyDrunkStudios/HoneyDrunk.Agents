using System.Collections.Frozen;

namespace HoneyDrunk.Agents.Operations.Runs;

/// <summary>The seven statuses a finished run can have in the v1 operations contract (ADR-0110).</summary>
public static class AgentRunStatus
{
    /// <summary>The agent ran to completion.</summary>
    public const string Completed = "completed";

    /// <summary>The run was refused before the agent ran.</summary>
    public const string Rejected = "rejected";

    /// <summary>The run stopped because the agent's breaker is open.</summary>
    public const string Halted = "halted";

    /// <summary>The run failed, or ended in a way this contract does not name.</summary>
    public const string Failed = "failed";

    /// <summary>The run stopped to wait for an approval.</summary>
    public const string AwaitingApproval = "awaitingapproval";

    /// <summary>The caller canceled the run.</summary>
    public const string Canceled = "canceled";

    /// <summary>The run ended with a stop exception that the runtime rethrew to its host.</summary>
    public const string Propagated = "propagated";

    /// <summary>Gets the seven statuses.</summary>
    public static IReadOnlySet<string> All { get; } = FrozenSet.ToFrozenSet(
        [Completed, Rejected, Halted, Failed, AwaitingApproval, Canceled, Propagated],
        StringComparer.Ordinal);

    /// <summary>Maps the outcome a lifecycle <c>complete</c> transition carries to a status.</summary>
    /// <param name="outcome">The runtime's outcome string.</param>
    /// <returns>The status; <see cref="Failed"/> for null, <c>error</c> and any outcome this contract does not name.</returns>
    public static string FromOutcome(string? outcome) => outcome switch
    {
        "Completed" => Completed,
        "Rejected" => Rejected,
        "AwaitingApproval" => AwaitingApproval,
        "Halted" => Halted,
        "cancelled" => Canceled,
        "propagated" => Propagated,
        _ => Failed,
    };
}
