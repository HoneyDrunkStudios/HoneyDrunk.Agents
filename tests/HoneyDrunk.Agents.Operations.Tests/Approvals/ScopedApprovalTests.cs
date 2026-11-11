using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests.Approvals;

/// <summary>Scoped approval reads preserve the released completeness signal without hiding matching work.</summary>
public sealed class ScopedApprovalTests
{
    /// <summary>More than a page of newer unrelated requests cannot hide an older listed workload's approval.</summary>
    /// <param name="otherSubject">A subject outside this host's workload list.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("user:a")]
    [InlineData("agent:ghost")]
    public async Task Other_subjects_cannot_hide_older_matching_approval(string otherSubject)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        host.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        await host.Gate.RequestAsync(Request("matching", "agent:a"));
        host.Time.Advance(TimeSpan.FromMinutes(1));
        for (var index = 0; index < 101; index++)
        {
            await host.Gate.RequestAsync(Request($"other-{index:D3}", otherSubject));
        }

        var response = await host.GetJsonAsync("/v1/approvals");

        Assert.Equal("available", response.GetProperty("state").GetString());
        Assert.False(response.GetProperty("truncated").GetBoolean());
        Assert.Equal("matching", Assert.Single(response.GetProperty("approvals").EnumerateArray()).GetProperty("approvalId").GetString());
    }

    /// <summary>A full matching page retains the owner's conservative truncated signal.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Full_scoped_page_is_truncated_and_newest_first()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        host.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        for (var index = 0; index < 101; index++)
        {
            await host.Gate.RequestAsync(Request($"approval-{index:D3}", "agent:a"));
            host.Time.Advance(TimeSpan.FromSeconds(1));
        }

        var response = await host.GetJsonAsync("/v1/approvals");

        Assert.Equal("available", response.GetProperty("state").GetString());
        Assert.True(response.GetProperty("truncated").GetBoolean());
        Assert.Equal(Enumerable.Range(1, 100).Reverse().Select(index => $"approval-{index:D3}"), response.GetProperty("approvals").EnumerateArray().Select(approval => approval.GetProperty("approvalId").GetString()));
    }

    /// <summary>Only the 20 workloads actually listed are passed to one bounded scoped query.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task One_query_uses_exactly_the_exposed_workloads()
    {
        var desk = new CapturingApprovalDesk();
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IApprovalDesk>(desk));
        for (var index = 21; index >= 1; index--)
        {
            await host.AddAgentAsync($"a{index:D2}");
        }

        var workloads = await host.GetJsonAsync("/v1/workloads");
        var response = await host.GetJsonAsync("/v1/approvals");

        var query = Assert.Single(desk.Queries);
        Assert.Equal(100, query.Limit);
        Assert.Equal(workloads.GetProperty("workloads").EnumerateArray().Select(workload => "agent:" + workload.GetProperty("id").GetString()), query.Subjects.Order(StringComparer.Ordinal));
        Assert.Equal(0, desk.LegacyReads);
        Assert.Equal("available", response.GetProperty("state").GetString());
        Assert.False(response.GetProperty("truncated").GetBoolean());
    }

    /// <summary>An empty host does not ask a provider to interpret an empty subject scope.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task No_workloads_means_no_source_read()
    {
        var desk = new CapturingApprovalDesk();
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IApprovalDesk>(desk));

        var response = await host.GetJsonAsync("/v1/approvals");

        Assert.Empty(desk.Queries);
        Assert.Equal(0, desk.LegacyReads);
        Assert.Equal("available", response.GetProperty("state").GetString());
        Assert.False(response.GetProperty("truncated").GetBoolean());
        Assert.Empty(response.GetProperty("approvals").EnumerateArray());
    }

    /// <summary>A provider violating the requested limit or subject scope cannot expose a misleading partial page.</summary>
    /// <param name="oversized">Whether the violation is the page size rather than the subject.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nonconforming_scoped_provider_is_unavailable(bool oversized)
    {
        var desk = new CapturingApprovalDesk
        {
            Results = oversized
                ? [.. Enumerable.Range(0, 101).Select(index => new PendingApproval(Request($"approval-{index}", "agent:a"), OperationsHost.Start))]
                : [new PendingApproval(Request("private-approval", "agent:ghost"), OperationsHost.Start)],
        };
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IApprovalDesk>(desk));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/approvals");

        Assert.Single(desk.Queries);
        Assert.Equal(0, desk.LegacyReads);
        Assert.Equal("unavailable", response.GetProperty("state").GetString());
        Assert.False(response.GetProperty("truncated").GetBoolean());
        Assert.Empty(response.GetProperty("approvals").EnumerateArray());
        Assert.DoesNotContain("private-approval", response.GetRawText(), StringComparison.Ordinal);
    }

    private static ApprovalRequest Request(string id, string subject) =>
        new(id, subject, "tool.publish", new Dictionary<string, string>(StringComparer.Ordinal), "tool:any@1", OperationsHost.Start.AddHours(1), "correlation");
}
