namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>
/// An approval waiting for a person's decision. It names the action only; the request's context, which may hold
/// tool arguments, is never projected.
/// </summary>
/// <param name="ApprovalId">The approval id.</param>
/// <param name="WorkloadId">The agent the approval concerns.</param>
/// <param name="Action">The action awaiting approval.</param>
/// <param name="RequestedAt">When the approval was requested.</param>
/// <param name="ExpiresAt">When the request expires if undecided.</param>
public sealed record PendingApprovalView(string ApprovalId, string WorkloadId, string Action, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt);
