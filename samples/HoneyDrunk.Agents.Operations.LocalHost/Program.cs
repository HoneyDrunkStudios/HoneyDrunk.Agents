using HoneyDrunk.Agents.Testing;
using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Capabilities.Testing;
using HoneyDrunk.Kernel.Abstractions.Agents;
using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using HoneyDrunk.Memory;
using HoneyDrunk.Memory.Abstractions;
using HoneyDrunk.Memory.Providers.InMemory;
using HoneyDrunk.Operator;
using HoneyDrunk.Operator.Abstractions;
using HoneyDrunk.Vault.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// A deterministic local agent host. It composes the real agent runtime and Operator's in-memory runtime over test
/// pieces, seeds a fixed workload, and serves the operations contract (ADR-0110) so a consumer can be built against
/// it. It runs only in the Development environment.
/// </summary>
public static class Program
{
    /// <summary>The environment identifier every workload of this host reports.</summary>
    public const string EnvironmentId = "local";

    /// <summary>Starts the host: the workload is seeded one hour in the past, then the clock follows the system.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A task that completes when the host stops.</returns>
    public static async Task Main(string[] args)
    {
        var clock = new LocalClock();
        var app = Build(args, clock);
        await using (app.ConfigureAwait(false))
        {
            clock.Freeze(TimeProvider.System.GetUtcNow().AddHours(-1));
            await LocalWorkload.SeedAsync(app.Services, clock).ConfigureAwait(false);
            clock.Release();
            var token = app.Services.GetRequiredService<LocalHostToken>();
            if (token.Generated)
            {
                // Shown once on the local console and never logged; set LocalHost:Token to keep one between runs.
                await Console.Out.WriteLineAsync($"Local operations host token for this run: {token.Value}").ConfigureAwait(false);
            }

            await app.RunAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Builds the host without seeding or starting it.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="clock">The host's clock.</param>
    /// <param name="configure">Optionally configures the builder, for example to use a test server.</param>
    /// <returns>The application.</returns>
    /// <exception cref="InvalidOperationException">The environment is not Development, or the configured token is too short.</exception>
    public static WebApplication Build(string[] args, LocalClock clock, Action<WebApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        DevelopmentBearerHandler.RequireDevelopment(builder.Environment);

        var services = builder.Services;
        services.AddSingleton(LocalHostToken.From(builder.Configuration));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(clock);
        AddRuntimePieces(services);
        AddOperator(services);
        services.AddHoneyDrunkAgents();
        services.AddAgentOperations(options => options.Environment = EnvironmentId);
        services.AddAuthentication(DevelopmentBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentBearerHandler>(DevelopmentBearerHandler.SchemeName, null);
        services.AddAuthorization(options => options.AddPolicy(
            AgentOperationsPolicies.Read,
            policy => policy.AddAuthenticationSchemes(DevelopmentBearerHandler.SchemeName).RequireAuthenticatedUser()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAgentOperations();
        return app;
    }

    // The Kernel, Capabilities and Memory pieces the runtime needs, composed as the agent test harness composes them.
    private static void AddRuntimePieces(IServiceCollection services)
    {
        var capabilities = new CapabilityTestHarness();
        services.AddSingleton<IGridContextAccessor>(new TestGridContextAccessor(new TestGridContext(AgentTestHarness.Tenant)));
        services.AddSingleton<IAgentExecutionContextFactory>(new TestAgentExecutionContextFactory());
        services.AddSingleton<ITelemetryActivityFactory>(new NullTelemetryActivityFactory());
        services.AddSingleton(capabilities.Registry);
        services.AddSingleton(capabilities.Invoker);
        services.AddSingleton<IMemoryScopeResolver>(provider => new MemoryScopeResolver(
            new GrantingAuthorizationPolicy(),
            provider.GetRequiredService<IApprovalGate>(),
            Options.Create(new MemoryAuthorizationOptions()),
            provider.GetRequiredService<ITelemetryActivityFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MemoryScopeResolver>>()));
        services.AddSingleton<IMemoryStore>(provider => new ScopeCheckedMemoryStore(
            new InMemoryMemoryStore(provider.GetRequiredService<TimeProvider>()),
            nameof(InMemoryMemoryStore),
            provider.GetRequiredService<ITelemetryActivityFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ScopeCheckedMemoryStore>>()));
    }

    // Operator's in-memory runtime, with a fixed budget for the reviewer and an audit log that can be read back.
    private static void AddOperator(IServiceCollection services)
    {
        services.AddSingleton<IConfigProvider>(new DictionaryConfigProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"HoneyDrunk:Operator:Budget:{LocalWorkload.ReviewerId}"] = "10",
        }));
        services.AddSingleton<LocalAuditLog>();
        services.AddSingleton<IAuditLog>(provider => provider.GetRequiredService<LocalAuditLog>());
        services.AddSingleton<IAuditQuery>(provider => provider.GetRequiredService<LocalAuditLog>());
        services.AddHoneyDrunkOperator();
    }
}
