# Changelog

## [0.2.0]

- First release: the read side of the version 1 operations contract (ADR-0110).
- `AddAgentOperations` and `MapAgentOperations`, with the required `agent-operations:read` policy.
- Routes for workloads, one workload with its pause and budget, a page of finished runs, one run, pending approvals and recent audit.
- A bounded run journal (`IAgentRunJournal`, in-memory by default) fed by lifecycle transitions.
- The response records, their JSON settings, and JSON Schemas and fixtures under `contracts/operations/v1`.
