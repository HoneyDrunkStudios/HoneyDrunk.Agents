namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A recalled or newly written memory entry.</summary>
/// <param name="Id">The stable entry identifier.</param>
/// <param name="Kind">The entry kind.</param>
/// <param name="Content">The entry content.</param>
/// <param name="Tags">The entry tags.</param>
/// <param name="WrittenAt">The time the entry was written.</param>
/// <param name="FromHomeScope">Whether the entry is in the execution's home scope.</param>
public sealed record AgentMemoryItem(
    string Id,
    string Kind,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset WrittenAt,
    bool FromHomeScope)
{
    /// <summary>Gets the stable entry identifier.</summary>
    public string Id { get; } = AgentValidation.NonBlank(Id, nameof(Id));

    /// <summary>Gets the entry kind.</summary>
    public string Kind { get; } = AgentValidation.NonBlank(Kind, nameof(Kind));

    /// <summary>Gets the entry content.</summary>
    public string Content { get; } = AgentValidation.NonBlank(Content, nameof(Content));

    /// <summary>Gets the entry tags.</summary>
    public IReadOnlyList<string> Tags { get; } = AgentValidation.NonBlankList(Tags, nameof(Tags));

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => nameof(AgentMemoryItem);
}
