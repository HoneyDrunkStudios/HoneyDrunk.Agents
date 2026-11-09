# Agent operations contract, version 1

The read side of the shared HTTP contract that every agent host serves (ADR-0110). `HoneyDrunk.Agents.Operations` implements it; the JSON Schemas in this folder (draft 2020-12) describe each response.

## Routes

Every route is a `GET` under `/v1` and requires the host's `agent-operations:read` authorization policy.

| Route | Schema | Notes |
|---|---|---|
| `/v1/workloads` | [workloads.schema.json](workloads.schema.json) | The first 20 agents by id, each with its 10 newest finished runs. |
| `/v1/workloads/{id}` | [workload.schema.json](workload.schema.json) | One agent with its phase, pause and budget. `404` for an unknown id. |
| `/v1/workloads/{id}/runs?limit=&before=` | [runs.schema.json](runs.schema.json) | A page of finished runs, newest first. `limit` is 1 to 50 (default 20). `before` is a run id. `400` for a bad `limit` or `before`; `404` for an unknown id. |
| `/v1/workloads/{id}/runs/{runId}` | [run.schema.json](run.schema.json) | One finished run. `404` for an unknown agent or run, and for a run that is still running. |
| `/v1/approvals` | [approvals.schema.json](approvals.schema.json) | Approvals waiting for a decision, for the agents this host lists. |
| `/v1/workloads/{id}/audit` | [audit.schema.json](audit.schema.json) | The last 7 days of audit entries about the agent, newest first, at most 50. `404` for an unknown id. |

## Rules

- **Version.** Every response carries `schemaVersion: 1`.
- **Staleness.** Every response carries `generatedAt` (UTC), so a consumer can judge how old it is.
- **Empty and unavailable are different.** An empty list is `[]`. A part of a response whose source is missing or failing reports `"state": "unavailable"` and carries no value; the rest of the response is still served.
- **Identifiers.** A workload `id` and an `environment` are 1 to 64 characters of `a-z 0-9 . _ -`, starting with a letter or digit.
- **Display names.** `displayName` is 1 to 64 characters, not blank, with no control characters.
- **Runs.** A run `id` is 32 lowercase hexadecimal digits. Run ids are unique within a list. `status` is one of `completed`, `rejected`, `halted`, `failed`, `awaitingapproval`, `canceled`, `propagated`. `completedAt` is never before `startedAt`. Only finished runs are listed.
- **Times.** UTC, ISO-8601, with a `Z` suffix.
- **Audit targets.** An audit entry is about an agent when its target id is `agent:{id}`, or the bare `{id}` under the target type `agent` or `cost-scope`. Another kind of target that shares the id is not shown.
- **Safe projection.** No response includes a prompt, a request payload, tool arguments, an agent's output, an approval's context, audit field changes or metadata, or credentials.

## Versioning

Adding an optional property or a new route is not a new version: consumers must ignore properties they do not know. Removing or renaming a property, changing a type, loosening a rule above or adding a value to a closed set (`status`, `phase`, `state`, `outcome`) needs a new version under `/v2`, with its own folder here.

## Fixtures

[fixtures/](fixtures/) holds the responses of the deterministic local host (`samples/HoneyDrunk.Agents.Operations.LocalHost`), so a consumer can be built and tested without a live agent. Run ids and `generatedAt` are replaced by placeholders.
