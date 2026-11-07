namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A bounded recency query over the memory visible to this execution.</summary>
/// <param name="Limit">The maximum number of entries to return, from 1 to 200.</param>
/// <param name="Kind">The optional entry kind to match.</param>
/// <param name="AllTags">The optional tags that every returned entry must have.</param>
public sealed record AgentRecall(int Limit = 20, string? Kind = null, IReadOnlyList<string>? AllTags = null)
{
    /// <summary>Gets the maximum number of entries.</summary>
    public int Limit { get; } = Limit is >= 1 and <= 200
        ? Limit
        : throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "The recall limit must be 1 to 200.");

    /// <summary>Gets the optional entry kind.</summary>
    public string? Kind { get; } = Kind is null ? null : AgentValidation.NonBlank(Kind, nameof(Kind));

    /// <summary>Gets the optional tags.</summary>
    public IReadOnlyList<string>? AllTags { get; } = AllTags is null ? null : AgentValidation.NonBlankList(AllTags, nameof(AllTags));

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => nameof(AgentRecall);
}
