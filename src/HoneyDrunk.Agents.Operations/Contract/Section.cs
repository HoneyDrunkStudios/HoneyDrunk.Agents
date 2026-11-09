namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>Creates response sections.</summary>
public static class Section
{
    /// <summary>A section whose source answered.</summary>
    /// <typeparam name="T">The value the section carries.</typeparam>
    /// <param name="value">The value, or null when there is none.</param>
    /// <returns>The section.</returns>
    public static Section<T> Available<T>(T? value)
        where T : class => new(SectionState.Available, value);

    /// <summary>A section whose source is missing or failed. It carries no value.</summary>
    /// <typeparam name="T">The value the section would carry.</typeparam>
    /// <returns>The section.</returns>
    public static Section<T> Unavailable<T>()
        where T : class => new(SectionState.Unavailable, null);
}
