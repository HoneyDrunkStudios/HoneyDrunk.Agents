# Changelog

## [0.2.1]

- Scope one bounded pending-approval read to the exposed workloads with Operator 0.3.0, retaining `truncated` and legacy-page behavior.
- Require Audit 0.3.0 bounded newest-first reads: three source pages of at most 50, with no unbounded fallback.
- Keep the version 1 response shape and deterministic fixtures unchanged.

## [0.2.0]

- First release: the read side of the version 1 operations contract (ADR-0110).
- `AddAgentOperations` and `MapAgentOperations`, with the required `agent-operations:read` policy.
- Routes for workloads, one workload with its pause and budget, a page of finished runs, one run, pending approvals and recent audit.
- A bounded run journal (`IAgentRunJournal`, in-memory by default) fed by lifecycle transitions.
- The response records, their JSON settings, and JSON Schemas and fixtures under `contracts/operations/v1`.
- Serialize asynchronous journal writes per agent/run, preserving start times and isolating failures.
- `truncated` on the approvals response: true when the approval desk returned a full page, so an older approval for a listed agent may be missing.
