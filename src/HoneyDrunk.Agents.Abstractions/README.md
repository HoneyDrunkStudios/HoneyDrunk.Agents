# HoneyDrunk.Agents.Abstractions

Define an agent with `IAgent`. Its invocation receives an execution context with effective grants, registered tool bindings, and scoped access to tools and memory. Hosts manage registration and execution through `IAgentLifecycle`.

The only HoneyDrunk dependency is Kernel.Abstractions. The runtime supplies lifecycle and authorization behavior in a separate package.
