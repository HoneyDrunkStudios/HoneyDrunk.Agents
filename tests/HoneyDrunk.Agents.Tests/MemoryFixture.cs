using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Memory;
using HoneyDrunk.Memory.Providers.InMemory;
using HoneyDrunk.Operator.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HoneyDrunk.Agents.Tests;

/// <summary>The real Memory runtime over the in-memory provider, with a scope-claim policy.</summary>
internal sealed class MemoryFixture
{
    public static readonly TenantId Tenant = new("01JC0000000000000000000001");

    public static readonly ProjectId ProjectA = new("01JC0000000000000000000002");

    public static readonly ProjectId ProjectB = new("01JC0000000000000000000003");

    public MemoryFixture()
    {
        this.Provider = new InMemoryMemoryStore(this.Time);
        this.Resolver = new MemoryScopeResolver(
            new TestScopePolicy(),
            new InMemoryApprovalGate(this.Time),
            Options.Create(new MemoryAuthorizationOptions()),
            this.Telemetry,
            this.Time,
            NullLogger<MemoryScopeResolver>.Instance);
        this.Store = new ScopeCheckedMemoryStore(this.Provider, nameof(InMemoryMemoryStore), this.Telemetry, this.Time, NullLogger<ScopeCheckedMemoryStore>.Instance);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));

    public RecordingTelemetryActivityFactory Telemetry { get; } = new();

    public InMemoryMemoryStore Provider { get; }

    public MemoryScopeResolver Resolver { get; }

    public ScopeCheckedMemoryStore Store { get; }

    public ListLogger<MemoryAgentMemory> Log { get; } = new();

    public static string ReadScope(ProjectId project, string agentId = InvokerFixture.AgentId) => $"memory.read:{Tenant}:{project}:{agentId}";

    public static AgentMemoryBinding Binding(ProjectId home, params ProjectId[] alsoRead) => new(Tenant, home, alsoRead);

    public async Task<MemoryAgentMemory> OpenAsync(AgentMemoryBinding binding, params string[] scopes)
    {
        var identity = new ExecutionIdentity(InvokerFixture.AgentId, "exec-1", "corr-1", new AgentGrants(scopes, []), []);
        var (home, read) = await AgentMemoryScopes.OpenAsync(this.Resolver, identity, binding, CancellationToken.None);
        return new MemoryAgentMemory(this.Store, home, read, identity, this.Telemetry, this.Log);
    }
}
