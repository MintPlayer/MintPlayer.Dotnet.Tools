namespace MintPlayer.Resilience;

/// <summary>
/// Base class of the hook attributes: marks a method of a <see cref="ResiliencePipelineAttribute"/> class as
/// a hook of a strategy. The hook binds to the only strategy of its kind in the pipeline; when there are
/// several, pass the <see cref="ResilienceStrategyAttribute.Name"/> of the one it belongs to. A hook named on
/// the strategy attribute itself (<c>[Retry(OnRetry = nameof(Log))]</c>) takes precedence.
/// </summary>
/// <remarks>Signatures are those documented on the matching strategy attribute property.</remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public abstract class ResilienceHookAttribute : Attribute
{
    private protected ResilienceHookAttribute(string? strategy) => Strategy = strategy;

    /// <summary>Gets the name of the strategy this hook belongs to, or <see langword="null"/> for the only strategy of its kind.</summary>
    public string? Strategy { get; }
}

/// <summary>The retry predicate (<see cref="RetryAttribute.ShouldHandle"/>).</summary>
/// <param name="strategy">The name of the retry strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RetryWhenAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The retry event (<see cref="RetryAttribute.OnRetry"/>).</summary>
/// <param name="strategy">The name of the retry strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnRetryAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The retry delay generator (<see cref="RetryAttribute.DelayGenerator"/>).</summary>
/// <param name="strategy">The name of the retry strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DelayGeneratorAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The timeout generator (<see cref="TimeoutAttribute.TimeoutGenerator"/>).</summary>
/// <param name="strategy">The name of the timeout strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TimeoutGeneratorAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The timeout event (<see cref="TimeoutAttribute.OnTimeout"/>).</summary>
/// <param name="strategy">The name of the timeout strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnTimeoutAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The circuit-breaker predicate (<see cref="CircuitBreakerAttribute.ShouldHandle"/>).</summary>
/// <param name="strategy">The name of the circuit breaker, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BreakWhenAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The circuit-opened event (<see cref="CircuitBreakerAttribute.OnOpened"/>).</summary>
/// <param name="strategy">The name of the circuit breaker, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnOpenedAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The circuit-closed event (<see cref="CircuitBreakerAttribute.OnClosed"/>).</summary>
/// <param name="strategy">The name of the circuit breaker, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnClosedAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The circuit-half-opened event (<see cref="CircuitBreakerAttribute.OnHalfOpened"/>).</summary>
/// <param name="strategy">The name of the circuit breaker, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnHalfOpenedAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The break-duration generator (<see cref="CircuitBreakerAttribute.BreakDurationGenerator"/>).</summary>
/// <param name="strategy">The name of the circuit breaker, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BreakDurationGeneratorAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The fallback predicate (<see cref="FallbackAttribute.ShouldHandle"/>).</summary>
/// <param name="strategy">The name of the fallback strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class FallbackWhenAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The fallback action (<see cref="FallbackAttribute.FallbackAction"/>).</summary>
/// <param name="strategy">The name of the fallback strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class FallbackWithAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The fallback event (<see cref="FallbackAttribute.OnFallback"/>).</summary>
/// <param name="strategy">The name of the fallback strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnFallbackAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The hedging predicate (<see cref="HedgingAttribute.ShouldHandle"/>).</summary>
/// <param name="strategy">The name of the hedging strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HedgeWhenAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);

/// <summary>The hedging event (<see cref="HedgingAttribute.OnHedging"/>).</summary>
/// <param name="strategy">The name of the hedging strategy, when the pipeline has several.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnHedgingAttribute(string? strategy = null) : ResilienceHookAttribute(strategy);
