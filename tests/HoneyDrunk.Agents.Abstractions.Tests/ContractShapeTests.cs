using HoneyDrunk.Agents.Abstractions.Tests.ContractShape;
using System.Reflection;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>The contract-shape canary for the frozen Agents surfaces (ADR-0020 D10, invariant 137).</summary>
public sealed class ContractShapeTests
{
    private const string BaselinePath = "api/HoneyDrunk.Agents.Abstractions.contract-shape.txt";

    /// <summary>Gets the frozen roots. <see cref="IAgentLifecycle"/> is host-facing and stays outside.</summary>
    internal static Type[] Roots { get; } =
    [
        typeof(IAgent),
        typeof(IAgentRunContext),
        typeof(IToolInvoker),
        typeof(IAgentMemory),
    ];

    /// <summary>
    /// The frozen contracts match the committed shape. Set <c>HONEYDRUNK_UPDATE_CONTRACT_SHAPE=1</c>
    /// to regenerate it, which is refused unless <c>Version</c> has risen.
    /// </summary>
    [Fact]
    [Trait("Category", "ContractShape")]
    public void Frozen_contracts_match_the_committed_shape()
    {
        var shape = "# package: HoneyDrunk.Agents.Abstractions\n"
            + "# roots: " + string.Join(", ", Roots.Select(root => root.Name)) + "\n"
            + ContractShapeRenderer.Render(Roots);
        var baseline = Path.Join(FindRepositoryRoot(), BaselinePath.Replace('/', Path.DirectorySeparatorChar));
        var informational = typeof(IAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The Abstractions assembly has no informational version.");
        var update = Environment.GetEnvironmentVariable("HONEYDRUNK_UPDATE_CONTRACT_SHAPE") == "1";

        var (passed, message) = ContractShapeCheck.Verify(shape, baseline, informational.Split('+')[0], update);

        Assert.True(passed, message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "HoneyDrunk.Agents.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("HoneyDrunk.Agents.slnx was not found above the test output directory.");
    }
}
