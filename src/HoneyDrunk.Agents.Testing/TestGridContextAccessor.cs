using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents.Testing;

/// <summary>Returns a settable grid context.</summary>
/// <param name="gridContext">The initial grid context.</param>
public sealed class TestGridContextAccessor(IGridContext gridContext) : IGridContextAccessor
{
    /// <inheritdoc />
    public IGridContext GridContext { get; set; } = gridContext ?? throw new ArgumentNullException(nameof(gridContext));
}
