using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Tests;

/// <summary>An operation context that records how it ended.</summary>
internal sealed class TestOperationContext(IGridContext gridContext, string operationName) : IOperationContext
{
    private readonly Dictionary<string, object?> metadata = new(StringComparer.Ordinal);

    public IGridContext GridContext { get; } = gridContext;

    public string OperationName { get; } = operationName;

    public string OperationId { get; } = Guid.NewGuid().ToString("N");

    public string CorrelationId => this.GridContext.CorrelationId;

    public string? CausationId => null;

    public TenantId TenantId => this.GridContext.TenantId;

    public string? ProjectId => null;

    public DateTimeOffset StartedAtUtc => DateTimeOffset.UnixEpoch;

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool? IsSuccess { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool Disposed { get; private set; }

    public IReadOnlyDictionary<string, object?> Metadata => this.metadata;

    public void Complete()
    {
        this.IsSuccess = true;
        this.CompletedAtUtc = DateTimeOffset.UnixEpoch;
    }

    public void Fail(string errorMessage, Exception? exception = null)
    {
        this.IsSuccess = false;
        this.ErrorMessage = errorMessage;
        this.CompletedAtUtc = DateTimeOffset.UnixEpoch;
    }

    public void AddMetadata(string key, object? value) => this.metadata[key] = value;

    public void Dispose() => this.Disposed = true;
}
