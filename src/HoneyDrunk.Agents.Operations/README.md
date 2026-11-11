# HoneyDrunk.Agents.Operations

The shared, versioned HTTP contract that every HoneyDrunk Grid agent host serves, so one control room can read any agent the same way (ADR-0110). This version is the read side: workloads, runs, pending approvals, budget and pause, and recent audit. Starting, cancelling, deciding and pausing are not built yet.

## Use it

Reference the package from the agent host's web project, next to `HoneyDrunk.Agents`.

```csharp
builder.Services.AddHoneyDrunkAgents();
builder.Services.AddAgentOperations(options => options.Environment = "production");
builder.Services.AddAuthorization(options => options.AddPolicy(
    AgentOperationsPolicies.Read,
    policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "agents.read")));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAgentOperations();
```

- **`AddAgentOperations`** registers the options, the run journal and the lifecycle observer that feeds it. `Environment` is required: 1 to 64 characters of `a-z 0-9 . _ -`, starting with a letter or digit.
- **`MapAgentOperations`** maps the routes under `/v1` and returns the route group, so the host can add its own conventions (rate limiting, for example).

## The policy is required

Every route requires the authorization policy `agent-operations:read` (`AgentOperationsPolicies.Read`). The host defines it; this package chooses no authentication scheme and validates no token. `MapAgentOperations` throws at startup when the policy is not defined, when the environment is not valid, or when `AddHoneyDrunkAgents` and `AddAgentOperations` were not called. A host cannot serve these routes unprotected by accident.

## Routes

| Route | Returns |
|---|---|
| `GET /v1/workloads` | The first 20 agents by id, each with its 10 newest finished runs. |
| `GET /v1/workloads/{id}` | One agent: its phase, its manual pause and its spend against this month's budget. `supportedActions` is always empty in this version. |
| `GET /v1/workloads/{id}/runs?limit=&before=` | A page of finished runs, newest first. `limit` is 1 to 50 (default 20). `before` is a run id; `nextCursor` is the value for the next page. |
| `GET /v1/workloads/{id}/runs/{runId}` | One finished run. A run that is still running is `404`. |
| `GET /v1/approvals` | Operator approvals waiting for a decision, for the agents this host lists, newest first. `truncated` is `true` when the list may be incomplete. |
| `GET /v1/workloads/{id}/audit` | The last 7 days of audit entries about the agent, newest first, at most 50. |

An unknown agent is `404`. A bad `limit` or `before` is `400`. A decommissioned agent stays listed, with its phase, and its runs stay readable.

The JSON Schemas and the rules every response meets are in [`contracts/operations/v1`](https://github.com/HoneyDrunkStudios/HoneyDrunk.Agents/tree/main/contracts/operations/v1). Every response carries `schemaVersion: 1` and `generatedAt` (UTC), and is written with the contract's own JSON settings (`OperationsJson.Options`), whatever the host configured.

## What a response never holds

Responses are a safe projection, copied field by field from named fields only:

- A run is an id, a status and two times. Never the request payload, the agent's output or a reason text.
- An approval is its id, the agent, the action and two times. Never the request's context, which may hold tool arguments.
- An audit entry is its time, event, actor, outcome, target and reason. Never its field changes or metadata.
- A failing source is logged by exception type name only; its message is never returned.

## Empty, stale and unavailable

- An empty list is `[]`.
- `generatedAt` says how old a response is.
- The pause and the budget each have a `state`, and so do the approvals and audit responses. A source the host has not registered, or one that throws, makes only its own part `unavailable`; the rest is still served.
- Runs have no `state` of their own. When the run journal or the agent directory throws, the route answers `503` with no body, and the failure is logged by exception type name. It is never shown as an empty list.

| Part | Source | When the host has none |
|---|---|---|
| Pause | Operator's `IAgentPauses` | `unavailable` |
| Budget | Operator's `ICostGuard` | `unavailable` |
| Approvals | Operator's `IApprovalDesk` (subject-scoped capability when available) | `unavailable` |
| Audit | Audit's `IAuditQuery` with `IBoundedAuditQuery` | `unavailable` |

## Bounded source reads

**Approvals.** Operator 0.3.0's optional `ISubjectScopedApprovalDesk` is used through the existing
`IApprovalDesk` registration. One query includes only the first 20 workloads exposed by `/workloads`
and returns at most 100 pending approvals, with filtering before the source limit. Other subjects can
no longer displace these workloads' older approvals. An empty workload list makes no source query.

The released `truncated` field is preserved: a full page sets it to `true` for both scoped and legacy
desks. A legacy desk is queried once with limit 100 and filtered afterward, so its full page can still
hide older matching approvals. Consumers must continue to show that a truncated list may be incomplete.
Oversized pages and scoped pages that contain invalid, repeated or out-of-scope approvals report
`unavailable`; the endpoint never widens or repeats the read.

**Audit.** Audit 0.3.0's `IBoundedAuditQuery` is required through the existing `IAuditQuery` registration.
A legacy provider without that capability reports `unavailable` without an unbounded fallback.
For one inclusive seven-day window, three disjoint queries each read at most 50 newest entries:
bare agent id with target type `agent`, bare id with type `cost-scope`, and `agent:{id}` with any type.
At most 150 source results enter the endpoint before it selects the newest 50. Nonconforming pages
or a failure in any scope report `unavailable`, never a partial successful history.

These are result-materialization and query-count bounds, not guarantees about a database's internal
scan or sort work. The newest occurrence times are preserved. When more records share the cutoff
time than a limit allows, membership follows each provider's audit-id comparison. The response sorts
its bounded candidates ordinally by id for ties, but cannot promise global ordinal-id membership
across arbitrary provider collations. The local host uses ordinal comparison. The three calls do not
provide a shared transaction snapshot while new events arrive.

## The run journal

`RunJournalObserver` records each execution from the lifecycle's `execute` and `complete` transitions into an `IAgentRunJournal`. The default, `InMemoryAgentRunJournal`, keeps the newest 100 runs per agent **in this process's memory**: runs are lost on restart, and two processes do not see each other's runs. Register your own `IAgentRunJournal` before `AddAgentOperations` to keep runs somewhere durable. Writes for each agent/run are serialized, so an asynchronous completion waits for its start to persist; other runs proceed independently. A journal whose write fails never fails an execution; the failure is logged. A journal whose read fails makes the routes that list runs answer `503`.

## Local host and fixtures

`samples/HoneyDrunk.Agents.Operations.LocalHost` serves this contract from two scripted agents with a fixed workload: five runs that end five different ways, one pending approval, one pause and spend under a budget. It runs only in the Development environment.

```bash
dotnet run --project samples/HoneyDrunk.Agents.Operations.LocalHost
```

It listens on `http://localhost:5179` and prints a random bearer token for the run. To keep one token between runs, set `LocalHost__Token` (16 characters or more) in your environment.

The same responses, for a fixed clock, are checked in under [`contracts/operations/v1/fixtures`](https://github.com/HoneyDrunkStudios/HoneyDrunk.Agents/tree/main/contracts/operations/v1/fixtures), so a consumer can be tested without running anything.
