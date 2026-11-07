using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Testing;

/// <summary>A fixed grid context for tests.</summary>
/// <param name="tenantId">The tenant.</param>
/// <param name="initialized">Whether the context reports itself initialized.</param>
public sealed class TestGridContext(TenantId tenantId, bool initialized = true) : IGridContext
{
    private readonly Dictionary<string, string> baggage = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsInitialized { get; } = initialized;

    /// <inheritdoc />
    public string CorrelationId { get; init; } = "test-correlation";

    /// <inheritdoc />
    public string? CausationId { get; init; }

    /// <inheritdoc />
    public string NodeId { get; init; } = "test-node";

    /// <inheritdoc />
    public string StudioId { get; init; } = "test-studio";

    /// <inheritdoc />
    public string Environment { get; init; } = "test";

    /// <inheritdoc />
    public TenantId TenantId { get; } = tenantId;

    /// <inheritdoc />
    public string? ProjectId { get; init; }

    /// <inheritdoc />
    public CancellationToken Cancellation => CancellationToken.None;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Baggage => this.baggage;

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; init; } = TestAgentExecutionContext.FixedStart;

    /// <inheritdoc />
    public void AddBaggage(string key, string value) => this.baggage[key] = value;
}
