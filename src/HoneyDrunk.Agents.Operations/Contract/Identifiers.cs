using System.Text.RegularExpressions;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>The identifier rule of the contract: workload ids and environments.</summary>
internal static partial class Identifiers
{
    /// <summary>Checks the rule: 1 to 64 characters of a-z, 0-9, '.', '_' or '-', starting with a letter or digit.</summary>
    /// <param name="value">The candidate.</param>
    /// <returns>Whether it is an identifier.</returns>
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}
