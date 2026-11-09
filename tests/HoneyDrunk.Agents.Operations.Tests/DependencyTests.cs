using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>Dependency direction of the Operations package (ADR-0110).</summary>
public sealed class DependencyTests
{
    /// <summary>
    /// The package references the agent runtime, the Abstractions of Operator, Audit and Kernel, ASP.NET Core and
    /// the base libraries, and nothing else: no node runtime, no data provider and no test package.
    /// </summary>
    [Fact]
    public void Operations_depends_only_on_allowed_assemblies()
    {
        string[] allowed =
        [
            "HoneyDrunk.Agents",
            "HoneyDrunk.Agents.Abstractions",
            "HoneyDrunk.Operator.Abstractions",
            "HoneyDrunk.Audit.Abstractions",
            "HoneyDrunk.Kernel.Abstractions",
        ];
        string[] allowedFamilies = ["Microsoft.AspNetCore", "Microsoft.Extensions", "System"];

        var unexpected = typeof(AgentOperationsPolicies).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .Where(name => !allowedFamilies.Any(family => name == family || name.StartsWith(family + ".", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(unexpected);
    }
}
