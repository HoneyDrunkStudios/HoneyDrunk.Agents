using System.Globalization;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>A remembered entry, as an agent sees it.</summary>
/// <param name="Id">The entry id.</param>
/// <param name="Kind">The entry kind.</param>
/// <param name="Content">The content.</param>
/// <param name="Tags">The tags.</param>
/// <param name="WrittenAt">When the entry was written.</param>
/// <param name="FromHomeScope">True when the entry belongs to the execution's home scope.</param>
public sealed record AgentMemoryItem(string Id, string Kind, string Content, IReadOnlyList<string> Tags, DateTimeOffset WrittenAt, bool FromHomeScope)
{
    /// <summary>Gets the entry id.</summary>
    public string Id { get; } = Id ?? throw new ArgumentNullException(nameof(Id));

    /// <summary>Gets the entry kind.</summary>
    public string Kind { get; } = Kind ?? throw new ArgumentNullException(nameof(Kind));

    /// <summary>Gets the content.</summary>
    public string Content { get; } = Content ?? throw new ArgumentNullException(nameof(Content));

    /// <summary>Gets a private copy of the tags.</summary>
    public IReadOnlyList<string> Tags { get; } = AgentValidation.NoNulls(Tags, nameof(Tags));

    /// <summary>Describes the entry without its content or tags.</summary>
    /// <returns>The id, kind, content length, tag count, time and home flag.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"AgentMemoryItem {{ Id = {this.Id}, Kind = {this.Kind}, ContentLength = {this.Content.Length}, TagCount = {this.Tags.Count}, WrittenAt = {this.WrittenAt:O}, FromHomeScope = {this.FromHomeScope} }}");
}
