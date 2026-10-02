namespace MintPlayer.Resilience;

/// <summary>
/// Why a pipeline refused to run (or to finish) a call. Testing it costs nothing, and it is what
/// <see cref="Outcome{TResult}.Rejection"/> reports instead of an exception.
/// </summary>
public enum RejectionKind : byte
{
    /// <summary>The call was not rejected.</summary>
    None = 0,

    /// <summary>A circuit breaker is open. The throwing API raises a <see cref="CircuitBreaker.BrokenCircuitException"/>.</summary>
    CircuitOpen,

    /// <summary>A circuit breaker is manually isolated. The throwing API raises a <see cref="CircuitBreaker.IsolatedCircuitException"/>.</summary>
    CircuitIsolated,

    /// <summary>A rate or concurrency limiter refused a permit. The throwing API raises a <see cref="RateLimiting.RateLimiterRejectedException"/>.</summary>
    RateLimited,

    /// <summary>A timeout strategy cancelled the call. The throwing API raises a <see cref="Timeout.TimeoutRejectedException"/>.</summary>
    Timeout,
}
