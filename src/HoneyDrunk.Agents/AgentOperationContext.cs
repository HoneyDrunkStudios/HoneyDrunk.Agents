using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;

namespace HoneyDrunk.Agents;

/// <summary>
/// One execution's operation context, built over the grid context the lifecycle already checked. Passing it to
/// Kernel's agent context factory keeps Kernel from building one over the execution scope's own, uninitialized grid
/// context. The lifecycle completes or fails it and disposes it.
/// </summary>
internal sealed class AgentOperationContext : IOperationContext
{
    private readonly Dictionary<string, object?> metadata = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AgentOperationContext"/> class.</summary>
    /// <param name="gridContext">The checked grid context.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="operationId">The operation id; the execution id.</param>
    /// <param name="timeProvider">The clock.</param>
    public AgentOperationContext(IGridContext gridContext, string operationName, string operationId, TimeProvider timeProvider)
    {
        this.GridContext = gridContext ?? throw new ArgumentNullException(nameof(gridContext));
        this.OperationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        this.OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.StartedAtUtc = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public IGridContext GridContext { get; }

    /// <inheritdoc />
    public string OperationName { get; }

    /// <inheritdoc />
    public string OperationId { get; }

    /// <inheritdoc />
    public string CorrelationId => this.GridContext.CorrelationId;

    /// <inheritdoc />
    public string? CausationId => this.GridContext.CausationId;

    /// <inheritdoc />
    public TenantId TenantId => this.GridContext.TenantId;

    /// <inheritdoc />
    public string? ProjectId => this.GridContext.ProjectId;

    /// <inheritdoc />
    public DateTimeOffset StartedAtUtc { get; }

    /// <inheritdoc />
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <inheritdoc />
    public bool? IsSuccess { get; private set; }

    /// <inheritdoc />
    public string? ErrorMessage { get; private set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Metadata => this.metadata;

    /// <inheritdoc />
    public void Complete() => this.End(true, null);

    /// <inheritdoc />
    public void Fail(string errorMessage, Exception? exception = null) => this.End(false, errorMessage);

    /// <inheritdoc />
    public void AddMetadata(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.metadata[key] = value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.IsSuccess is null)
        {
            this.End(false, "abandoned");
        }
    }

    private void End(bool success, string? errorMessage)
    {
        if (this.IsSuccess is not null)
        {
            return;
        }

        this.IsSuccess = success;
        this.ErrorMessage = errorMessage;
        this.CompletedAtUtc = this.timeProvider.GetUtcNow();
    }
}
