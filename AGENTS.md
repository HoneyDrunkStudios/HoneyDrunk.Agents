# HoneyDrunk.Agents agent instructions

This repository holds the Grid agent runtime (ADR-0020): `HoneyDrunk.Agents.Abstractions` (the agent, execution context, tool invoker, agent memory and lifecycle contracts, with the records they use), `HoneyDrunk.Agents` (the in-process lifecycle, tool invocation through the Capabilities node behind Operator gates, scoped memory through the Memory node, and service registration), `HoneyDrunk.Agents.Operations` (the read side of the shared agent operations contract over HTTP, ADR-0110: a run journal fed by lifecycle transitions, the response records and the `/v1` endpoints) and `HoneyDrunk.Agents.Testing` (test harness and fixtures). `samples/HoneyDrunk.Agents.Operations.LocalHost` is a Development-only host that serves the contract from a fixed workload; it is not packed. Inspect [README.md](README.md), the tracked files and open implementation PRs before making claims; an open PR is not merged behavior. Do not invent node ownership or new responsibilities from the repository name.

## Build and test

The solution is `HoneyDrunk.Agents.slnx`.

```bash
dotnet build HoneyDrunk.Agents.slnx -c Release
dotnet test HoneyDrunk.Agents.slnx -c Release
```

The contract-shape canary (`Category=ContractShape`) freezes `IAgent`, `IAgentRunContext`, `IToolInvoker`, `IAgentMemory` and every public type their signatures reach in `api/HoneyDrunk.Agents.Abstractions.contract-shape.txt` (Grid invariant 137). `IAgentLifecycle` is host-facing and outside it. To accept an intended shape change, raise `Version` in `Directory.Build.props` first, then regenerate and commit the baseline:

```bash
HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1 dotnet test tests/HoneyDrunk.Agents.Abstractions.Tests -c Release --filter Category=ContractShape
```

`contracts/operations/v1` holds the operations contract's JSON Schemas and the fixtures the local host produces for a fixed clock. `ContractTests` keeps the schemas' property names equal to the response records, and `FixtureTests` fails when the fixtures differ from the local host's output. To accept an intended change, regenerate the fixtures, then read every file for private data before committing:

```bash
UPDATE_FIXTURES=1 dotnet test tests/HoneyDrunk.Agents.Operations.Tests -c Release --filter Fixtures_match_local_host_output
```

`tests/HoneyDrunk.Agents.Canary` runs the runtime under Kernel's real `AddHoneyDrunkNode` registration (as a background host) and against the real Kernel grid context and agent-context projection, the real Capabilities runtime and Auth guard, the real Memory runtime, and HoneyDrunk.Auth's `DefaultAuthorizationPolicy`.

## Boundaries

- `Abstractions` references only `HoneyDrunk.Kernel.Abstractions` and no AI runtime (Grid invariants 131 and 136). The runtime references only `*.Abstractions` packages and never a `HoneyDrunk.AI*` or `HoneyDrunk.Auth*` package (invariants 132 and 133). `Testing` is for test projects only.
- Tool calls go only through `IToolInvoker` to the Capabilities registry and invoker; the capability guard runs once, inside that invoker. Never call `ICapabilityGuard` or build an Auth type here (invariant 133).
- Memory goes only through `IAgentMemory` over the Memory node's store and scopes; execution state is disposed when the execution completes (invariant 134).
- There is no function-calling loop yet. When it is added, it goes in the runtime package (invariant 135).
- Tool arguments, tool output, memory content and tags, and agent exception messages never go into logs, traces or results. Results carry codes, not text.
- `Operations` references the runtime, the `*.Abstractions` of Operator, Audit and Kernel, and ASP.NET Core only. Every route requires the `agent-operations:read` policy, and the package chooses no authentication scheme. A response is a projection copied from named fields: never a request payload, an agent's output, tool arguments, an approval's context, audit changes or metadata, or a credential. A change to a response is a contract change: follow the versioning rule in `contracts/operations/v1/README.md` and update the schema and fixtures with it.

## Working rules

Read the [shared engineering conventions](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md) and this repository's owning documentation before editing. Apply the parts relevant to this stack; preserve existing public contracts, dependency direction and repository-specific behavior. Verify shared capabilities in current code before reusing them; a catalog entry or scaffold is not an implemented integration.

Work within the selected request. Preserve unrelated changes and use a separate worktree when needed. Review the final diff, use Conventional Commits and ready-for-review PRs with exactly one accurate `Authorship:` line and a `Request:` line; include the authorship in commit trailers. Run meaningful checks for the affected behavior and report the reviewed/tested revision, failures and unrun checks. For documentation-only changes, check links, paths and instruction consistency. Preserve required checks and inspect actual latest-head Sonar new-code findings where analysis applies; do not suppress findings or weaken gates to obtain a pass. Legacy Grid Review is retired; do not restore its workers, queues or bypass labels. A configured replacement reviewer is not evidence of a completed review or enforcing merge check.

## Code Review Rules

Apply the [shared review criteria](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md#code-review) to changed behavior, using the repository boundaries above. Report actionable findings with the failing path, concrete impact and a small corrective action; disclose unavailable evidence. These rules grant no cross-repository access or merge authority.

- Review against the selected responsibility and actual tracked files. A repository name, catalog entry or open PR is not an implemented contract; do not invent ownership, public APIs or a runtime framework beyond what is tracked.
- If implementation is introduced, check real existing shared capabilities and dependency direction before accepting duplicate mechanics. Trace trust/tenant boundaries, failure/retry/cancellation and bounded work for the behavior actually added; keep product policy in its selected owner.
- Require verification that exists and fits the change: observable behavior and contract tests, and relevant security and concurrency tests for new code. Do not fabricate build commands, benchmark results, analysis coverage or deployment readiness.
