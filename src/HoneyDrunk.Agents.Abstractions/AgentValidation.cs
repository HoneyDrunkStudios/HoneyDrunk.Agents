using System.Text.RegularExpressions;

namespace HoneyDrunk.Agents.Abstractions;

internal static partial class AgentValidation
{
    internal static string NonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    internal static string AgentId(string value, string parameterName)
        => Match(value, AgentIdPattern(), "Use 1 to 64 lowercase identifier characters, starting with a letter or digit.", parameterName);

    internal static string ToolName(string value, string parameterName)
        => Match(value, ToolNamePattern(), "Use 1 to 64 lowercase tool name characters, starting with a letter.", parameterName);

    internal static string ToolVersion(string value, string parameterName)
        => Match(value, ToolVersionPattern(), "Use a numeric major.minor.patch version without leading zeros.", parameterName);

    internal static IReadOnlyList<string> NonBlankList(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var snapshot = values.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Values must be non-blank.", parameterName);
        }

        return Array.AsReadOnly(snapshot);
    }

    private static string Match(string value, Regex pattern, string message, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return pattern.IsMatch(value) ? value : throw new ArgumentException(message, parameterName);
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNamePattern();

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ToolVersionPattern();
}
