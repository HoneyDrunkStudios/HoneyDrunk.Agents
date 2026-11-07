using HoneyDrunk.Operator.Abstractions;

namespace HoneyDrunk.Agents;

/// <summary>The Operator checks a tool call passes before dispatch.</summary>
/// <param name="Breaker">The circuit breaker.</param>
/// <param name="Policy">The decision policy.</param>
/// <param name="Gate">The approval gate.</param>
/// <param name="Ledger">The open-approval ledger.</param>
internal sealed record OperatorGates(ICircuitBreaker Breaker, IDecisionPolicy Policy, IApprovalGate Gate, ToolApprovalLedger Ledger);
