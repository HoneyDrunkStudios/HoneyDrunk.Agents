namespace HoneyDrunk.Agents;

/// <summary>Activity names and the only tag keys the runtime writes. Tags never carry arguments, output or content.</summary>
internal static class AgentTelemetry
{
    public const string LifecycleActivity = "agents.lifecycle";

    public const string ExecuteActivity = "agents.execute";

    public const string ToolCallActivity = "agents.tool_call";

    public const string MemoryActivity = "agents.memory";

    public const string AgentIdTag = "agent.id";

    public const string ExecutionIdTag = "agent.execution_id";

    public const string TransitionTag = "agent.transition";

    public const string OutcomeTag = "agent.outcome";

    public const string ToolNameTag = "tool.name";

    public const string ToolVersionTag = "tool.version";

    public const string ToolStatusTag = "tool.status";

    public const string MemoryOperationTag = "memory.operation";

    public const string MemoryResultCountTag = "memory.result_count";

    public const string GateKindTag = "gate.kind";

    public const string GateOutcomeTag = "gate.outcome";
}
