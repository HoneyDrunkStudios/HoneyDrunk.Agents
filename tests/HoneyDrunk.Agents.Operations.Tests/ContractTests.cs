using HoneyDrunk.Agents.Operations.Contract;
using HoneyDrunk.Operator.Abstractions;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>The v1 response records, their JSON and the checked-in schemas.</summary>
public sealed class ContractTests
{
    private const string RunId = "019a3f2e5c007000800000000000000a";
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Gets each schema file with its response record and the records its <c>$defs</c> describe.</summary>
    public static TheoryData<string, Type, string[], Type[]> Schemas => new()
    {
        { "workloads.schema.json", typeof(WorkloadsResponse), ["workloadSummary", "runSummary"], [typeof(WorkloadSummary), typeof(RunSummary)] },
        { "workload.schema.json", typeof(WorkloadDetail), ["pauseSection", "pauseView", "budgetSection", "budgetView"], [typeof(Section<PauseView>), typeof(PauseView), typeof(Section<BudgetView>), typeof(BudgetView)] },
        { "runs.schema.json", typeof(RunsPage), ["runSummary"], [typeof(RunSummary)] },
        { "run.schema.json", typeof(RunDetail), ["runSummary"], [typeof(RunSummary)] },
        { "approvals.schema.json", typeof(ApprovalsResponse), ["pendingApprovalView"], [typeof(PendingApprovalView)] },
        { "audit.schema.json", typeof(AuditResponse), ["auditEntryView"], [typeof(AuditEntryView)] },
    };

