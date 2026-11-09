using HoneyDrunk.Agents.Operations.Endpoints;
using HoneyDrunk.Agents.Operations.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Agents.Operations;

/// <summary>Maps the operations endpoints.</summary>
public static class AgentOperationsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the version 1 read endpoints under <c>/v1</c> (ADR-0110). Every route requires the
    /// <see cref="AgentOperationsPolicies.Read"/> policy, which the host must define.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The <c>/v1</c> route group, so the host can add its own conventions.</returns>
    /// <exception cref="InvalidOperationException">
    /// The host has not registered the agent runtime and the operations services, or has not defined the read policy.
    /// </exception>
    /// <exception cref="OptionsValidationException">The operations options are not valid.</exception>
    public static RouteGroupBuilder MapAgentOperations(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var services = endpoints.ServiceProvider;
        var registered = services.GetService<IServiceProviderIsService>();
        if (registered is null || !registered.IsService(typeof(IAgentRunJournal)) || !registered.IsService(typeof(IAgentDirectory)))
        {
            throw new InvalidOperationException("The operations endpoints need the agent runtime and the operations services: call AddHoneyDrunkAgents and AddAgentOperations before MapAgentOperations.");
        }

        // Reading the value runs the options validation now, at startup, not on the first request.
        _ = services.GetRequiredService<IOptions<AgentOperationsOptions>>().Value;
        if (services.GetService<IOptions<AuthorizationOptions>>()?.Value.GetPolicy(AgentOperationsPolicies.Read) is null)
        {
            throw new InvalidOperationException($"The operations endpoints require the authorization policy '{AgentOperationsPolicies.Read}'. Define it with AddAuthorization before MapAgentOperations.");
        }

        var group = endpoints.MapGroup("/v1").RequireAuthorization(AgentOperationsPolicies.Read);
        WorkloadEndpoints.Map(group);
        ApprovalEndpoints.Map(group);
        AuditEndpoints.Map(group);
        return group;
    }
}
