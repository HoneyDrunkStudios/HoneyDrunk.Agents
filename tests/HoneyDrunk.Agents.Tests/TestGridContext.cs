using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Tests;

/// <summary>A settable grid context.</summary>
internal sealed class TestGridContext(TenantId tenantId, bool initialized = true) : IGridContext
{
    private readonly Dictionary<string, string> baggage = new(StringComparer.Ordinal);

    public bool IsInitialized { get; } = initialized;

    public string CorrelationId => "corr-1";

    public string? CausationId => null;

    public string NodeId => "test-node";

    public string StudioId => "test-studio";

    public string Environment => "test";

    public TenantId TenantId { get; } = tenantId;

    public string? ProjectId => null;

    public CancellationToken Cancellation => CancellationToken.None;

    public IReadOnlyDictionary<string, string> Baggage => this.baggage;

    public DateTimeOffset CreatedAtUtc => DateTimeOffset.UnixEpoch;

    public void AddBaggage(string key, string value) => this.baggage[key] = value;
}
