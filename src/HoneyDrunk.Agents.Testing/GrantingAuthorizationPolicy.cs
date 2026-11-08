using HoneyDrunk.Auth.Abstractions;

namespace HoneyDrunk.Agents.Testing;

/// <summary>
/// For tests only: allows a request when the identity holds every required scope and, if roles are required, at
/// least one of them. Never register it in a production host.
/// </summary>
public sealed class GrantingAuthorizationPolicy : IAuthorizationPolicy
{
    /// <inheritdoc />
    public string PolicyName => "test-granting";

    /// <inheritdoc />
    public Task<AuthorizationDecision> EvaluateAsync(AuthenticatedIdentity? identity, AuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reasons = request.RequiredScopes
            .Where(scope => identity is null || !identity.HasClaim(AuthClaimTypes.Scope, scope))
            .Select(_ => new DenyReason(AuthorizationDenyCode.MissingScope, "missing scope"))
            .ToList();

        if (request.RequiredRoles.Count > 0 && (identity is null || !request.RequiredRoles.Any(role => identity.HasClaim(AuthClaimTypes.Role, role))))
        {
            reasons.Add(new DenyReason(AuthorizationDenyCode.MissingRole, "missing role"));
        }

        return Task.FromResult(reasons.Count == 0 ? AuthorizationDecision.Allow() : AuthorizationDecision.Deny(reasons));
    }
}
