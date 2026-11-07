using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Operator.Testing;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Builds tool invokers over the real Capabilities runtime with scripted Operator gates.</summary>
internal sealed class InvokerFixture
{
    public const string AgentId = "agent-1";

    public InvokerFixture(CapabilityResult? handlerResult = null)
    {
        this.Capabilities = new CapabilityTestHarness();
        this.Handler = new RecordingCapabilityHandler(handlerResult ?? CapabilityResult.Success(Json("""{"ok":true}""")));
        this.Capabilities.Register(CapabilityFixtures.Descriptor(), this.Handler);
        this.Ledger = new ToolApprovalLedger(this.Time);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));

    public CapabilityTestHarness Capabilities { get; }

    public RecordingCapabilityHandler Handler { get; }

    public InMemoryCircuitBreaker Breaker { get; } = new();

    public ScriptedDecisionPolicy Policy { get; } = new();

    public ScriptedApprovalGate Gate { get; } = new();

    public ToolApprovalLedger Ledger { get; }

    public AgentRuntimeOptions Options { get; } = new();

    public RecordingTelemetryActivityFactory Telemetry { get; } = new();

    public ListLogger<CapabilityToolInvoker> Log { get; } = new();

    public AgentGrants Grants { get; set; } = new([CapabilityFixtures.Scope], ["operator"]);

    public IReadOnlyList<AgentToolBinding> Bindings { get; set; } = [new(CapabilityFixtures.Name, CapabilityFixtures.Version)];

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static ToolCall Call(string arguments = """{"q":1}""", string name = CapabilityFixtures.Name, string version = CapabilityFixtures.Version) =>
        new(name, version, Json(arguments));

    public CapabilityToolInvoker Create(string executionId = "exec-1") =>
        new(
            new ExecutionIdentity(AgentId, executionId, "corr-1", this.Grants, this.Bindings),
            this.Capabilities.Registry,
            this.Capabilities.Invoker,
            new OperatorGates(this.Breaker, this.Policy, this.Gate, this.Ledger),
            this.Options,
            this.Telemetry,
            this.Log,
            this.Time);
}
