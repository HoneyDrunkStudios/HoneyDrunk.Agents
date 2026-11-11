using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// Accepts a bearer token equal to the host's <see cref="LocalHostToken"/>. It exists so the local host has
/// something to authorize; it is not a production scheme, and the host refuses to start outside the Development
/// environment.
/// </summary>
/// <param name="options">The scheme options.</param>
/// <param name="logger">The logger factory.</param>
/// <param name="encoder">The URL encoder.</param>
/// <param name="token">The token the host accepts.</param>
/// <param name="environment">The host's environment.</param>
public sealed class DevelopmentBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalHostToken token,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme's name.</summary>
    public const string SchemeName = "DevelopmentBearer";

    private const string BearerPrefix = "Bearer ";

    /// <summary>Refuses to go on outside the Development environment.</summary>
    /// <param name="environment">The host's environment.</param>
    /// <exception cref="InvalidOperationException">The environment is not Development.</exception>
    public static void RequireDevelopment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException($"The local operations host runs only in the Development environment; this one is '{environment.EnvironmentName}'.");
        }
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = this.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = Encoding.UTF8.GetBytes(header[BearerPrefix.Length..]);
        if (!environment.IsDevelopment() || !CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(token.Value)))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer token is not the local host's token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "local-development")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
