using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Agents.Operations.Runs;
using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Operator.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HoneyDrunk.Agents.Operations.Tests;

/// <summary>
/// An in-process host on <see cref="TestServer"/>: the real agent runtime over test pieces, in-memory Operator
/// state, a test authentication scheme and the operations endpoints.
/// </summary>
internal sealed class OperationsHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly AgentTestHarness pieces;

    private OperationsHost(WebApplication app, AgentTestHarness pieces, FakeTimeProvider time, CapturedLogs logs)
    {
        this.app = app;
        this.pieces = pieces;
        this.Time = time;
        this.Logs = logs;
        this.Client = app.GetTestClient();
        this.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        this.Anonymous = app.GetTestClient();
    }

    public static DateTimeOffset Start { get; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; }

    public CapturedLogs Logs { get; }

    public HttpClient Client { get; }

    public HttpClient Anonymous { get; }

    public IServiceProvider Services => this.app.Services;

    public IAgentLifecycle Lifecycle => this.Services.GetRequiredService<IAgentLifecycle>();

    public IAgentRunJournal Journal => this.Services.GetRequiredService<IAgentRunJournal>();

    public InMemoryApprovalGate Gate => this.pieces.Gate;

    public InMemoryAgentPauses Pauses => this.Services.GetRequiredService<InMemoryAgentPauses>();

    public InMemoryCostGuard CostGuard => this.Services.GetRequiredService<InMemoryCostGuard>();

    /// <summary>Builds and starts a host.</summary>
    /// <param name="services">Registers or replaces services before the defaults are added.</param>
    /// <param name="environment">The operations environment identifier.</param>
    /// <param name="registerPolicy">Whether the host defines the read policy.</param>
    /// <param name="withOperator">Whether the host has an approval desk, a pause store and a cost guard.</param>
    /// <returns>The started host.</returns>
    public static async Task<OperationsHost> StartAsync(
        Action<IServiceCollection>? services = null,
        string? environment = "test",
        bool registerPolicy = true,
        bool withOperator = true)
    {
        var time = new FakeTimeProvider(Start);
        var pieces = new AgentTestHarness(timeProvider: time);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        var logs = new CapturedLogs();
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
        services?.Invoke(builder.Services);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<IGridContextAccessor>(pieces.GridAccessor);
        builder.Services.AddSingleton<IAgentExecutionContextFactory>(pieces.ContextFactory);
        builder.Services.AddSingleton<ITelemetryActivityFactory>(new NullTelemetryActivityFactory());
        builder.Services.AddSingleton(pieces.Capabilities.Registry);
        builder.Services.AddSingleton(pieces.Capabilities.Invoker);
        builder.Services.AddSingleton<IMemoryScopeResolver>(pieces.MemoryResolver);
        builder.Services.AddSingleton<IMemoryStore>(pieces.MemoryStore);
        builder.Services.AddSingleton<ICircuitBreaker>(pieces.Breaker);
        builder.Services.AddSingleton<IDecisionPolicy>(pieces.Policy);
        builder.Services.AddSingleton<IApprovalGate>(pieces.Gate);
        if (withOperator)
        {
            builder.Services.AddSingleton(new InMemoryAgentPauses(time));
            builder.Services.AddSingleton(new InMemoryCostGuard());
            builder.Services.TryAddSingleton<IApprovalDesk>(new InMemoryApprovalDesk(pieces.Gate));
            builder.Services.TryAddSingleton<IAgentPauses>(provider => provider.GetRequiredService<InMemoryAgentPauses>());
            builder.Services.TryAddSingleton<ICostGuard>(provider => provider.GetRequiredService<InMemoryCostGuard>());
        }

        builder.Services.AddHoneyDrunkAgents();
        builder.Services.AddAgentOperations(options => options.Environment = environment!);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization(options =>
        {
            if (registerPolicy)
            {
                options.AddPolicy(AgentOperationsPolicies.Read, policy => policy.RequireAuthenticatedUser());
            }
        });

        var app = builder.Build();
        try
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAgentOperations();
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            pieces.Dispose();
            throw;
        }

        return new OperationsHost(app, pieces, time, logs);
    }

    /// <summary>Registers and initializes an agent.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="name">The descriptor's name; the id by default.</param>
    /// <param name="run">What each execution does; it completes by default.</param>
    /// <returns>A task.</returns>
    public async Task AddAgentAsync(string agentId, string? name = null, Func<IAgentRunContext, AgentRequest, Task<AgentResult>>? run = null)
    {
        this.Lifecycle.Register(new AgentRegistration(new NamedAgent(agentId, name ?? agentId, run), AgentGrants.None, []));
        await this.Lifecycle.InitializeAsync(agentId);
    }

    /// <summary>Runs an agent once through the real lifecycle.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="payload">The request payload, as JSON.</param>
    /// <returns>The result.</returns>
    public Task<AgentResult> ExecuteAsync(string agentId, string payload = "{}")
    {
        using var document = JsonDocument.Parse(payload);
        return this.Lifecycle.ExecuteAsync(agentId, new AgentRequest("test", document.RootElement.Clone()), this.pieces.ExecutionOptions());
    }

    /// <summary>Journals a finished run that started the given number of minutes after <see cref="Start"/>.</summary>
    /// <param name="agentId">The agent id.</param>
    /// <param name="number">The run number; it sets the run id and the start time.</param>
    /// <param name="status">The status.</param>
    /// <returns>The run id.</returns>
    public async Task<string> FinishRunAsync(string agentId, int number, string status = "completed")
    {
        var started = Start.AddMinutes(number);
        await this.Journal.StartedAsync(agentId, RunId(number), started, CancellationToken.None);
        await this.Journal.CompletedAsync(agentId, RunId(number), started.AddSeconds(30), status, CancellationToken.None);
        return RunId(number);
    }

    /// <summary>Gets a path with the authenticated client and parses the body.</summary>
    /// <param name="path">The request path.</param>
    /// <returns>The parsed body.</returns>
    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await this.Client.GetAsync(new Uri(path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        Xunit.Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>A deterministic run id: 32 hexadecimal digits that parse as a non-empty GUID.</summary>
    /// <param name="number">The run number, above zero.</param>
    /// <returns>The run id.</returns>
    public static string RunId(int number) => number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        this.Anonymous.Dispose();
        await this.app.DisposeAsync();
        this.pieces.Dispose();
    }
}
