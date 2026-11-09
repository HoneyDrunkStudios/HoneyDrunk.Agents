using HoneyDrunk.Agents.Operations.LocalHost;
using HoneyDrunk.Agents.Operations.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>The deterministic local host and the checked-in fixtures it produces.</summary>
public sealed class FixtureTests
{
    private const string Token = "fixture-tests-only-token";
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Each fixture file with the route that produces it. "{run}" stands for the reviewer's newest finished run.
    private static readonly (string file, string path)[] Routes =
    [
        ("workloads.json", "/v1/workloads"),
        ("workload-reviewer.json", "/v1/workloads/reviewer"),
        ("workload-summarizer.json", "/v1/workloads/summarizer"),
        ("runs-reviewer.json", "/v1/workloads/reviewer/runs"),
        ("run-reviewer.json", "/v1/workloads/reviewer/runs/{run}"),
        ("approvals.json", "/v1/approvals"),
        ("audit-reviewer.json", "/v1/workloads/reviewer/audit"),
    ];

    private static string FixtureDirectory => Path.Combine(RepoFiles.ContractDirectory, "fixtures");

    /// <summary>
    /// Every fixture equals what the local host serves for a clock fixed at 2026-11-01T09:00:00Z, with run ids
    /// replaced by placeholders. Set <c>UPDATE_FIXTURES=1</c> to write the files instead of comparing them.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Fixtures_match_local_host_output()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var placeholders = await PlaceholdersAsync(app.Services);
        var newestReviewerRun = (await app.Services.GetRequiredService<IAgentRunJournal>().RecentAsync("reviewer", 1, null, finishedOnly: true, CancellationToken.None))[0].RunId;
        var produced = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, path) in Routes)
        {
            using var response = await client.GetAsync(new Uri(path.Replace("{run}", newestReviewerRun, StringComparison.Ordinal), UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {body}");
            produced[file] = Normalize(body, placeholders);
        }

        using var anonymous = app.GetTestClient();
        using var refused = await anonymous.GetAsync(new Uri("/v1/workloads", UriKind.Relative));
        produced["unauthorized.txt"] = ((int)refused.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";

        if (Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(FixtureDirectory);
            foreach (var (file, content) in produced)
            {
                await File.WriteAllTextAsync(Path.Combine(FixtureDirectory, file), content);
            }
        }

        Assert.All(produced, fixture => Assert.Equal(ReadFixture(fixture.Key), fixture.Value));
        Assert.Equal(produced.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(FixtureDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>The seeded workload covers what a consumer has to render: five runs in five states, an approval, a pause and a budget.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Local_host_seeds_every_state()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var workloads = await GetAsync(client, "/v1/workloads");
        var reviewer = await GetAsync(client, "/v1/workloads/reviewer");
        var summarizer = await GetAsync(client, "/v1/workloads/summarizer");
        var approvals = await GetAsync(client, "/v1/approvals");
        var audit = await GetAsync(client, "/v1/workloads/reviewer/audit");

        ConsumerRules.AssertWorkloadsAccepted(workloads);
        var listed = workloads.GetProperty("workloads").EnumerateArray().ToArray();
        Assert.Equal(["Fixture Reviewer", "Fixture Summarizer"], listed.Select(workload => workload.GetProperty("displayName").GetString()));
        Assert.All(listed, workload => Assert.Equal("local", workload.GetProperty("environment").GetString()));
        var statuses = listed.SelectMany(workload => workload.GetProperty("runs").EnumerateArray()).Select(run => run.GetProperty("status").GetString());
        Assert.Equal(["awaitingapproval", "completed", "failed", "halted", "rejected"], statuses.Order(StringComparer.Ordinal));
        Assert.Equal(10m, reviewer.GetProperty("budget").GetProperty("value").GetProperty("limit").GetDecimal());
        Assert.Equal(4.25m, reviewer.GetProperty("budget").GetProperty("value").GetProperty("spent").GetDecimal());
        Assert.Equal(JsonValueKind.Null, reviewer.GetProperty("pause").GetProperty("value").ValueKind);
        Assert.Equal("fixture-operator", summarizer.GetProperty("pause").GetProperty("value").GetProperty("pausedBy").GetString());
        Assert.Equal("reviewer", Assert.Single(approvals.GetProperty("approvals").EnumerateArray()).GetProperty("workloadId").GetString());
        Assert.Equal("available", audit.GetProperty("state").GetString());
        Assert.NotEqual(0, audit.GetProperty("entries").GetArrayLength());
    }

    /// <summary>The checked-in workloads fixture passes every rule the contract's first consumer applies.</summary>
    [Fact]
    public void Workloads_fixture_passes_admin_rules()
    {
        using var fixture = JsonDocument.Parse(ReadFixture("workloads.json"));

        ConsumerRules.AssertWorkloadsAccepted(fixture.RootElement);
        Assert.NotEqual(0, fixture.RootElement.GetProperty("workloads").GetArrayLength());
    }

    /// <summary>No fixture holds anything outside the safe projection: every string in it is one the seed chose to show.</summary>
    [Fact]
    public void Fixtures_hold_no_private_data()
    {
        var text = string.Concat(Routes.Select(route => ReadFixture(route.file)));

        Assert.DoesNotContain(LocalWorkload.PrivateMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The local host accepts only its configured bearer token.</summary>
    /// <param name="scheme">The Authorization scheme, or null for no header.</param>
    /// <param name="value">The Authorization parameter.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(null, null)]
    [InlineData("Bearer", "wrong-token-wrong-token")]
    [InlineData("Bearer", "fixture-tests-only-toke")]
    [InlineData("Basic", Token)]
    public async Task Local_host_rejects_other_credentials(string? scheme, string? value)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        if (scheme is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, value);
        }

        using var response = await client.GetAsync(new Uri("/v1/workloads", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", ReadFixture("unauthorized.txt").Trim());
    }

    /// <summary>The local host does not start outside the Development environment, or without a real token.</summary>
    /// <param name="environment">The environment name.</param>
    /// <param name="token">The configured token.</param>
    [Theory]
    [InlineData("Production", Token)]
    [InlineData("Staging", Token)]
    [InlineData("Development", "")]
    [InlineData("Development", "short")]
    public void Local_host_refuses_to_start_outside_development(string environment, string token)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Program.Build([$"--environment={environment}", $"--LocalHost:Token={token}"], new LocalClock()));

        Assert.Contains("Development", failure.Message, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var clock = new LocalClock();
        clock.Freeze(Start);
        var app = Program.Build(["--environment=Development", $"--LocalHost:Token={Token}"], clock, builder => builder.WebHost.UseTestServer());
        try
        {
            await LocalWorkload.SeedAsync(app.Services, clock);
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    // Run ids carry random bits, so each is replaced by its place in start order: a stable, valid run id.
    private static async Task<IReadOnlyDictionary<string, string>> PlaceholdersAsync(IServiceProvider services)
    {
        var journal = services.GetRequiredService<IAgentRunJournal>();
        var runs = new List<AgentRunRecord>();
        foreach (var agent in services.GetRequiredService<IAgentDirectory>().List())
        {
            runs.AddRange(await journal.RecentAsync(agent.AgentId, 100, null, finishedOnly: false, CancellationToken.None));
        }

        return runs
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.AgentId, StringComparer.Ordinal)
            .Select((run, index) => (id: run.RunId, placeholder: (index + 1).ToString("x32", System.Globalization.CultureInfo.InvariantCulture)))
            .ToDictionary(pair => pair.id, pair => pair.placeholder, StringComparer.Ordinal);
    }

    private static string Normalize(string body, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var (runId, placeholder) in placeholders)
        {
            body = body.Replace(runId, placeholder, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(body);
        return JsonSerializer.Serialize(document.RootElement, Indented).ReplaceLineEndings("\n") + "\n";
    }

    private static string ReadFixture(string file) => File.ReadAllText(Path.Combine(FixtureDirectory, file)).ReplaceLineEndings("\n");
}
