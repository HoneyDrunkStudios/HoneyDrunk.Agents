namespace HoneyDrunk.Agents.Abstractions;

/// <summary>One note to write to the execution's home memory.</summary>
/// <param name="Kind">The entry kind.</param>
/// <param name="Content">The entry content.</param>
/// <param name="Tags">The entry tags.</param>
public sealed record AgentMemoryNote(
    string Kind,
    string Content,
    IReadOnlyList<string> Tags)
{
    /// <summary>Gets the entry kind.</summary>
    public string Kind { get; } = AgentValidation.NonBlank(Kind, nameof(Kind));

    /// <summary>Gets the entry content.</summary>
    public string Content { get; } = AgentValidation.NonBlank(Content, nameof(Content));

    /// <summary>Gets the entry tags.</summary>
    public IReadOnlyList<string> Tags { get; } = AgentValidation.NonBlankList(Tags, nameof(Tags));

    /// <summary>Formats the record without including workload data.</summary>
    /// <returns>The safe display text.</returns>
    public override string ToString() => nameof(AgentMemoryNote);
}
