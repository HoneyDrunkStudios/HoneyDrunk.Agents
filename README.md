# HoneyDrunk.Agents

The HoneyDrunk Grid's agent runtime: how an agent is registered, given a context, allowed to call tools and remember things, and run one execution at a time (ADR-0020). Agents compose the Capabilities node for tools, the Memory node for memory and the Operator node for safety gates. This repository calls no model.

## Packages

| Package | Use it from | Holds |
|---|---|---|
| `HoneyDrunk.Agents.Abstractions` | Any code that implements or calls an agent | `IAgent`, `IAgentExecutionContext`, `IToolInvoker`, `IAgentMemory`, `IAgentLifecycle`, and records such as `AgentDescriptor`, `AgentGrants`, `ToolCall`, `AgentResult` and `AgentMemoryBinding`. It references only `HoneyDrunk.Kernel.Abstractions`. |
| `HoneyDrunk.Agents` | The host's composition root only | `InProcessAgentLifecycle`, `ToolApprovalLedger`, `AgentRuntimeOptions`, `IAgentLifecycleObserver` and `AddHoneyDrunkAgents`. |
| `HoneyDrunk.Agents.Testing` | Test projects only | `AgentTestHarness` over the real runtime, `ScriptedAgent`, `InMemoryToolInvoker`, `InMemoryAgentMemory`, `TestAgentExecutionContext` and deterministic Kernel contexts. |

## Hosting

`AddHoneyDrunkAgents` registers the lifecycle and the approval ledger. The host composes the rest: Kernel (`AddHoneyDrunkNode` plus telemetry), Capabilities (`ICapabilityRegistry`, `ICapabilityInvoker`), Memory (`IMemoryScopeResolver`, `IMemoryStore`), Operator (`ICircuitBreaker`, `IDecisionPolicy`, `IApprovalGate`) and logging. Options are checked when the lifecycle is built: at least one tool call, a positive timeout and a positive approval lifetime.

The lifecycle reads the grid context once, from `IGridContextAccessor`, and builds the execution's operation context over it. In an HTTP request, Kernel's default accessor already finds the request's context. A background host (a worker or a scheduled job) registers Kernel's `ScopedGridContextAccessor` as `IGridContextAccessor`, initializes a grid context, and wraps each execution in `ScopedGridContextAccessor.SetContext(grid)`. Without a context the execution returns `grid_context_missing` instead of throwing.

```csharp
services.AddHoneyDrunkAgents(options =>
{
    options.MaxToolCallsPerExecution = 8;
    options.PropagatedExceptionTypes.Add(typeof(MyBudgetStopException));
});

lifecycle.Register(new AgentRegistration(agent, grants, [new("search", "1.0.0")]));
await lifecycle.InitializeAsync(agent.Descriptor.AgentId);
var result = await lifecycle.ExecuteAsync(agent.Descriptor.AgentId, request, new AgentExecutionOptions(new AgentMemoryBinding(tenant, project, [])));
```

## One execution

Each execution runs in its own dependency-injection scope. Before the agent runs, the lifecycle checks, in order:

| Check | Result when it fails |
|---|---|
| The agent is `Initialized` | `InvalidOperationException` naming the phase |
| The grid context is initialized | `Failed`, `grid_context_missing` |
| The memory tenant is the grid context's tenant | `Rejected`, `tenant_mismatch` |
| The Operator breaker `agent:{agentId}` is closed | `Halted`, `breaker_open`; a breaker that throws gives `Failed`, `gate_error` |
| Reading other projects is authorized by the Memory node | `Rejected`, `memory_escalation_denied`; a resolver that throws gives `Failed`, `memory_error` |

The agent id and descriptor are captured at registration, so a descriptor that changes later cannot move an agent to another breaker or memory scope. Decommissioning refuses new executions; executions already running finish.

The agent then runs under `ExecutionTimeout` (60 seconds by default). Whatever happens, the context, its tools and its memory are disposed, so an agent that keeps the context cannot act after the execution ends. An agent exception gives `Failed`, `agent_error`, and only its type name is logged. A timeout gives `Failed`, `timeout`. Caller cancellation propagates. An exception whose type is listed in `PropagatedExceptionTypes`, or derives from one, is rethrown unchanged after cleanup and never retried; use it for host stops such as a budget limit.

The effective grants are the registered grants narrowed by `AgentExecutionOptions.NarrowGrantsTo`. A narrowing never widens them.

## Tools

`context.Tools.InvokeAsync` checks, in order, and the first failure wins:

1. The execution's tool-call limit (`MaxToolCallsPerExecution`, 8): `LimitReached`.
2. The agent's bindings, exact on name and version: `NotBound`.
3. The Capabilities registry: `NotFound`.
4. The Operator breaker: `Halted`.
5. Operator's `IDecisionPolicy` for action `tool.{name}`: `Deny` gives `Denied`, `operator_denied`; `RequireApproval` starts the approval flow.
6. The Capabilities invoker, which runs the capability guard exactly once. Its status maps one to one.

Any Operator failure fails closed with `Failed`, `gate_error`, except a configured stop exception, which reaches the caller unchanged. When the execution ends, a call still in flight is cancelled, and an approval that arrives afterwards is never consumed or dispatched; memory calls in flight are cancelled the same way.

**Approvals.** A call is keyed by a request key: an HMAC, under a random per-host secret, of the agent, the tool and the canonical arguments, so the key cannot be used to guess the arguments. The first call raises one approval request with a fresh id and returns `AwaitingApproval` with that id; concurrent proposals of the same call share it. Later proposals reuse the open id until it is decided or expires (`ToolApprovalTtl`, 24 hours). An approved id authorizes exactly one dispatch; the next identical call asks again. A denial stands until the request expires. Open approvals live in the host's memory, so after a restart a pending call raises a new request. The approval request carries the tool, the request key and the execution id, never the arguments; showing an approver the exact arguments needs a view served by the agent's host and is not built yet.

## Memory

`context.Memory` writes to the home project's scope for the agent and reads the home scope plus any `AlsoRead` projects the Memory node authorized before the run. It has no delete or summarize. Recalled items say whether they came from the home scope.

## Telemetry and logs

Activities are `agents.execute`, `agents.tool_call` and `agents.memory`, through Kernel's `ITelemetryActivityFactory`, with only these tags: `agent.id`, `agent.execution_id`, `agent.transition`, `agent.outcome`, `tool.name`, `tool.version`, `tool.status`, `memory.operation`, `memory.result_count`, `gate.kind` and `gate.outcome`. A proposed tool name appears only once it matches a binding. Tool arguments and output, memory content and tags, and exception messages never reach logs, traces or results.

## Not built yet

- **The function-calling loop.** The AI node's abstractions cannot yet send tools to a model, so an execution is one structured decision made by the agent's own code, followed by at most `MaxToolCallsPerExecution` tool calls. The loop will live in this runtime package (Grid invariant 135).
- **Audit records for lifecycle transitions.** Operator has no audit surface yet; transitions go to observers, logs and telemetry.
- **Recording a human approval decision.** Operator's current gate cannot record one; that arrives with Operator's decision recorder.

## Decisions

- [ADR-0020](https://github.com/HoneyDrunkStudios/HoneyDrunk.Studio/blob/main/adrs/ADR-0020-stand-up-honeydrunk-agents-node.md): stand up the Agents node.
