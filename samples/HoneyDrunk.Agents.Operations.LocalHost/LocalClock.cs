namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// The local host's clock. While frozen it stands still until it is advanced, so seeding is deterministic; once
/// released it follows the system clock.
/// </summary>
public sealed class LocalClock : TimeProvider
{
    private readonly Lock gate = new();
    private DateTimeOffset? frozen;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (this.gate)
        {
            return this.frozen ?? base.GetUtcNow();
        }
    }

    /// <summary>Stops the clock at an instant.</summary>
    /// <param name="at">The instant.</param>
    public void Freeze(DateTimeOffset at)
    {
        lock (this.gate)
        {
            this.frozen = at.ToUniversalTime();
        }
    }

    /// <summary>Moves a frozen clock forward.</summary>
    /// <param name="by">How far; never negative.</param>
    /// <exception cref="InvalidOperationException">The clock is not frozen.</exception>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        lock (this.gate)
        {
            this.frozen = (this.frozen ?? throw new InvalidOperationException("Only a frozen clock can be advanced.")) + by;
        }
    }

    /// <summary>Lets the clock follow the system clock again.</summary>
    public void Release()
    {
        lock (this.gate)
        {
            this.frozen = null;
        }
    }
}
