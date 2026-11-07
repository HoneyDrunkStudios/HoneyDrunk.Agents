using HoneyDrunk.Agents.Abstractions.Tests.ContractShape;
using System.Reflection;
using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Checks the shape of the agent-facing contracts and their signature closure.</summary>
public sealed class ContractShapeTests
{
    private const string BaselineFile = "HoneyDrunk.Agents.Abstractions.contract-shape.txt";
    private const string UpdateVariable = "HONEYDRUNK_UPDATE_CONTRACT_SHAPE";
    private static readonly Type[] Roots = [typeof(IAgent), typeof(IAgentExecutionContext), typeof(IToolInvoker), typeof(IAgentMemory)];

    /// <summary>The four frozen contracts match the committed shape.</summary>
    [Fact]
    [Trait("Category", "ContractShape")]
    public void FrozenContracts_Match_CommittedShape()
    {
        var shape = ContractShapeRenderer.Render(Roots);
        var baseline = Path.Join(FindRepositoryRoot(), "api", BaselineFile);
        var version = typeof(IAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";

        var (passed, message) = ContractShapeCheck.Verify(shape, baseline, version, update);

        Assert.True(passed, message);
    }

    /// <summary>The host lifecycle remains outside the frozen agent-facing surface.</summary>
    [Fact]
    [Trait("Category", "ContractShape")]
    public void HostLifecycle_Remains_OutsideAgentContractClosure()
    {
        var shape = ContractShapeRenderer.Render(Roots);

        Assert.DoesNotContain(nameof(IAgentLifecycle), shape, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AgentRegistration), shape, StringComparison.Ordinal);
        Assert.Contains("HoneyDrunk.Kernel.Abstractions.Agents.IAgentExecutionContext", shape, StringComparison.Ordinal);
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

        throw new InvalidOperationException("The repository root (the folder holding HoneyDrunk.Agents.slnx) was not found.");
    }
}
