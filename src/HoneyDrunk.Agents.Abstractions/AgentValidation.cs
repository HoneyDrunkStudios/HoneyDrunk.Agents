using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoneyDrunk.Agents.Abstractions;

/// <summary>Shared argument checks for the Agents records.</summary>
internal static partial class AgentValidation
{
    /// <summary>The longest name, kind or version accepted.</summary>
    internal const int MaxTextLength = 128;

    /// <summary>Checks an agent id against the memory coordinate format.</summary>
    /// <param name="agentId">The id.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>The id, unchanged when valid.</returns>
    internal static string AgentId(string agentId, string paramName)
    {
        ArgumentNullException.ThrowIfNull(agentId, paramName);
        return AgentIdPattern().IsMatch(agentId)
            ? agentId
            : throw new ArgumentException("The agent id must be 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.", paramName);
    }

    /// <summary>Checks a short text value: not blank, no control characters, bounded.</summary>
    /// <param name="value">The value.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>The value, unchanged when valid.</returns>
    internal static string Text(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTextLength || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The value must be 1 to {MaxTextLength} characters, not blank, with no control characters.", paramName);
        }

        return value;
    }

    /// <summary>Copies a list whose entries must not be blank.</summary>
    /// <param name="values">The values.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>A private copy.</returns>
    internal static IReadOnlyList<string> NonBlankList(IReadOnlyList<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Entries must not be null, empty or whitespace.", paramName);
        }

        return values.ToArray().AsReadOnly();
    }

    /// <summary>Copies a list that must not contain null.</summary>
    /// <param name="values">The values.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>A private copy.</returns>
    internal static IReadOnlyList<string> NoNulls(IReadOnlyList<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Entries must not be null.", paramName);
        }

        return values.ToArray().AsReadOnly();
    }

    /// <summary>Clones a JSON value that must be defined.</summary>
    /// <param name="element">The value.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>A private copy.</returns>
    internal static JsonElement Defined(JsonElement element, string paramName) =>
        element.ValueKind == JsonValueKind.Undefined
            ? throw new ArgumentException("The JSON value must be defined.", paramName)
            : element.Clone();

    /// <summary>Clones a JSON value that must be an object.</summary>
    /// <param name="element">The value.</param>
    /// <param name="paramName">The parameter name.</param>
    /// <returns>A private copy.</returns>
    internal static JsonElement JsonObject(JsonElement element, string paramName) =>
        element.ValueKind == JsonValueKind.Object
            ? element.Clone()
            : throw new ArgumentException("The JSON value must be an object.", paramName);

    /// <summary>Clones an optional JSON value.</summary>
    /// <param name="element">The value.</param>
    /// <returns>A private copy, or the value unchanged when null or undefined.</returns>
    internal static JsonElement? Optional(JsonElement? element) =>
        element is { ValueKind: not JsonValueKind.Undefined } value ? value.Clone() : element;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgentIdPattern();
}
