using System.Globalization;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Something an agent asks to remember. The memory node validates kind, content and tags.</summary>
/// <param name="Kind">The entry kind.</param>
/// <param name="Content">The content.</param>
/// <param name="Tags">The tags.</param>
public sealed record AgentMemoryNote(string Kind, string Content, IReadOnlyList<string> Tags)
{
    /// <summary>Gets the entry kind.</summary>
    public string Kind { get; } = Kind ?? throw new ArgumentNullException(nameof(Kind));

    /// <summary>Gets the content.</summary>
    public string Content { get; } = Content ?? throw new ArgumentNullException(nameof(Content));

    /// <summary>Gets a private copy of the tags.</summary>
    public IReadOnlyList<string> Tags { get; } = AgentValidation.NoNulls(Tags, nameof(Tags));

    /// <summary>Describes the note without its content or tags.</summary>
    /// <returns>The kind, the content length and the tag count.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"AgentMemoryNote {{ Kind = {this.Kind}, ContentLength = {this.Content.Length}, TagCount = {this.Tags.Count} }}");
}
