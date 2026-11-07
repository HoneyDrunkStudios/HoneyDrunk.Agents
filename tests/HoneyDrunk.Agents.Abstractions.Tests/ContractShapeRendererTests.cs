using HoneyDrunk.Agents.Abstractions.Tests.ContractShape;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Behavior of the contract-shape renderer and check.</summary>
public sealed class ContractShapeRendererTests
{
    private const string Prefix = "HoneyDrunk.Agents.Abstractions.Tests.ContractShape.ShapeFixtures+";

    /// <summary>Parameter nullability, default values and enum numbers are recorded.</summary>
    [Fact]
    public void Render_records_nullability_and_defaults()
    {
        var lines = Lines(ContractShapeRenderer.Render([typeof(ShapeFixtures.IShapeFixtureRoot)]));

        Assert.Contains(
            Prefix + "IShapeFixtureRoot method: System.Threading.Tasks.Task<" + Prefix + "ShapeFixtureRecord?> FindAsync(System.String name, System.String? filter = null, System.Int32 limit = 10, System.Threading.CancellationToken cancellationToken = default)",
            lines);
        Assert.Contains(Prefix + "IShapeFixtureRoot property: System.String Name { get; }", lines);
        Assert.Contains(Prefix + "ShapeFixtureRecord property: System.String? Note { get; init; }", lines);
        Assert.Contains(Prefix + "ShapeFixtureKind value: Second = 2", lines);
    }

