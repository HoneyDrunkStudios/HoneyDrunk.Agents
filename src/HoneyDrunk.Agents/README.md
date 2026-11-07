# HoneyDrunk.Agents

Register the runtime through `AddHoneyDrunkAgents`. Compose Kernel, Operator, Capabilities, and Memory in the same host. For background work, establish an initialized Kernel Grid context before executing the agent.

Kernel 0.8.2 resolves its default operation context from the child DI scope. When the caller supplies the initialized Grid context through an ambient accessor, set `AgentRuntimeOptions.CreateOperationContext` to create a fresh Kernel operation over that context. The runtime owns and disposes the operation.

The host registers each agent with its maximum grants and exact tool bindings. Each execution gets its own service scope, a narrowed grant set, a writable home memory scope, and optional authorized read scopes. Cancellation and the execution deadline are passed to the agent and its tools and memory. Workloads must observe cancellation before returning.

An approval authorizes one capability dispatch in the same tenant. The host owns how approval decisions are recorded. Operator 0.1.2 exposes request and status contracts. Telemetry and run history can consume safe lifecycle metadata through the observer; model providers and durable run storage belong to the owning workload host.
