# Changelog

## [0.2.1]

- Move to the compatible HoneyDrunk.Operator.Abstractions 0.3.0 dependency.

## [0.2.0]

- `IAgentDirectory` and `AgentListing`: a read-only list of the registered agents, decommissioned ones included, ordered by id. `AddHoneyDrunkAgents` registers it as the lifecycle singleton.
- Moves to `HoneyDrunk.Operator.Abstractions` 0.2.0.

## [0.1.0]

- First release: the in-process lifecycle, tool invocation through Capabilities behind Operator gates, and scoped memory through Memory.
