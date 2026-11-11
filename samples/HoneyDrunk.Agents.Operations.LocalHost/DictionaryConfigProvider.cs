using HoneyDrunk.Vault.Abstractions;
using System.Globalization;

namespace HoneyDrunk.Agents.Operations.LocalHost;

/// <summary>
/// A configuration provider over a dictionary. Operator reads its limits through Vault's provider, so the local
/// host gives it one with fixed values.
/// </summary>
/// <param name="values">The configuration values, by key.</param>
public sealed class DictionaryConfigProvider(IReadOnlyDictionary<string, string> values) : IConfigProvider
{
    private readonly IReadOnlyDictionary<string, string> values = values ?? throw new ArgumentNullException(nameof(values));

    /// <inheritdoc />
    public Task<string> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"No configuration value '{key}'."));

    /// <inheritdoc />
    public Task<T> GetValueAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.TryGetValue(key, out var value) ? Convert<T>(value) : throw new KeyNotFoundException($"No configuration value '{key}'."));

    /// <inheritdoc />
    public Task<T> GetValueAsync<T>(string path, T defaultValue, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.TryGetValue(path, out var value) ? Convert<T>(value) : defaultValue);

    /// <inheritdoc />
    public Task<string?> TryGetValueAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.TryGetValue(key, out var value) ? value : null);

    private static T Convert<T>(string value) => (T)System.Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
}
