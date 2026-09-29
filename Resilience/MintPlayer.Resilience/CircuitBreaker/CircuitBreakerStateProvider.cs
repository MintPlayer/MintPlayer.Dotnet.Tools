namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>Reports the state of the circuit breaker it is attached to through <see cref="CircuitBreakerStrategyOptions{TResult}.StateProvider"/>.</summary>
public sealed class CircuitBreakerStateProvider
{
    private Func<CircuitState>? _circuitStateProvider;

    /// <summary>Gets the state of the circuit, or <see cref="CircuitState.Closed"/> before the pipeline is built.</summary>
    public CircuitState CircuitState => _circuitStateProvider?.Invoke() ?? CircuitState.Closed;

    internal bool IsInitialized => _circuitStateProvider is not null;

    internal void Initialize(Func<CircuitState> circuitStateProvider)
    {
        if (_circuitStateProvider is not null)
        {
            throw new InvalidOperationException($"This instance of '{nameof(CircuitBreakerStateProvider)}' is already initialized and cannot be used in a different circuit-breaker strategy.");
        }

        _circuitStateProvider = circuitStateProvider;
    }

    /// <summary>Detaches the breaker that <paramref name="circuitStateProvider"/> belongs to, if it is still the attached one.</summary>
    internal void Release(Func<CircuitState> circuitStateProvider)
        => Interlocked.CompareExchange(ref _circuitStateProvider, null, circuitStateProvider);
}