    /// <summary>Only the roots and the public types their signatures reach are rendered.</summary>
    [Fact]
    public void Render_follows_the_signature_closure_only()
    {
        var shape = ContractShapeRenderer.Render([typeof(ShapeFixtures.IShapeFixtureRoot)]);
        var lines = Lines(shape);

        Assert.Contains(lines, line => line.StartsWith(Prefix + "IShapeFixtureRoot = ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith(Prefix + "ShapeFixtureRecord = ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith(Prefix + "ShapeFixtureKind = ", StringComparison.Ordinal));
        Assert.DoesNotContain("ShapeFixtureUnreferenced", shape, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.StartsWith("System.", StringComparison.Ordinal));
    }

    /// <summary>Two renders of the same roots, in any order, are identical.</summary>
    [Fact]
    public void Render_is_stable_across_runs()
    {
        var first = ContractShapeRenderer.Render([typeof(ShapeFixtures.IShapeFixtureRoot), typeof(ShapeFixtures.ShapeFixtureKind)]);
        var second = ContractShapeRenderer.Render([typeof(ShapeFixtures.ShapeFixtureKind), typeof(ShapeFixtures.IShapeFixtureRoot)]);

        Assert.Equal(first, second);
        Assert.Equal(Lines(first).Where(IsBody).Order(StringComparer.Ordinal), Lines(first).Where(IsBody));
    }

    /// <summary>A changed body without an update request fails and lists the changed lines.</summary>
    [Fact]
    public void Verify_fails_on_drift_without_update()
    {
        using var baseline = new TempBaseline(Shape("A x", "A y"), "0.1.0");

        var (passed, message) = ContractShapeCheck.Verify(Shape("A x", "A z"), baseline.Path, "0.1.0", updateRequested: false);

        Assert.False(passed);
        Assert.Contains("+ A z", message, StringComparison.Ordinal);
        Assert.Contains("- A y", message, StringComparison.Ordinal);
        Assert.Contains("HONEYDRUNK_UPDATE_CONTRACT_SHAPE", message, StringComparison.Ordinal);
    }

    /// <summary>An update at the baseline's own version is refused and leaves the file alone.</summary>
    [Fact]
    public void Verify_refuses_update_at_the_same_version()
    {
        using var baseline = new TempBaseline(Shape("A x"), "0.1.0");
        var before = File.ReadAllText(baseline.Path);

        var (passed, message) = ContractShapeCheck.Verify(Shape("A x", "A y"), baseline.Path, "0.1.0+abc123", updateRequested: true);

        Assert.False(passed);
        Assert.Contains("0.1.0", message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(baseline.Path));
    }

    /// <summary>An update at a lower version is refused too.</summary>
    [Fact]
    public void Verify_refuses_update_at_a_lower_version()
    {
        using var baseline = new TempBaseline(Shape("A x"), "0.2.0");

        var (passed, _) = ContractShapeCheck.Verify(Shape("A y"), baseline.Path, "0.1.9", updateRequested: true);

        Assert.False(passed);
    }

    /// <summary>An update at a higher version rewrites the baseline with the new version and body.</summary>
    [Fact]
    public void Verify_accepts_update_at_a_higher_version()
    {
        using var baseline = new TempBaseline(Shape("A x"), "0.1.0");

        var (passed, _) = ContractShapeCheck.Verify(Shape("A x", "A y"), baseline.Path, "0.2.0+abc123", updateRequested: true);

        Assert.True(passed);
        var written = Lines(File.ReadAllText(baseline.Path));
        Assert.Equal(["# package: Test.Package", "# version: 0.2.0", "# roots: Test.Root", "A x", "A y"], written);
        Assert.True(ContractShapeCheck.Verify(Shape("A x", "A y"), baseline.Path, "0.2.0", updateRequested: false).passed);
    }

    /// <summary>The same body passes even when the assembly version moved.</summary>
    [Fact]
    public void Verify_passes_when_only_the_version_moved()
    {
        using var baseline = new TempBaseline(Shape("A x"), "0.1.0");

        var (passed, _) = ContractShapeCheck.Verify(Shape("A x"), baseline.Path, "0.3.0", updateRequested: false);

        Assert.True(passed);
        Assert.Contains("# version: 0.1.0", File.ReadAllText(baseline.Path), StringComparison.Ordinal);
    }

    /// <summary>A missing baseline fails and names the update switch.</summary>
    [Fact]
    public void Verify_fails_without_a_baseline()
    {
        var path = Path.Join(Path.GetTempPath(), $"contract-shape-{Guid.NewGuid():N}.txt");

        var (passed, message) = ContractShapeCheck.Verify(Shape("A x"), path, "0.1.0", updateRequested: false);

        Assert.False(passed);
        Assert.Contains("HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1", message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    /// <summary>A missing baseline is written when an update is requested.</summary>
    [Fact]
    public void Verify_writes_a_missing_baseline_on_update()
    {
        using var baseline = new TempBaseline(null, null);

        var (passed, _) = ContractShapeCheck.Verify(Shape("A x"), baseline.Path, "0.1.0", updateRequested: true);

        Assert.True(passed);
        Assert.Contains("# version: 0.1.0", File.ReadAllText(baseline.Path), StringComparison.Ordinal);
    }

    /// <summary>Windows line endings in the baseline do not count as drift.</summary>
    [Fact]
    public void Verify_ignores_line_endings()
    {
        using var baseline = new TempBaseline(null, null);
        File.WriteAllText(baseline.Path, "# package: Test.Package\r\n# version: 0.1.0\r\n# roots: Test.Root\r\nA x\r\n");

        Assert.True(ContractShapeCheck.Verify(Shape("A x"), baseline.Path, "0.1.0", updateRequested: false).passed);
    }

    private static string Shape(params string[] body) => "# package: Test.Package\n# roots: Test.Root\n" + string.Join('\n', body) + "\n";

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

    private static bool IsBody(string line) => !line.StartsWith('#');

    private sealed class TempBaseline : IDisposable
    {
        public TempBaseline(string? shape, string? version)
        {
            this.Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"contract-shape-{Guid.NewGuid():N}.txt");
            if (shape is not null && version is not null)
            {
                var lines = Lines(shape);
                File.WriteAllText(this.Path, string.Join('\n', [lines[0], $"# version: {version}", .. lines.Skip(1)]) + "\n");
            }
        }

        public string Path { get; }

        public void Dispose() => File.Delete(this.Path);
    }
}
