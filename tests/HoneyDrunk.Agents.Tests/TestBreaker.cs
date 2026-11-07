using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An in-memory breaker that can be made to throw.</summary>
internal sealed class TestBreaker : ICircuitBreaker
{
    private readonly InMemoryCircuitBreaker inner = new();

    public Exception? Throw { get; set; }

    public Task<bool> IsAllowedAsync(string breakerName, CancellationToken cancellationToken = default) =>
        this.Throw is null ? this.inner.IsAllowedAsync(breakerName, cancellationToken) : throw this.Throw;

    public Task TripAsync(string breakerName, string reason, CancellationToken cancellationToken = default) => this.inner.TripAsync(breakerName, reason, cancellationToken);

    public Task ResetAsync(string breakerName, CancellationToken cancellationToken = default) => this.inner.ResetAsync(breakerName, cancellationToken);

    public Task<BreakerState> GetStateAsync(string breakerName, CancellationToken cancellationToken = default) => this.inner.GetStateAsync(breakerName, cancellationToken);
}
