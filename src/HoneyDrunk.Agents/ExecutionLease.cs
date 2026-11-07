namespace HoneyDrunk.Agents;

internal sealed class ExecutionLease(CancellationToken executionToken)
{
    private readonly Lock _sync = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _ended;
    private int _calls;

    internal Call Link(CancellationToken callerToken)
    {
        lock (this._sync)
        {
            this.EnsureActive();
            var call = new Call(this, executionToken, callerToken);
            this._calls++;
            return call;
        }
    }

    internal void EnsureActive()
    {
        lock (this._sync)
        {
            ObjectDisposedException.ThrowIf(this._ended, this);
            executionToken.ThrowIfCancellationRequested();
        }
    }

    internal Task EndAsync()
    {
        lock (this._sync)
        {
            this._ended = true;
            if (this._calls == 0)
            {
                this._drained.TrySetResult();
            }

            return this._drained.Task;
        }
    }

    private void Release()
    {
        lock (this._sync)
        {
            this._calls--;
            if (this._ended && this._calls == 0)
            {
                this._drained.TrySetResult();
            }
        }
    }

    internal sealed class Call(ExecutionLease lease, CancellationToken executionToken, CancellationToken callerToken) : IDisposable
    {
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(executionToken, callerToken);
        private int _disposed;

        internal CancellationToken Token => this._source.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 0)
            {
                this._source.Dispose();
                lease.Release();
            }
        }
    }
}
