using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An in-memory breaker that can be made to throw, or to wait until its token is cancelled.</summary>
internal sealed class TestBreaker : ICircuitBreaker
{
    private readonly InMemoryCircuitBreaker inner = new();

    public Exception? Throw { get; set; }

    public bool WaitUntilCancelled { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> IsAllowedAsync(string breakerName, CancellationToken cancellationToken = default)
    {
        this.Entered.TrySetResult();
        if (this.Throw is not null)
        {
            throw this.Throw;
        }

        if (this.WaitUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return await this.inner.IsAllowedAsync(breakerName, cancellationToken);
    }

    public Task TripAsync(string breakerName, string reason, CancellationToken cancellationToken = default) => this.inner.TripAsync(breakerName, reason, cancellationToken);

    public Task ResetAsync(string breakerName, CancellationToken cancellationToken = default) => this.inner.ResetAsync(breakerName, cancellationToken);

    public Task<BreakerState> GetStateAsync(string breakerName, CancellationToken cancellationToken = default) => this.inner.GetStateAsync(breakerName, cancellationToken);
}
