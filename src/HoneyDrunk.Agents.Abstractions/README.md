# HoneyDrunk.Agents.Abstractions

Contracts for HoneyDrunk Grid agents (ADR-0020): `IAgent`, `IAgentExecutionContext`, `IToolInvoker`, `IAgentMemory` and `IAgentLifecycle`, with the small records they use.

The package references only `HoneyDrunk.Kernel.Abstractions`. It names no Capabilities, Memory, Operator or AI type, so consumers compile against agent contracts alone. The first four interfaces are frozen by a contract-shape canary; `IAgentLifecycle` is a host-facing surface outside it.
