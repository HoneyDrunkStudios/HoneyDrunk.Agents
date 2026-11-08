using HoneyDrunk.Kernel.Abstractions.Context;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Returns a fixed grid context.</summary>
internal sealed class TestGridContextAccessor(IGridContext gridContext) : IGridContextAccessor
{
    public IGridContext GridContext { get; set; } = gridContext;
}
