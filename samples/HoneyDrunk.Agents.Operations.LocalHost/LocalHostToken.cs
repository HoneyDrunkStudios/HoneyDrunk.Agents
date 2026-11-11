using System.Security.Cryptography;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// The one bearer token the local host accepts. It comes from the <c>LocalHost:Token</c> setting; with none
/// configured, a random token is made for the run, so no credential is ever checked in.
/// </summary>
public sealed class LocalHostToken
{
    /// <summary>The configuration key of the token.</summary>
    public const string Setting = "LocalHost:Token";

    private const int MinLength = 16;

    private LocalHostToken(string value, bool generated)
    {
        this.Value = value;
        this.Generated = generated;
    }

    /// <summary>Gets the token.</summary>
    public string Value { get; }

    /// <summary>Gets a value indicating whether the token was made for this run, not configured.</summary>
    public bool Generated { get; }

    /// <summary>Reads the configured token, or makes a random one when none is configured.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The token.</returns>
    /// <exception cref="InvalidOperationException">A token is configured but has fewer than 16 characters.</exception>
    public static LocalHostToken From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[Setting];
        if (string.IsNullOrEmpty(configured))
        {
            return new LocalHostToken(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)), generated: true);
        }

        return configured.Length >= MinLength
            ? new LocalHostToken(configured, generated: false)
            : throw new InvalidOperationException($"{Setting} is too short: use at least {MinLength} characters, or leave it unset to get a random token for the run.");
    }
}
