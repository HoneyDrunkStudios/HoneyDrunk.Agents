# HoneyDrunk.Agents agent instructions

The solution contains the agent-facing and host-facing contracts. Inspect [README.md](README.md) and the current branch before making runtime or release claims. The accepted node boundary is ADR-0020.

Run `dotnet test HoneyDrunk.Agents.slnx -c Release` and `dotnet build HoneyDrunk.Agents.slnx -c Release` for implementation changes. The agent-facing API is tracked by the contract-shape test. After an approved shape change and version increase, set `HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1` and run `dotnet test HoneyDrunk.Agents.slnx -c Release --filter Category=ContractShape` to regenerate the baseline. Unset the variable before normal test runs.

Read the [shared engineering conventions](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md) and this repository's owning documentation before editing. Apply the parts relevant to this stack; preserve existing public contracts, dependency direction and repository-specific behavior. Verify shared capabilities in current code before reusing them; a catalog entry or scaffold is not an implemented integration.

Work within the selected request. Preserve unrelated changes and use a separate worktree when needed. Review the final diff, use Conventional Commits and ready-for-review PRs with exactly one accurate `Authorship:` line and a `Request:` line; include the authorship in commit trailers. Run meaningful checks for the affected behavior and report the reviewed/tested revision, failures and unrun checks. For documentation-only changes, check links, paths and instruction consistency. Preserve required checks and inspect actual latest-head Sonar new-code findings where analysis applies; do not suppress findings or weaken gates to obtain a pass. Legacy Grid Review is retired; do not restore its workers, queues or bypass labels. A configured replacement reviewer is not evidence of a completed review or enforcing merge check.

For current documentation changes, validate Markdown links, tracked paths and `git diff --check`. Report the absence of executable validation explicitly; do not describe an unrun suite as passing.

## Code Review Rules

Apply the [shared review criteria](https://github.com/HoneyDrunkStudios/HoneyDrunk.Standards/blob/main/HoneyDrunk.Standards/docs/CONVENTIONS.md#code-review) to changed behavior, using the repository boundaries above. Report actionable findings with the failing path, concrete impact and a small corrective action; disclose unavailable evidence. These rules grant no cross-repository access or merge authority.

- Review against the selected responsibility and actual tracked files. A repository name, catalog entry or open PR is not an implemented contract; do not invent ownership, public APIs or a runtime framework for this scaffold.
- If implementation is introduced, check real existing shared capabilities and dependency direction before accepting duplicate mechanics. Trace trust/tenant boundaries, failure/retry/cancellation and bounded work for the behavior actually added; keep product policy in its selected owner.
- Require verification that exists and fits the change: metadata/link checks now, and observable behavior/contract and relevant security/concurrency tests for new code. Do not fabricate build commands, benchmark results, analysis coverage or deployment readiness.
