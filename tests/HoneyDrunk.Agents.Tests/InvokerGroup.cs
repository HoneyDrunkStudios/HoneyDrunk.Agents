using HoneyDrunk.Agents.Abstractions;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Several executions' invokers over one fixture, disposed together.</summary>
internal sealed class InvokerGroup(InvokerFixture fixture, int count) : IDisposable
{
    private readonly CapabilityToolInvoker[] invokers = [.. Enumerable.Range(1, count).Select(index => fixture.Create("exec-" + index))];

    /// <summary>Runs one call on every invoker at once.</summary>
    /// <returns>Each invoker's result.</returns>
    public Task<ToolCallResult[]> InvokeAllAsync() =>
        Task.WhenAll(this.invokers.Select(invoker => Task.Run(() => invoker.InvokeAsync(InvokerFixture.Call()))));

    public void Dispose()
    {
        foreach (var invoker in this.invokers)
        {
            invoker.Dispose();
        }
    }
}
