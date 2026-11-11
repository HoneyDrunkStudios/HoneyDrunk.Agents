namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>Finds checked-in files from the test output directory.</summary>
internal static class RepoFiles
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    public static string ContractDirectory => Path.Join(RootDirectory.Value, "contracts", "operations", "v1");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "HoneyDrunk.Agents.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
