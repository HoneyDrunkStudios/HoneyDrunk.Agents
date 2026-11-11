namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>A part of a response that has its own source, so it can be unavailable while the rest is served.</summary>
/// <typeparam name="T">The value the section carries.</typeparam>
public sealed record Section<T>
    where T : class
{
    /// <summary>Initializes a new instance of the <see cref="Section{T}"/> class.</summary>
    /// <param name="state">Whether the source answered.</param>
    /// <param name="value">The value.</param>
    internal Section(SectionState state, T? value)
    {
        this.State = state;
        this.Value = value;
    }

    /// <summary>Gets a value indicating whether the source answered.</summary>
    public SectionState State { get; }

    /// <summary>Gets the value: always null when unavailable; when available, null means there is none.</summary>
    public T? Value { get; }
}
