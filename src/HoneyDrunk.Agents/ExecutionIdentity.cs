using HoneyDrunk.Agents.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents;

/// <summary>Who one execution acts as, and what it may use.</summary>
/// <param name="AgentId">The agent id.</param>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="CorrelationId">The grid correlation id.</param>
/// <param name="Grants">The effective grants.</param>
/// <param name="Bindings">The tool bindings.</param>
/// <param name="Tenant">The checked grid tenant the execution runs in.</param>
internal sealed record ExecutionIdentity(string AgentId, string ExecutionId, string CorrelationId, AgentGrants Grants, IReadOnlyList<AgentToolBinding> Bindings, TenantId Tenant)
{
    /// <summary>Gets the caller subject used with the Capabilities and Memory nodes.</summary>
    public string Subject => "agent:" + this.AgentId;

    /// <summary>Gets the Operator breaker name for the agent.</summary>
    public string BreakerName => "agent:" + this.AgentId;
}
