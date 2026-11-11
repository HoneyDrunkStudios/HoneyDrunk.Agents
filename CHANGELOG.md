# Changelog

All notable changes to this repository are recorded here. Each package keeps its own changelog under `src/`.

## [0.2.0]

- New package `HoneyDrunk.Agents.Operations`: the read side of the version 1 agent operations contract over HTTP (ADR-0110), with a bounded run journal, JSON Schemas and fixtures under `contracts/operations/v1`, and a deterministic local host under `samples/`.
- The runtime lists its registered agents through `IAgentDirectory`.
- Moves to `HoneyDrunk.Operator.Abstractions` 0.2.0.

## [0.1.0]

- First slice: agent contracts, the in-process runtime and the test harness (ADR-0020).
