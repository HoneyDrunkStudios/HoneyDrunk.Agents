using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>
/// The checks the contract's first consumer applies to <c>GET /v1/workloads</c>. That consumer shows the whole view
/// as unavailable when any of them fails, so every workloads response and fixture must pass them.
/// </summary>
internal static class ConsumerRules
{
    private static readonly string[] Statuses = ["completed", "rejected", "halted", "failed", "awaitingapproval", "canceled", "propagated"];

    public static void AssertWorkloadsAccepted(JsonElement root)
    {
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("generatedAt").GetDateTimeOffset().Offset);
        var workloads = root.GetProperty("workloads").EnumerateArray().ToArray();
        Assert.InRange(workloads.Length, 0, 20);
        AssertUnique(workloads.Select(workload => workload.GetProperty("id").GetString()));
        foreach (var workload in workloads)
        {
            AssertIdentifier(workload.GetProperty("id").GetString());
            AssertIdentifier(workload.GetProperty("environment").GetString());
            var name = workload.GetProperty("displayName").GetString();
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.InRange(name!.Length, 1, 64);
            Assert.DoesNotContain(name, char.IsControl);
            var runs = workload.GetProperty("runs").EnumerateArray().ToArray();
            Assert.InRange(runs.Length, 0, 10);
            AssertUnique(runs.Select(run => run.GetProperty("id").GetString()));
            Assert.All(runs, AssertRun);
        }
    }

    public static void AssertRun(JsonElement run)
    {
        var id = run.GetProperty("id").GetString();
        Assert.True(Guid.TryParseExact(id, "N", out var parsed) && parsed != Guid.Empty, $"'{id}' is not a non-empty GUID in N format.");
        Assert.Contains(run.GetProperty("status").GetString(), Statuses);
        Assert.True(run.GetProperty("completedAt").GetDateTimeOffset() >= run.GetProperty("startedAt").GetDateTimeOffset());
    }

    private static void AssertIdentifier(string? value)
    {
        Assert.NotNull(value);
        Assert.Matches(@"\A[a-z0-9][a-z0-9._-]{0,63}\z", value);
    }

    private static void AssertUnique(IEnumerable<string?> ids)
    {
        var all = ids.ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }
}
