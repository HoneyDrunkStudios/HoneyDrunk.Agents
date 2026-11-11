using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Operations.Runs;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>
/// Maps runtime, Operator and Audit records to the response records. Every mapping copies named fields only, so a
/// field added to a source record never reaches a response by accident, and enforces the contract's rules even
/// when a source does not.
/// </summary>
internal static class Projection
{
    /// <summary>The prefix of an agent's subject and pause name.</summary>
    public const string AgentPrefix = "agent:";

    /// <summary>The maximum number of workloads exposed by the list endpoint.</summary>
    public const int MaxListedWorkloads = 20;

    private const int MaxDisplayName = 64;

    /// <summary>Lists the host's agents with valid ids, each once, ordered by id.</summary>
    /// <param name="directory">The agent directory.</param>
    /// <returns>The agents.</returns>
    public static IEnumerable<AgentListing> Listed(IAgentDirectory directory) => directory.List()
        .Where(agent => agent is not null && Identifiers.IsValid(agent.AgentId))
        .DistinctBy(agent => agent.AgentId, StringComparer.Ordinal)
        .OrderBy(agent => agent.AgentId, StringComparer.Ordinal);

    /// <summary>Finds one of the host's agents.</summary>
    /// <param name="directory">The agent directory.</param>
    /// <param name="id">The id from the route.</param>
    /// <returns>The agent, or null when the host lists no agent with that id.</returns>
    public static AgentListing? Find(IAgentDirectory directory, string id) =>
        Identifiers.IsValid(id) ? Listed(directory).FirstOrDefault(agent => string.Equals(agent.AgentId, id, StringComparison.Ordinal)) : null;

    /// <summary>
    /// Makes an agent's name safe to display: control characters are removed, it is trimmed and cut to 64
    /// characters, and a name with nothing left is replaced by the agent id.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(AgentListing agent)
    {
        var name = string.Concat((agent.Name ?? string.Empty).Where(character => !char.IsControl(character))).Trim();
        if (name.Length > MaxDisplayName)
        {
            // Never cut between the two halves of a surrogate pair.
            var length = char.IsHighSurrogate(name[MaxDisplayName - 1]) ? MaxDisplayName - 1 : MaxDisplayName;
            name = name[..length].TrimEnd();
        }

        return name.Length == 0 ? agent.AgentId : name;
    }

    /// <summary>Names a lifecycle phase.</summary>
    /// <param name="phase">The phase.</param>
    /// <returns><c>registered</c>, <c>initialized</c> or <c>decommissioned</c>.</returns>
    public static string Phase(AgentLifecyclePhase phase) => phase switch
    {
        AgentLifecyclePhase.Registered => "registered",
        AgentLifecyclePhase.Initialized => "initialized",
        AgentLifecyclePhase.Decommissioned => "decommissioned",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "The contract does not name this lifecycle phase."),
    };

    /// <summary>
    /// Maps a finished run. The id is written as 32 lowercase hexadecimal digits, a status the contract does not
    /// name becomes <c>failed</c>, and a completion recorded before the start is moved to the start.
    /// </summary>
    /// <param name="record">The journal's record.</param>
    /// <returns>The run, or null when it has not finished or its id is not a run id.</returns>
    public static RunSummary? Run(AgentRunRecord? record)
    {
        if (record?.CompletedAt is not { } completedAt || !Guid.TryParseExact(record.RunId, "N", out var runId) || runId == Guid.Empty)
        {
            return null;
        }

        var status = record.Status is not null && AgentRunStatus.All.Contains(record.Status) ? record.Status : AgentRunStatus.Failed;
        return new RunSummary(runId.ToString("N"), status, record.StartedAt, completedAt < record.StartedAt ? record.StartedAt : completedAt);
    }

    /// <summary>Maps a list of runs, keeping the journal's order, each run id once, up to a limit.</summary>
    /// <param name="records">The journal's records.</param>
    /// <param name="limit">The most runs to return.</param>
    /// <returns>The finished runs.</returns>
    public static IReadOnlyList<RunSummary> Runs(IReadOnlyList<AgentRunRecord> records, int limit) =>
    [
        .. records
            .Select(Run)
            .OfType<RunSummary>()
            .DistinctBy(run => run.Id, StringComparer.Ordinal)
            .Take(limit),
    ];

    /// <summary>Maps a pause.</summary>
    /// <param name="pause">The pause, or null when the agent is not paused.</param>
    /// <returns>The view, or null.</returns>
    public static PauseView? Pause(PauseState? pause) =>
        pause is null ? null : new PauseView(pause.PausedBy ?? string.Empty, pause.Reason ?? string.Empty, pause.Since);

    /// <summary>Maps a spend status. The guard's "no limit" sentinel becomes a zero limit with no remaining amount.</summary>
    /// <param name="status">The spend status.</param>
    /// <returns>The budget view.</returns>
    public static BudgetView Budget(CostStatus status) => status.Limit > 0m
        ? new BudgetView(status.Window, status.Spent, status.Limit, status.Remaining)
        : new BudgetView(status.Window, status.Spent, 0m, null);

    /// <summary>Maps a pending approval whose subject is an agent. The request's context is not read.</summary>
    /// <param name="pending">The pending approval.</param>
    /// <returns>The view, or null when the subject is not <c>agent:</c> followed by a valid agent id.</returns>
    public static PendingApprovalView? Approval(PendingApproval? pending)
    {
        var request = pending?.Request;
        if (pending is null || request?.Subject is not { } subject || !subject.StartsWith(AgentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var workloadId = subject[AgentPrefix.Length..];
        return Identifiers.IsValid(workloadId)
            ? new PendingApprovalView(request.ApprovalId ?? string.Empty, workloadId, request.Action ?? string.Empty, pending.RequestedAt, request.Expiry)
            : null;
    }

    /// <summary>Maps an audit entry. Its changes, metadata, correlation id and target display name are not read.</summary>
    /// <param name="entry">The audit entry.</param>
    /// <returns>The view.</returns>
    public static AuditEntryView Audit(AuditEntry entry) => new(
        entry.OccurredAt,
        entry.EventName ?? string.Empty,
        entry.Actor ?? string.Empty,
        entry.Outcome switch
        {
            AuditOutcome.Succeeded => "succeeded",
            AuditOutcome.Denied => "denied",
            AuditOutcome.Pending => "pending",
            AuditOutcome.Failed => "failed",
            _ => "failed",
        },
        entry.Target.Type ?? string.Empty,
        entry.Target.Id ?? string.Empty,
        entry.Reason);
}
