namespace HoneyDrunk.Agents.Abstractions;

/// <summary>What an agent asks to recall, newest first.</summary>
/// <param name="Limit">The most entries to return, 1 to 200.</param>
/// <param name="Kind">Only entries of this kind, if set.</param>
/// <param name="AllTags">Only entries carrying all of these tags, if set.</param>
public sealed record AgentRecall(int Limit = 20, string? Kind = null, IReadOnlyList<string>? AllTags = null)
{
    /// <summary>The highest limit accepted.</summary>
    public const int MaxLimit = 200;

    /// <summary>Gets the most entries to return.</summary>
    public int Limit { get; } = Limit is >= 1 and <= MaxLimit
        ? Limit
        : throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "The limit must be 1 to 200.");

    /// <summary>Gets a private copy of the required tags, if any.</summary>
    public IReadOnlyList<string>? AllTags { get; } = AllTags is null ? null : AgentValidation.NoNulls(AllTags, nameof(AllTags));
}