    /// <summary>The workloads response has exactly the v1 property names, in order.</summary>
    [Fact]
    public void Workloads_response_serializes_to_v1_names()
    {
        var response = new WorkloadsResponse(At, [new WorkloadSummary("reviewer", "Fixture Reviewer", "local", [new RunSummary(RunId, "completed", At, At.AddSeconds(2))])]);

        var json = JsonSerializer.Serialize(response, OperationsJson.Options);

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","workloads":[{"id":"reviewer","displayName":"Fixture Reviewer","environment":"local","runs":[{"id":"019a3f2e5c007000800000000000000a","status":"completed","startedAt":"2026-11-01T09:00:00Z","completedAt":"2026-11-01T09:00:02Z"}]}]}""",
            json);
    }

    /// <summary>The workload detail has exactly the v1 property names, in order, and no actions in this version.</summary>
    [Fact]
    public void Workload_detail_serializes_to_v1_names()
    {
        var detail = new WorkloadDetail(
            At,
            "reviewer",
            "Fixture Reviewer",
            "local",
            "initialized",
            Section.Available(new PauseView("operator:budget", "Spend reached the limit.", At)),
            Section.Available(new BudgetView("2026-11", 4.5m, 10m, 5.5m)),
            []);

        var json = JsonSerializer.Serialize(detail, OperationsJson.Options);

        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","id":"reviewer","displayName":"Fixture Reviewer","environment":"local","phase":"initialized","pause":{"state":"available","value":{"pausedBy":"operator:budget","reason":"Spend reached the limit.","since":"2026-11-01T09:00:00Z"}},"budget":{"state":"available","value":{"window":"2026-11","spent":4.5,"limit":10,"remaining":5.5}},"supportedActions":[]}""",
            json);
    }

    /// <summary>Times are written in UTC, whatever offset they were captured with.</summary>
    [Fact]
    public void Run_times_are_utc_iso8601()
    {
        var started = new DateTimeOffset(2026, 11, 1, 11, 0, 0, 500, TimeSpan.FromHours(2));

        var json = JsonSerializer.Serialize(new RunSummary(RunId, "failed", started, started.AddMinutes(1)), OperationsJson.Options);
        var read = JsonSerializer.Deserialize<RunSummary>(json, OperationsJson.Options);

        Assert.Contains("\"startedAt\":\"2026-11-01T09:00:00.5Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"completedAt\":\"2026-11-01T09:01:00.5Z\"", json, StringComparison.Ordinal);
        Assert.Equal(started, read!.StartedAt);
    }

    /// <summary>A budget with no limit reports a zero limit and no remaining amount, never the guard's sentinel.</summary>
    [Fact]
    public void Unlimited_budget_has_null_remaining()
    {
        var unlimited = Projection.Budget(new CostStatus(3m, 0m, decimal.MaxValue, "2026-11"));
        var limited = Projection.Budget(new CostStatus(3m, 10m, 7m, "2026-11"));

        Assert.Equal(new BudgetView("2026-11", 3m, 0m, null), unlimited);
        Assert.Equal(new BudgetView("2026-11", 3m, 10m, 7m), limited);
        Assert.Equal("""{"window":"2026-11","spent":3,"limit":0,"remaining":null}""", JsonSerializer.Serialize(unlimited, OperationsJson.Options));
    }

    /// <summary>An unavailable section says so and carries no value; an available one may be empty.</summary>
    [Fact]
    public void Unavailable_section_has_no_value()
    {
        var unavailable = Section.Unavailable<BudgetView>();
        var notPaused = Section.Available<PauseView>(null);
        var approvals = new ApprovalsResponse(At, SectionState.Unavailable, []);

        Assert.Null(unavailable.Value);
        Assert.Equal("""{"state":"unavailable","value":null}""", JsonSerializer.Serialize(unavailable, OperationsJson.Options));
        Assert.Equal("""{"state":"available","value":null}""", JsonSerializer.Serialize(notPaused, OperationsJson.Options));
        Assert.Equal(
            """{"schemaVersion":1,"generatedAt":"2026-11-01T09:00:00Z","state":"unavailable","approvals":[]}""",
            JsonSerializer.Serialize(approvals, OperationsJson.Options));
    }

    /// <summary>The approval view has no member that could carry an approval's context.</summary>
    [Fact]
    public void Approval_view_has_no_context_property()
    {
        var names = typeof(PendingApprovalView).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["ApprovalId", "WorkloadId", "Action", "RequestedAt", "ExpiresAt"], names);
        Assert.DoesNotContain(names, name => name is "Context" or "Arguments" or "Payload");
    }

    /// <summary>An approval is projected only when its subject is <c>agent:</c> followed by a valid agent id.</summary>
    /// <param name="subject">The approval's subject.</param>
    /// <param name="workloadId">The projected workload id, or null when the approval is left out.</param>
    [Theory]
    [InlineData("agent:reviewer", "reviewer")]
    [InlineData("agent:a.b_c-1", "a.b_c-1")]
    [InlineData("reviewer", null)]
    [InlineData("user:reviewer", null)]
    [InlineData("agent:", null)]
    [InlineData("agent:Not An Id", null)]
    [InlineData("AGENT:reviewer", null)]
    public void Approval_subject_must_be_an_agent(string subject, string? workloadId)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal) { ["arguments"] = "private" };
        var pending = new PendingApproval(new ApprovalRequest("approval-1", subject, "tool.publish", context, "tool:publish@1", At.AddHours(1), "correlation-1"), At);

        var view = Projection.Approval(pending);

        Assert.Equal(workloadId is null ? null : new PendingApprovalView("approval-1", workloadId, "tool.publish", At, At.AddHours(1)), view);
    }

    /// <summary>Each schema parses, and it names and requires exactly the properties its records serialize.</summary>
    /// <param name="file">The schema file name.</param>
    /// <param name="record">The response record.</param>
    /// <param name="definitions">The names under <c>$defs</c>.</param>
    /// <param name="definitionRecords">The record each definition describes.</param>
    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schemas_are_valid_json_and_name_every_property(string file, Type record, string[] definitions, Type[] definitionRecords)
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.ContractDirectory, file)));
        var root = schema.RootElement;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        AssertDescribes(root, record);
        var defined = root.TryGetProperty("$defs", out var defs) ? defs.EnumerateObject().Select(definition => definition.Name).ToArray() : [];
        Assert.Equal(definitions.Order(StringComparer.Ordinal), defined.Order(StringComparer.Ordinal));
        for (var index = 0; index < definitions.Length; index++)
        {
            AssertDescribes(defs.GetProperty(definitions[index]), definitionRecords[index]);
        }
    }

    private static void AssertDescribes(JsonElement schema, Type record)
    {
        var serialized = OperationsJson.Options.GetTypeInfo(record).Properties.Select(property => property.Name).ToArray();

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(serialized, schema.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal(serialized, schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
    }
}
