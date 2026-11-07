namespace HoneyDrunk.Agents.Abstractions.Tests.ContractShape;

/// <summary>Fixture types for the renderer tests.</summary>
public static class ShapeFixtures
{
    /// <summary>A fixture enum reached through the record.</summary>
    public enum ShapeFixtureKind
    {
        /// <summary>The first kind.</summary>
        First = 1,

        /// <summary>The second kind.</summary>
        Second = 2,
    }

    /// <summary>A fixture root for the renderer tests.</summary>
    public interface IShapeFixtureRoot
    {
        /// <summary>Gets the fixture name.</summary>
        string Name { get; }

        /// <summary>A method with nullable parameters and defaults.</summary>
        /// <param name="name">A required name.</param>
        /// <param name="filter">An optional filter.</param>
        /// <param name="limit">A limit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The record, or <see langword="null"/>.</returns>
        Task<ShapeFixtureRecord?> FindAsync(string name, string? filter = null, int limit = 10, CancellationToken cancellationToken = default);
    }

    /// <summary>A fixture record reached through the root's signature.</summary>
    /// <param name="Label">A label.</param>
    /// <param name="Note">An optional note.</param>
    /// <param name="Kind">A kind.</param>
    public sealed record ShapeFixtureRecord(string Label, string? Note, ShapeFixtureKind Kind);

    /// <summary>A fixture type that no root signature mentions.</summary>
    public sealed class ShapeFixtureUnreferenced
    {
        /// <summary>Gets a value.</summary>
        public int Value { get; init; }
    }
}
