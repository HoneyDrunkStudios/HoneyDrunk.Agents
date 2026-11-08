using HoneyDrunk.Auth.Abstractions;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Allows a request when the identity holds every required scope claim.</summary>
internal sealed class TestScopePolicy : IAuthorizationPolicy
{
    public string PolicyName => "test-scope";

    public Task<AuthorizationDecision> EvaluateAsync(AuthenticatedIdentity? identity, AuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var missing = request.RequiredScopes.Where(scope => identity is null || !identity.HasClaim(AuthClaimTypes.Scope, scope)).ToArray();
        return Task.FromResult(missing.Length == 0
            ? AuthorizationDecision.Allow()
            : AuthorizationDecision.Deny(missing.Select(scope => new DenyReason(AuthorizationDenyCode.MissingScope, "missing scope"))));
    }
}
