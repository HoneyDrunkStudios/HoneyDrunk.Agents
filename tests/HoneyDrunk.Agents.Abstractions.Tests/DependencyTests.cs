using Xunit;

namespace HoneyDrunk.Agents.Abstractions.Tests;

/// <summary>Dependency direction of the Abstractions package (ADR-0020 D2, invariants 131 and 136).</summary>
public sealed class DependencyTests
{
    /// <summary>The only HoneyDrunk reference is Kernel's Abstractions.</summary>
    [Fact]
    public void Abstractions_reference_only_kernel_abstractions()
    {
        var honeyDrunkReferences = typeof(AgentDescriptor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("HoneyDrunk.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["HoneyDrunk.Kernel.Abstractions"], honeyDrunkReferences);
    }

    /// <summary>No AI runtime or sibling node assembly is referenced.</summary>
    [Fact]
    public void Abstractions_reference_no_ai_runtime()
    {
        string[] forbidden = ["Microsoft.Extensions.AI", "HoneyDrunk.AI", "HoneyDrunk.Capabilities", "HoneyDrunk.Memory", "HoneyDrunk.Operator"];

        var references = typeof(AgentDescriptor).Assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
