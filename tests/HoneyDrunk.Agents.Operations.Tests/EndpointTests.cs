using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Operations.Runs;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Operator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>The v1 read endpoints, served by a real host on a test server.</summary>
public sealed class EndpointTests
{
    private const string Sentinel = "private-sentinel-7f3a";
    private static readonly TenantId Tenant = new("01JC0000000000000000000001");

    /// <summary>Gets every route for an agent called <c>a</c> and a run that does not exist.</summary>
    public static TheoryData<string> WorkloadRoutes => new()
    {
        "/v1/workloads/a",
        "/v1/workloads/a/runs",
        "/v1/workloads/a/runs/" + OperationsHost.RunId(1),
        "/v1/workloads/a/audit",
    };

    /// <summary>The workloads list has the v1 shape, passes every consumer rule and reflects real executions.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Workloads_returns_v1_shape_admin_accepts()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("reviewer", "Fixture Reviewer");
        await host.AddAgentAsync("summarizer", "Fixture Summarizer", (_, _) => Task.FromResult(new AgentResult(AgentOutcome.Halted, null, "stopped", null)));
        await host.ExecuteAsync("reviewer");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        await host.ExecuteAsync("summarizer");

        var root = await host.GetJsonAsync("/v1/workloads");

        ConsumerRules.AssertWorkloadsAccepted(root);
        Assert.Equal(host.Time.GetUtcNow(), root.GetProperty("generatedAt").GetDateTimeOffset());
        var workloads = root.GetProperty("workloads").EnumerateArray().ToArray();
        Assert.Equal(["reviewer", "summarizer"], workloads.Select(workload => workload.GetProperty("id").GetString()));
        Assert.Equal(["Fixture Reviewer", "Fixture Summarizer"], workloads.Select(workload => workload.GetProperty("displayName").GetString()));
        Assert.All(workloads, workload => Assert.Equal("test", workload.GetProperty("environment").GetString()));
        Assert.Equal("completed", Assert.Single(workloads[0].GetProperty("runs").EnumerateArray()).GetProperty("status").GetString());
        var halted = Assert.Single(workloads[1].GetProperty("runs").EnumerateArray());
        Assert.Equal("halted", halted.GetProperty("status").GetString());
        Assert.Equal(OperationsHost.Start.AddMinutes(1), halted.GetProperty("startedAt").GetDateTimeOffset());
    }

    /// <summary>A host with more than 20 agents lists the first 20 by id.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task More_than_20_agents_lists_20_by_id()
    {
        await using var host = await OperationsHost.StartAsync();
        var ids = Enumerable.Range(1, 22).Select(number => $"a{number:00}").ToArray();
        foreach (var id in ids.Reverse())
        {
            await host.AddAgentAsync(id);
        }

        var root = await host.GetJsonAsync("/v1/workloads");

        ConsumerRules.AssertWorkloadsAccepted(root);
        Assert.Equal(ids.Take(20), root.GetProperty("workloads").EnumerateArray().Select(workload => workload.GetProperty("id").GetString()));
    }

    /// <summary>A workload shows its 10 newest finished runs, newest first, and never a run that is still running.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Only_finished_runs_newest_first_at_most_10()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        for (var number = 1; number <= 12; number++)
        {
            await host.FinishRunAsync("a", number);
        }

        await host.Journal.StartedAsync("a", OperationsHost.RunId(13), OperationsHost.Start.AddMinutes(13), CancellationToken.None);

        var root = await host.GetJsonAsync("/v1/workloads");

        ConsumerRules.AssertWorkloadsAccepted(root);
        var runs = root.GetProperty("workloads")[0].GetProperty("runs").EnumerateArray().Select(run => run.GetProperty("id").GetString());
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(OperationsHost.RunId), runs);
    }

    /// <summary>A name that a display could not show safely is cleaned, cut to 64 characters, or replaced by the id.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Display_name_is_made_safe()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("blank", " \t ");
        await host.AddAgentAsync("control", " Line\r\nBreak\u0007 ");
        await host.AddAgentAsync("long", new string('n', 63) + "\U0001F600" + "tail");

        var root = await host.GetJsonAsync("/v1/workloads");

        ConsumerRules.AssertWorkloadsAccepted(root);
        var names = root.GetProperty("workloads").EnumerateArray().Select(workload => workload.GetProperty("displayName").GetString()).ToArray();
        string?[] expected = ["blank", "LineBreak", new string('n', 63)];
        Assert.Equal(expected, names);
    }

    /// <summary>
    /// Records from a faulty journal are made safe: a run with a bad id or no completion is dropped, a repeated id is
    /// kept once, an unknown status becomes failed, and a completion before the start is moved to the start.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Unsafe_journal_records_are_made_safe()
    {
        var start = OperationsHost.Start;
        var journal = new FixedRunJournal(
            new AgentRunRecord("not-a-run-id", "a", start, start, "completed"),
            new AgentRunRecord(new string('0', 32), "a", start, start, "completed"),
            new AgentRunRecord(OperationsHost.RunId(4), "a", start, null, null),
            new AgentRunRecord(OperationsHost.RunId(0xab).ToUpperInvariant(), "a", start, start.AddSeconds(1), "exploded"),
            new AgentRunRecord(OperationsHost.RunId(0xab), "a", start, start.AddSeconds(1), "completed"),
            new AgentRunRecord(OperationsHost.RunId(2), "a", start, start.AddSeconds(-5), "completed"));
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAgentRunJournal>(journal));
        await host.AddAgentAsync("a");

        var root = await host.GetJsonAsync("/v1/workloads");
        var page = await host.GetJsonAsync("/v1/workloads/a/runs");

        ConsumerRules.AssertWorkloadsAccepted(root);
        foreach (var runs in new[] { root.GetProperty("workloads")[0].GetProperty("runs"), page.GetProperty("runs") })
        {
            Assert.Equal([OperationsHost.RunId(0xab), OperationsHost.RunId(2)], runs.EnumerateArray().Select(run => run.GetProperty("id").GetString()));
            Assert.Equal("failed", runs[0].GetProperty("status").GetString());
            Assert.Equal(start, runs[1].GetProperty("completedAt").GetDateTimeOffset());
        }
    }

    /// <summary>Every route under an unknown workload is 404.</summary>
    /// <param name="path">The route.</param>
    /// <returns>A task.</returns>
    [Theory]
    [MemberData(nameof(WorkloadRoutes))]
    public async Task Unknown_workload_is_404(string path)
    {
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(new FakeAuditQuery()));
        await host.AddAgentAsync("b");
        await host.FinishRunAsync("a", 1);

        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A workload id that could never be an agent id is 404, never an error.</summary>
    /// <param name="id">The escaped id.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("Not%20An%20Id")]
    [InlineData("..%2F..%2Fsecret")]
    [InlineData("agent%3Aa")]
    [InlineData("%0A")]
    public async Task Hostile_workload_id_is_404(string id)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");

        using var detail = await host.Client.GetAsync(new Uri($"/v1/workloads/{id}", UriKind.Relative));
        using var runs = await host.Client.GetAsync(new Uri($"/v1/workloads/{id}/runs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, runs.StatusCode);
    }

    /// <summary>A decommissioned agent stays listed, shows its phase, and keeps its runs readable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Decommissioned_workload_is_listed_with_phase()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.AddAgentAsync("b");
        var runId = await host.FinishRunAsync("a", 1);
        await host.Lifecycle.DecommissionAsync("a");

        var list = await host.GetJsonAsync("/v1/workloads");
        var retired = await host.GetJsonAsync("/v1/workloads/a");
        var live = await host.GetJsonAsync("/v1/workloads/b");
        var runs = await host.GetJsonAsync("/v1/workloads/a/runs");

        Assert.Equal(["a", "b"], list.GetProperty("workloads").EnumerateArray().Select(workload => workload.GetProperty("id").GetString()));
        Assert.Equal("decommissioned", retired.GetProperty("phase").GetString());
        Assert.Equal("initialized", live.GetProperty("phase").GetString());
        Assert.Equal(runId, Assert.Single(runs.GetProperty("runs").EnumerateArray()).GetProperty("id").GetString());
    }

    /// <summary>A limit outside 1 to 50, or one that is not a whole number, is 400.</summary>
    /// <param name="limit">The escaped limit.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("abc")]
    [InlineData("100000")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("%2B5")]
    [InlineData("99999999999999999999")]
    [InlineData("5&limit=6")]
    public async Task Runs_limit_out_of_range_is_400(string limit)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");

        using var response = await host.Client.GetAsync(new Uri($"/v1/workloads/a/runs?limit={limit}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The smallest and largest limits are accepted.</summary>
    /// <param name="limit">The limit.</param>
    /// <param name="expected">How many runs come back.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    public async Task Runs_limit_bounds_are_accepted(int limit, int expected)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        for (var number = 1; number <= 60; number++)
        {
            await host.FinishRunAsync("a", number);
        }

        var page = await host.GetJsonAsync($"/v1/workloads/a/runs?limit={limit}");

        Assert.Equal(expected, page.GetProperty("runs").GetArrayLength());
    }

    /// <summary>A cursor that is not a run id is 400.</summary>
    /// <param name="before">The escaped cursor.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData("0000000000000000000000000000001")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("0000000000000000000000000000000g")]
    [InlineData("%27%20OR%201%3D1")]
    public async Task Malformed_cursor_is_400(string before)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.FinishRunAsync("a", 1);

        using var response = await host.Client.GetAsync(new Uri($"/v1/workloads/a/runs?before={before}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A well-formed cursor that names no kept run returns an empty page.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Unknown_cursor_returns_empty_page()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.FinishRunAsync("a", 1);

        var page = await host.GetJsonAsync($"/v1/workloads/a/runs?before={OperationsHost.RunId(99)}");

        Assert.Equal(1, page.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0, page.GetProperty("runs").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    /// <summary>Following the cursor reads every finished run once, newest first, and ends with no cursor.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Next_cursor_pages_through_runs()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        for (var number = 1; number <= 5; number++)
        {
            await host.FinishRunAsync("a", number);
        }

        var seen = new List<string?>();
        var sizes = new List<int>();
        string? cursor = null;
        do
        {
            var page = await host.GetJsonAsync("/v1/workloads/a/runs?limit=2" + (cursor is null ? string.Empty : "&before=" + cursor));
            var runs = page.GetProperty("runs").EnumerateArray().ToArray();
            Assert.All(runs, ConsumerRules.AssertRun);
            seen.AddRange(runs.Select(run => run.GetProperty("id").GetString()));
            sizes.Add(runs.Length);
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && sizes.Count < 10);

        Assert.Equal([2, 2, 1], sizes);
        Assert.Equal(Enumerable.Range(1, 5).Reverse().Select(OperationsHost.RunId), seen);
    }

    /// <summary>One finished run is read by id; a running run, an unknown run and a malformed id are 404.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Run_returns_one_finished_run()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.AddAgentAsync("b");
        var finished = await host.FinishRunAsync("a", 1, "rejected");
        await host.Journal.StartedAsync("a", OperationsHost.RunId(2), OperationsHost.Start, CancellationToken.None);

        var found = await host.GetJsonAsync($"/v1/workloads/a/runs/{finished}");
        using var running = await host.Client.GetAsync(new Uri($"/v1/workloads/a/runs/{OperationsHost.RunId(2)}", UriKind.Relative));
        using var unknown = await host.Client.GetAsync(new Uri($"/v1/workloads/a/runs/{OperationsHost.RunId(3)}", UriKind.Relative));
        using var malformed = await host.Client.GetAsync(new Uri("/v1/workloads/a/runs/not-a-run", UriKind.Relative));
        using var otherAgent = await host.Client.GetAsync(new Uri($"/v1/workloads/b/runs/{finished}", UriKind.Relative));

        Assert.Equal(1, found.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(host.Time.GetUtcNow(), found.GetProperty("generatedAt").GetDateTimeOffset());
        ConsumerRules.AssertRun(found.GetProperty("run"));
        Assert.Equal(finished, found.GetProperty("run").GetProperty("id").GetString());
        Assert.Equal("rejected", found.GetProperty("run").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, running.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherAgent.StatusCode);
    }

    /// <summary>The detail shows the agent's pause and its spend against the monthly budget, and no actions.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Detail_shows_pause_and_budget()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a", "Agent A");
        await host.AddAgentAsync("b");
        host.CostGuard.DefaultLimit = 10m;
        await host.CostGuard.RecordAsync(new CostEvent("cost-1", "a", Tenant.ToString(), "2026-11", 4m, "usd", "test", OperationsHost.Start, "correlation-1"));
        await host.CostGuard.RecordAsync(new CostEvent("cost-2", "a", Tenant.ToString(), "2026-10", 9m, "usd", "test", OperationsHost.Start, "correlation-1"));
        await host.Pauses.PauseAsync("agent:a", "operator-1", "Reviewing a bad run.");

        var paused = await host.GetJsonAsync("/v1/workloads/a");
        var running = await host.GetJsonAsync("/v1/workloads/b");

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","id":"a","displayName":"Agent A","environment":"test","phase":"initialized","pause":{"state":"available","value":{"pausedBy":"operator-1","reason":"Reviewing a bad run.","since":"2026-11-01T09:00:00Z"}},"budget":{"state":"available","value":{"window":"2026-11","spent":4,"limit":10,"remaining":6}},"supportedActions":[]}""",
            paused.GetRawText());
        Assert.Equal("available", running.GetProperty("pause").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, running.GetProperty("pause").GetProperty("value").ValueKind);
        Assert.Equal(0m, running.GetProperty("budget").GetProperty("value").GetProperty("spent").GetDecimal());
    }

    /// <summary>A cost guard that throws makes only the budget unavailable; its message never reaches the response.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Failing_cost_guard_marks_only_budget_unavailable()
    {
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<ICostGuard>(new ThrowingOperator()));
        await host.AddAgentAsync("a");
        await host.Pauses.PauseAsync("agent:a", "operator-1", "Paused.");

        var detail = await host.GetJsonAsync("/v1/workloads/a");

        Assert.Equal("""{"state":"unavailable","value":null}""", detail.GetProperty("budget").GetRawText());
        Assert.Equal("available", detail.GetProperty("pause").GetProperty("state").GetString());
        Assert.Equal("operator-1", detail.GetProperty("pause").GetProperty("value").GetProperty("pausedBy").GetString());
        Assert.DoesNotContain(ThrowingOperator.Secret, detail.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A pause store that fails makes only the pause unavailable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Failing_pause_store_marks_only_pause_unavailable()
    {
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAgentPauses>(new ThrowingOperator()));
        await host.AddAgentAsync("a");

        var detail = await host.GetJsonAsync("/v1/workloads/a");

        Assert.Equal("""{"state":"unavailable","value":null}""", detail.GetProperty("pause").GetRawText());
        Assert.Equal("available", detail.GetProperty("budget").GetProperty("state").GetString());
        Assert.DoesNotContain(ThrowingOperator.Secret, detail.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A host with no approval desk, pause store or cost guard reports those parts as unavailable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Missing_operator_sources_are_unavailable()
    {
        await using var host = await OperationsHost.StartAsync(withOperator: false);
        await host.AddAgentAsync("a");

        var detail = await host.GetJsonAsync("/v1/workloads/a");
        var approvals = await host.GetJsonAsync("/v1/approvals");

        Assert.Equal("unavailable", detail.GetProperty("pause").GetProperty("state").GetString());
        Assert.Equal("unavailable", detail.GetProperty("budget").GetProperty("state").GetString());
        Assert.Equal("unavailable", approvals.GetProperty("state").GetString());
        Assert.Equal(0, approvals.GetProperty("approvals").GetArrayLength());
    }

    /// <summary>Pending approvals are listed for this host's agents only, newest first, and decided ones are gone.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Approvals_list_pending_for_listed_agents_only()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.AddAgentAsync("b");
        host.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        var expiry = OperationsHost.Start.AddHours(1);
        await host.Gate.RequestAsync(Request("approval-1", "agent:a", "tool.publish", expiry));
        host.Time.Advance(TimeSpan.FromMinutes(1));
        await host.Gate.RequestAsync(Request("approval-2", "agent:ghost", "tool.publish", expiry));
        await host.Gate.RequestAsync(Request("approval-3", "user:a", "tool.publish", expiry));
        await host.Gate.RequestAsync(Request("approval-4", "agent:b", "tool.delete", expiry));
        await host.Gate.RequestAsync(Request("approval-5", "agent:b", "tool.send", expiry));
        host.Gate.Decide("approval-5", ApprovalOutcome.Denied);

        var approvals = await host.GetJsonAsync("/v1/approvals");

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:01:00Z","state":"available","approvals":[{"approvalId":"approval-4","workloadId":"b","action":"tool.delete","requestedAt":"2026-11-01T09:01:00Z","expiresAt":"2026-11-01T10:00:00Z"},{"approvalId":"approval-1","workloadId":"a","action":"tool.publish","requestedAt":"2026-11-01T09:00:00Z","expiresAt":"2026-11-01T10:00:00Z"}]}""",
            approvals.GetRawText());
    }

    /// <summary>A desk that throws makes the approvals unavailable, with an empty list and no message.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Approvals_unavailable_when_desk_throws()
    {
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IApprovalDesk>(new ThrowingOperator()));
        await host.AddAgentAsync("a");

        var approvals = await host.GetJsonAsync("/v1/approvals");

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","state":"unavailable","approvals":[]}""",
            approvals.GetRawText());
    }

    /// <summary>The audit lists the last 7 days of entries whose target is the agent, under either id form, newest first.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Audit_reads_agent_targets_newest_first()
    {
        var audit = new FakeAuditQuery();
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");
        var now = OperationsHost.Start;
        audit.Add(Entry("1", now.AddDays(-8), "agent.too-old", "a"));
        audit.Add(Entry("2", now.AddDays(-3), "agent.paused", "agent:a", AuditOutcome.Succeeded, "Reviewing a bad run."));
        audit.Add(Entry("3", now.AddDays(-1), "approval.denied", "a", AuditOutcome.Denied));
        audit.Add(Entry("4", now.AddHours(-2), "agent.other", "b"));
        audit.Add(Entry("5", now.AddMinutes(-5), "agent.resumed", "agent:a"));

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","state":"available","entries":[{"at":"2026-11-01T08:55:00Z","event":"agent.resumed","actor":"operator-1","outcome":"succeeded","targetType":"agent","targetId":"agent:a","reason":null},{"at":"2026-10-31T09:00:00Z","event":"approval.denied","actor":"operator-1","outcome":"denied","targetType":"agent","targetId":"a","reason":null},{"at":"2026-10-29T09:00:00Z","event":"agent.paused","actor":"operator-1","outcome":"succeeded","targetType":"agent","targetId":"agent:a","reason":"Reviewing a bad run."}]}""",
            response.GetRawText());
        Assert.All(audit.Filters, filter => Assert.Contains(filter.TargetId, new[] { "a", "agent:a" }));
        Assert.All(audit.Filters, filter => Assert.True(filter.Since >= now.AddDays(-7) && filter.Until == now));
    }

    /// <summary>A busy agent's audit is cut to its 50 newest entries, and entries about other targets are never shown.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Audit_returns_at_most_50_newest_about_the_agent()
    {
        var audit = new FakeAuditQuery { IgnoreTarget = true };
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        await host.AddAgentAsync("a");
        for (var number = 1; number <= 60; number++)
        {
            audit.Add(Entry($"a{number}", OperationsHost.Start.AddSeconds(-number), "agent.ran", "a"));
            audit.Add(Entry($"b{number}", OperationsHost.Start.AddSeconds(-number), "agent.ran", "agent:b"));
        }

        var entries = (await host.GetJsonAsync("/v1/workloads/a/audit")).GetProperty("entries").EnumerateArray().ToArray();

        Assert.Equal(50, entries.Length);
        Assert.All(entries, entry => Assert.Equal("a", entry.GetProperty("targetId").GetString()));
        Assert.Equal(
            Enumerable.Range(1, 50).Select(number => OperationsHost.Start.AddSeconds(-number)),
            entries.Select(entry => entry.GetProperty("at").GetDateTimeOffset()));
    }

    /// <summary>A host with no audit query reports the audit as unavailable.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Audit_unavailable_without_query()
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","state":"unavailable","entries":[]}""",
            response.GetRawText());
    }

    /// <summary>An audit query that throws makes the audit unavailable, with no message.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Audit_unavailable_when_query_throws()
    {
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(new FakeAuditQuery { Throw = true }));
        await host.AddAgentAsync("a");

        var response = await host.GetJsonAsync("/v1/workloads/a/audit");

        Assert.Equal("unavailable", response.GetProperty("state").GetString());
        Assert.DoesNotContain("audit-secret", response.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// No route returns a request payload, an agent's output, an approval's context, or an audit entry's changes or
    /// metadata.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task No_route_leaks_private_sentinels()
    {
        var audit = new FakeAuditQuery();
        await using var host = await OperationsHost.StartAsync(services => services.AddSingleton<IAuditQuery>(audit));
        using var output = JsonDocument.Parse($$"""{"answer":"{{Sentinel}}"}""");
        await host.AddAgentAsync("a", run: (_, _) => Task.FromResult(new AgentResult(AgentOutcome.Completed, output.RootElement.Clone(), null, null)));
        await host.AddAgentAsync("b", run: (_, _) => throw new InvalidOperationException(Sentinel));
        Assert.True((await host.ExecuteAsync("a", $$"""{"prompt":"{{Sentinel}}"}""")).Output.HasValue);
        Assert.Equal(AgentOutcome.Failed, (await host.ExecuteAsync("b", $$"""{"prompt":"{{Sentinel}}"}""")).Outcome);
        host.Gate.DefaultOutcome = ApprovalOutcome.Pending;
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["arguments"] = Sentinel, [Sentinel] = "key" };
        await host.Gate.RequestAsync(new ApprovalRequest("approval-1", "agent:a", "tool.publish", context, Sentinel, OperationsHost.Start.AddHours(1), Sentinel));
        audit.Add(new AuditEntry(
            new AuditEntryId("entry-1"),
            OperationsHost.Start.AddMinutes(-1),
            "operator-1",
            "agent.configured",
            AuditCategory.AgentAction,
            AuditOutcome.Succeeded,
            new AuditTarget("agent", "a", Sentinel),
            Tenant,
            Sentinel,
            AuditOperation.Update,
            [new AuditChange("prompt", Sentinel, Sentinel)],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["arguments"] = Sentinel }));
        var runId = (await host.GetJsonAsync("/v1/workloads/a/runs")).GetProperty("runs")[0].GetProperty("id").GetString();
        string[] paths = ["/v1/workloads", "/v1/workloads/a", "/v1/workloads/b", "/v1/workloads/a/runs", "/v1/workloads/b/runs", $"/v1/workloads/a/runs/{runId}", "/v1/approvals", "/v1/workloads/a/audit"];

        foreach (var path in paths)
        {
            var body = (await host.GetJsonAsync(path)).GetRawText();

            Assert.DoesNotContain(Sentinel, body, StringComparison.Ordinal);
        }

        Assert.Equal(1, (await host.GetJsonAsync("/v1/approvals")).GetProperty("approvals").GetArrayLength());
        Assert.Equal(1, (await host.GetJsonAsync("/v1/workloads/a/audit")).GetProperty("entries").GetArrayLength());
    }

    /// <summary>A request with no credentials is rejected on every route.</summary>
    /// <param name="path">The route.</param>
    /// <returns>A task.</returns>
    [Theory]
    [MemberData(nameof(WorkloadRoutes))]
    [InlineData("/v1/workloads")]
    [InlineData("/v1/approvals")]
    public async Task Anonymous_request_is_rejected(string path)
    {
        await using var host = await OperationsHost.StartAsync();
        await host.AddAgentAsync("a");
        await host.FinishRunAsync("a", 1);

        using var response = await host.Anonymous.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Mapping the endpoints in a host that has not defined the read policy fails at startup.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Missing_policy_fails_at_startup()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => OperationsHost.StartAsync(registerPolicy: false));

        Assert.Contains(AgentOperationsPolicies.Read, failure.Message, StringComparison.Ordinal);
        Assert.Equal("agent-operations:read", AgentOperationsPolicies.Read);
    }

    /// <summary>The environment is required and must be an identifier.</summary>
    /// <param name="environment">The configured environment.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not Valid")]
    [InlineData("-leading")]
    [InlineData("production/eu")]
    public async Task Environment_must_be_an_identifier(string? environment)
    {
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => OperationsHost.StartAsync(environment: environment));

        Assert.Contains("Environment", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Mapping the endpoints without registering the operations services fails with a clear message.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Mapping_without_services_fails_at_startup()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options => options.AddPolicy(AgentOperationsPolicies.Read, policy => policy.RequireAuthenticatedUser()));
        await using var app = builder.Build();

        var failure = Assert.Throws<InvalidOperationException>(() => app.MapAgentOperations());

        Assert.Contains("AddAgentOperations", failure.Message, StringComparison.Ordinal);
    }

    private static ApprovalRequest Request(string approvalId, string subject, string action, DateTimeOffset expiry) =>
        new(approvalId, subject, action, new Dictionary<string, string>(StringComparer.Ordinal), "tool:any@1", expiry, "correlation-1");

    private static AuditEntry Entry(string id, DateTimeOffset at, string eventName, string targetId, AuditOutcome outcome = AuditOutcome.Succeeded, string? reason = null) =>
        new(new AuditEntryId(id), at, "operator-1", eventName, AuditCategory.AgentAction, outcome, new AuditTarget("agent", targetId), Tenant, Reason: reason);
}
