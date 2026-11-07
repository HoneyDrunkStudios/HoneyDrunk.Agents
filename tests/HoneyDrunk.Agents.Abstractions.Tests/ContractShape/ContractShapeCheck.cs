using System.Text;

namespace HoneyDrunk.Agents.Abstractions.Tests.ContractShape;

/// <summary>
/// Compares a rendered contract shape with the committed baseline. A changed body fails unless an update is
/// requested, and an update is refused unless the assembly version is greater than the baseline's version.
/// </summary>
internal static class ContractShapeCheck
{
    private const string VersionHeader = "# version: ";
    private const string PackageHeader = "# package: ";
    private const int MaxListedLines = 20;
    private const string UpdateHint =
        "If the change is intended, raise <Version> in Directory.Build.props, then run the ContractShape tests with HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1 " +
        "(dotnet test --filter Category=ContractShape) and commit the regenerated baseline.";

    private const string CreateHint =
        "Run the ContractShape tests with HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1 (dotnet test --filter Category=ContractShape) and commit the new baseline.";

    /// <summary>Checks the current shape against the baseline file, and rewrites the file when an allowed update is requested.</summary>
    /// <param name="currentShape">The rendered shape: <c># package:</c> and <c># roots:</c> header lines, then the body.</param>
    /// <param name="baselineFile">The path of the committed baseline file.</param>
    /// <param name="assemblyVersion">The assembly's informational version; any text from <c>+</c> on is ignored.</param>
    /// <param name="updateRequested">Whether to regenerate the baseline.</param>
    /// <returns>Whether the check passed, and a message saying why.</returns>
    public static (bool passed, string message) Verify(string currentShape, string? baselineFile, string assemblyVersion, bool updateRequested)
    {
        ArgumentNullException.ThrowIfNull(currentShape);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyVersion);
        var version = assemblyVersion.Split('+')[0];
        var current = Parse(currentShape);

        if (string.IsNullOrWhiteSpace(baselineFile))
        {
            return (false, "No contract-shape baseline path was given.");
        }

        if (!File.Exists(baselineFile))
        {
            if (!updateRequested)
            {
                return (false, $"No contract-shape baseline exists at {baselineFile}. {CreateHint}");
            }

            Write(baselineFile, current, version);
            return (true, $"Wrote a new contract-shape baseline at version {version}.");
        }

        var baseline = Parse(File.ReadAllText(baselineFile));
        if (baseline.body.SequenceEqual(current.body, StringComparer.Ordinal))
        {
            return (true, "The contract shape matches the baseline.");
        }

        var baselineVersion = baseline.header.FirstOrDefault(line => line.StartsWith(VersionHeader, StringComparison.Ordinal))?[VersionHeader.Length..].Trim() ?? string.Empty;
        if (!updateRequested)
        {
            return (false, $"The contract shape changed since version {baselineVersion}.\n{Difference(baseline.body, current.body)}{UpdateHint}");
        }

        if (!IsGreater(version, baselineVersion))
        {
            return (false, $"Refusing to update the contract-shape baseline: the shape changed but version {version} is not greater than the baseline version {baselineVersion}. Raise <Version> first.");
        }

        Write(baselineFile, current, version);
        return (true, $"Updated the contract-shape baseline from version {baselineVersion} to {version}.");
    }

    private static (List<string> header, List<string> body) Parse(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
        return (lines.Where(line => line.StartsWith('#')).ToList(), lines.Where(line => !line.StartsWith('#')).ToList());
    }

    private static void Write(string path, (List<string> header, List<string> body) shape, string version)
    {
        var builder = new StringBuilder();
        var header = shape.header.Where(line => !line.StartsWith(VersionHeader, StringComparison.Ordinal)).ToList();
        var package = header.FindIndex(line => line.StartsWith(PackageHeader, StringComparison.Ordinal));
        header.Insert(package + 1, VersionHeader + version);
        foreach (var line in header.Concat(shape.body))
        {
            builder.Append(line).Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, builder.ToString());
    }

    private static string Difference(List<string> baseline, List<string> current)
    {
        var builder = new StringBuilder();
        var added = current.Except(baseline, StringComparer.Ordinal).ToList();
        var removed = baseline.Except(current, StringComparer.Ordinal).ToList();
        Append(builder, "Added", '+', added);
        Append(builder, "Removed", '-', removed);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string title, char marker, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        builder.Append(title).Append(" (").Append(lines.Count).Append("):\n");
        foreach (var line in lines.Take(MaxListedLines))
        {
            builder.Append(marker).Append(' ').Append(line).Append('\n');
        }

        if (lines.Count > MaxListedLines)
        {
            builder.Append("... and ").Append(lines.Count - MaxListedLines).Append(" more\n");
        }
    }

    private static bool IsGreater(string candidate, string baseline) =>
        System.Version.TryParse(candidate.Split('-')[0], out var next)
        && System.Version.TryParse(baseline.Split('-')[0], out var previous)
        && next > previous;
}
