using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// Accepts a bearer token equal to the <c>LocalHost:Token</c> setting. It exists so the local host has something
/// to authorize; it is not a production scheme, and the host refuses to start outside the Development environment.
/// </summary>
/// <param name="options">The scheme options.</param>
/// <param name="logger">The logger factory.</param>
/// <param name="encoder">The URL encoder.</param>
/// <param name="configuration">The host's configuration.</param>
/// <param name="environment">The host's environment.</param>
public sealed class DevelopmentBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme's name.</summary>
    public const string SchemeName = "DevelopmentBearer";

    /// <summary>The configuration key of the token.</summary>
    public const string TokenSetting = "LocalHost:Token";

    private const string BearerPrefix = "Bearer ";
    private const int MinTokenLength = 16;

    /// <summary>Refuses to go on outside the Development environment, or without a token of at least 16 characters.</summary>
    /// <param name="environment">The host's environment.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="InvalidOperationException">The environment is not Development, or the token is missing or too short.</exception>
    public static void RequireDevelopment(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException($"The local operations host runs only in the Development environment; this one is '{environment.EnvironmentName}'.");
        }

        if ((configuration[TokenSetting]?.Length ?? 0) < MinTokenLength)
        {
            throw new InvalidOperationException($"The local operations host needs {TokenSetting} in the Development environment: at least {MinTokenLength} characters.");
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

        var expected = Encoding.UTF8.GetBytes(configuration[TokenSetting] ?? string.Empty);
        var presented = Encoding.UTF8.GetBytes(header[BearerPrefix.Length..]);
        if (!environment.IsDevelopment() || expected.Length < MinTokenLength || !CryptographicOperations.FixedTimeEquals(presented, expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer token is not the local host's token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "local-development")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
