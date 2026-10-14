namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>The state of a circuit breaker. The values are the same as Polly's.</summary>
public enum CircuitState
{
    /// <summary>The circuit is closed: calls flow, and their outcomes are measured.</summary>
    Closed = 0,

    /// <summary>The circuit is open: calls are rejected until the break duration has elapsed.</summary>
    Open = 1,

    /// <summary>The break has elapsed and one probe call is running; every other call is rejected until it finishes.</summary>
    HalfOpen = 2,

    /// <summary>The circuit is held open manually (<see cref="CircuitBreakerManualControl.IsolateAsync"/>) and rejects every call until closed.</summary>
    Isolated = 3,
}
