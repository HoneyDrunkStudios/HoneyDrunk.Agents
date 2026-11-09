using HoneyDrunk.Agents.Operations.Contract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HoneyDrunk.Agents.Operations.Endpoints;

/// <summary>Shared pieces of the endpoint handlers: the response writer and the handling of an optional source.</summary>
internal static partial class Sources
{
    /// <summary>The log category of the endpoints.</summary>
    public const string LogCategory = "HoneyDrunk.Agents.Operations";

    /// <summary>Writes a response with the contract's JSON settings, whatever the host configured.</summary>
    /// <typeparam name="T">The response record.</typeparam>
    /// <param name="response">The response.</param>
    /// <returns>The result.</returns>
    public static IResult Json<T>(T response) => Results.Json(response, OperationsJson.Options);

    /// <summary>Whether an exception means the source failed, rather than that the request was canceled.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>True for a source failure.</returns>
    public static bool IsFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OutOfMemoryException && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);

    /// <summary>Reads one section from a source that may be missing or may fail. Only the section becomes unavailable.</summary>
    /// <typeparam name="TSource">The source.</typeparam>
    /// <typeparam name="T">The section's value.</typeparam>
    /// <param name="source">The source, or null when the host has not registered one.</param>
    /// <param name="name">The section's name, for the log.</param>
    /// <param name="read">Reads the value.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The section.</returns>
    public static async Task<Section<T>> ReadAsync<TSource, T>(TSource? source, string name, Func<TSource, Task<T?>> read, ILogger logger, CancellationToken cancellationToken)
        where TSource : class
        where T : class
    {
        if (source is null)
        {
            return Section.Unavailable<T>();
        }

        try
        {
            return Section.Available(await read(source).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsFailure(exception, cancellationToken))
        {
            // The message may carry private data; only the type name is recorded.
            SourceFailed(logger, name, exception.GetType().Name);
            return Section.Unavailable<T>();
        }
    }

    /// <summary>Logs a failed source by exception type name.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="section">The section's name.</param>
    /// <param name="exceptionType">The exception's type name.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Operations section {Section} is unavailable: its source failed with {ExceptionType}")]
    public static partial void SourceFailed(ILogger logger, string section, string exceptionType);
}
