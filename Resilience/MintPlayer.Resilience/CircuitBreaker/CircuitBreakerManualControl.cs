namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>
/// Isolates (holds open) and closes circuit breakers manually. Attach it through
/// <see cref="CircuitBreakerStrategyOptions{TResult}.ManualControl"/>; one control may drive several breakers.
/// </summary>
public sealed class CircuitBreakerManualControl
{
    private readonly Lock _lock = new();
    private readonly HashSet<Func<ResilienceContext, Task>> _onIsolate = [];
    private readonly HashSet<Func<ResilienceContext, Task>> _onReset = [];
    private bool _isolated;

    /// <summary>Initializes a control whose breakers start closed.</summary>
    public CircuitBreakerManualControl()
    {
    }

    /// <summary>Initializes a control; when <paramref name="isIsolated"/> is true, every breaker attached to it starts isolated.</summary>
    /// <param name="isIsolated">Whether attached breakers start isolated.</param>
    public CircuitBreakerManualControl(bool isIsolated) => _isolated = isIsolated;

    internal bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _onIsolate.Count == 0;
            }
        }
    }

    /// <summary>Isolates every attached breaker: it rejects every call with <see cref="IsolatedCircuitException"/> until <see cref="CloseAsync"/>.</summary>
    /// <param name="cancellationToken">The cancellation token passed to the context of the events.</param>
    /// <returns>A task that completes once every breaker is isolated and its <c>OnOpened</c> event has run.</returns>
    public Task IsolateAsync(CancellationToken cancellationToken = default)
    {
        Func<ResilienceContext, Task>[] callbacks;
        lock (_lock)
        {
            callbacks = [.. _onIsolate];
            _isolated = true;
        }

        return InvokeAsync(callbacks, cancellationToken);
    }

    /// <summary>Closes every attached breaker and clears its health window.</summary>
    /// <param name="cancellationToken">The cancellation token passed to the context of the events.</param>
    /// <returns>A task that completes once every breaker is closed and its <c>OnClosed</c> event has run.</returns>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        Func<ResilienceContext, Task>[] callbacks;
        lock (_lock)
        {
            callbacks = [.. _onReset];
            _isolated = false;
        }

        return InvokeAsync(callbacks, cancellationToken);
    }

    /// <summary>Attaches a breaker; isolates it right away when the control is isolated. Disposing the result detaches it.</summary>
    internal IDisposable Initialize(Func<ResilienceContext, Task> onIsolate, Func<ResilienceContext, Task> onReset)
    {
        bool isolated;
        lock (_lock)
        {
            _onIsolate.Add(onIsolate);
            _onReset.Add(onReset);
            isolated = _isolated;
        }

        if (isolated)
        {
            var context = ResilienceContextPool.Shared.Get();
            context.IsSynchronous = true;
            try
            {
                onIsolate(context).GetAwaiter().GetResult();
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }

        return new Registration(this, onIsolate, onReset);
    }

    private static async Task InvokeAsync(Func<ResilienceContext, Task>[] callbacks, CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            foreach (var callback in callbacks)
            {
                await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
            }
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private void Remove(Func<ResilienceContext, Task> onIsolate, Func<ResilienceContext, Task> onReset)
    {
        lock (_lock)
        {
            _onIsolate.Remove(onIsolate);
            _onReset.Remove(onReset);
        }
    }

    private sealed class Registration(CircuitBreakerManualControl owner, Func<ResilienceContext, Task> onIsolate, Func<ResilienceContext, Task> onReset) : IDisposable
    {
        public void Dispose() => owner.Remove(onIsolate, onReset);
    }
}
