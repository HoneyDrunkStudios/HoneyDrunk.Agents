using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents.Testing;

/// <summary>An operation context that records how it ended.</summary>
/// <param name="gridContext">The grid context.</param>
/// <param name="operationName">The operation name.</param>
public sealed class TestOperationContext(IGridContext gridContext, string operationName) : IOperationContext
{
    private readonly Dictionary<string, object?> metadata = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IGridContext GridContext { get; } = gridContext ?? throw new ArgumentNullException(nameof(gridContext));

    /// <inheritdoc />
    public string OperationName { get; } = operationName ?? throw new ArgumentNullException(nameof(operationName));

    /// <inheritdoc />
    public string OperationId { get; } = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public string CorrelationId => this.GridContext.CorrelationId;

    /// <inheritdoc />
    public string? CausationId => this.GridContext.CausationId;

    /// <inheritdoc />
    public TenantId TenantId => this.GridContext.TenantId;

    /// <inheritdoc />
    public string? ProjectId => this.GridContext.ProjectId;

    /// <inheritdoc />
    public DateTimeOffset StartedAtUtc => TestAgentExecutionContext.FixedStart;

    /// <inheritdoc />
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <inheritdoc />
    public bool? IsSuccess { get; private set; }

    /// <inheritdoc />
    public string? ErrorMessage { get; private set; }

    /// <summary>Gets a value indicating whether the context was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Metadata => this.metadata;

    /// <inheritdoc />
    public void Complete()
    {
        this.IsSuccess = true;
        this.CompletedAtUtc = TestAgentExecutionContext.FixedStart;
    }

    /// <inheritdoc />
    public void Fail(string errorMessage, Exception? exception = null)
    {
        this.IsSuccess = false;
        this.ErrorMessage = errorMessage;
        this.CompletedAtUtc = TestAgentExecutionContext.FixedStart;
    }

    /// <inheritdoc />
    public void AddMetadata(string key, object? value) => this.metadata[key] = value;

    /// <inheritdoc />
    public void Dispose() => this.Disposed = true;
}
