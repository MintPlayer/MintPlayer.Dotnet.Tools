using MintPlayer.Resilience.RateLimiting;

namespace MintPlayer.Resilience;

// The strategy attributes of a [ResiliencePipeline] class. Rules shared by all of them:
//   - They run in declaration order, the first one outermost, and must all be on ONE declaration of the
//     class (plan S1: across partial declarations the order is undefined).
//   - Durations are integer milliseconds (…Ms), because a TimeSpan is not a valid attribute argument (S7);
//     -1 on a nullable duration means "not set".
//   - Defaults are Polly's, the same as the runtime options classes.
//   - A string property naming a hook or a member takes nameof(...) of a method, field or property of the
//     pipeline class. Hook methods are static in the static form and may be instance methods in the DI form.

/// <summary>Base class of the strategy attributes of a <see cref="ResiliencePipelineAttribute"/> class.</summary>
public abstract class ResilienceStrategyAttribute : Attribute
{
    private protected ResilienceStrategyAttribute()
    {
    }

    /// <summary>
    /// Gets or sets the name of the strategy, used by telemetry, as the configuration key of a reloadable
    /// pipeline, and by hook attributes that target this strategy when there are several of its kind.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>A timeout (<see cref="Timeout.TimeoutStrategyOptions"/>). Two timeouts are common: a total budget outside a retry and a per-attempt timeout inside it.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TimeoutAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the timeout in milliseconds. Default 30 000; valid 10 ms to 1 day.</summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>Gets or sets the hook that computes the timeout per execution: <c>TimeSpan M(TimeoutGeneratorArguments)</c> or <c>TimeSpan M()</c>.</summary>
    public string? TimeoutGenerator { get; set; }

    /// <summary>Gets or sets the event raised when the timeout fires: <c>ValueTask M(OnTimeoutArguments)</c> (or <c>void</c>).</summary>
    public string? OnTimeout { get; set; }
}

/// <summary>A retry (<see cref="Retry.RetryStrategyOptions{TResult}"/>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RetryAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the maximum number of retries. Default 3; <see cref="int.MaxValue"/> retries forever.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets how the delay grows. Default <see cref="DelayBackoffType.Constant"/>.</summary>
    public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Constant;

    /// <summary>Gets or sets the base delay in milliseconds. Default 2 000.</summary>
    public int DelayMs { get; set; } = 2_000;

    /// <summary>Gets or sets the cap on the computed delay in milliseconds; -1 (the default) means no cap.</summary>
    public int MaxDelayMs { get; set; } = -1;

    /// <summary>Gets or sets whether jitter is added. Default <see langword="false"/>.</summary>
    public bool UseJitter { get; set; }

    /// <summary>Gets or sets the predicate: <c>bool M(RetryPredicateArguments&lt;T&gt;)</c> or <c>bool M(Outcome&lt;T&gt;)</c>. Default: every exception except a cancellation, and every rejection.</summary>
    public string? ShouldHandle { get; set; }

    /// <summary>Gets or sets the delay generator: <c>TimeSpan? M(RetryDelayGeneratorArguments&lt;T&gt;)</c> (or returning <c>TimeSpan</c>).</summary>
    public string? DelayGenerator { get; set; }

    /// <summary>Gets or sets the event raised before each retry: <c>ValueTask M(OnRetryArguments&lt;T&gt;)</c> (or <c>void</c>).</summary>
    public string? OnRetry { get; set; }

    /// <summary>Gets or sets a field or property of type <see cref="Retry.RetryBudget"/> to share a budget with other pipelines.</summary>
    public string? Budget { get; set; }

    /// <summary>Gets or sets the event raised when the budget refuses a retry: <c>ValueTask M(OnRetryBudgetExhaustedArguments&lt;T&gt;)</c> (or <c>void</c>).</summary>
    public string? OnBudgetExhausted { get; set; }
}

/// <summary>A circuit breaker (<see cref="CircuitBreaker.CircuitBreakerStrategyOptions{TResult}"/>), including the slow-call ratio (beyond Polly).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CircuitBreakerAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the failure ratio that opens the circuit. Default 0.1.</summary>
    public double FailureRatio { get; set; } = 0.1;

    /// <summary>Gets or sets the minimum number of calls in the sampling window before the ratio counts. Default 100.</summary>
    public int MinimumThroughput { get; set; } = 100;

    /// <summary>Gets or sets the sampling window in milliseconds. Default 30 000.</summary>
    public int SamplingDurationMs { get; set; } = 30_000;

    /// <summary>Gets or sets how long the circuit stays open, in milliseconds. Default 5 000.</summary>
    public int BreakDurationMs { get; set; } = 5_000;

    /// <summary>Gets or sets the duration above which a call counts as slow, in milliseconds; -1 (the default) disables slow-call tracking.</summary>
    public int SlowCallDurationThresholdMs { get; set; } = -1;

    /// <summary>Gets or sets the slow-call ratio that opens the circuit. Default 1.0.</summary>
    public double SlowCallRatio { get; set; } = 1.0;

    /// <summary>Gets or sets the predicate: <c>bool M(CircuitBreakerPredicateArguments&lt;T&gt;)</c> or <c>bool M(Outcome&lt;T&gt;)</c>.</summary>
    public string? ShouldHandle { get; set; }

    /// <summary>Gets or sets the break-duration generator: <c>TimeSpan M(BreakDurationGeneratorArguments)</c>.</summary>
    public string? BreakDurationGenerator { get; set; }

    /// <summary>Gets or sets the event raised when the circuit opens: <c>ValueTask M(OnCircuitOpenedArguments&lt;T&gt;)</c>.</summary>
    public string? OnOpened { get; set; }

    /// <summary>Gets or sets the event raised when the circuit closes: <c>ValueTask M(OnCircuitClosedArguments&lt;T&gt;)</c>.</summary>
    public string? OnClosed { get; set; }

    /// <summary>Gets or sets the event raised when the circuit half-opens: <c>ValueTask M(OnCircuitHalfOpenedArguments)</c>.</summary>
    public string? OnHalfOpened { get; set; }

    /// <summary>Gets or sets a field or property of type <see cref="CircuitBreaker.CircuitBreakerManualControl"/>.</summary>
    public string? ManualControl { get; set; }

    /// <summary>Gets or sets a field or property of type <see cref="CircuitBreaker.CircuitBreakerStateProvider"/>.</summary>
    public string? StateProvider { get; set; }
}

/// <summary>A fallback (<see cref="Fallback.FallbackStrategyOptions{TResult}"/>). The action is required, by name or with <see cref="FallbackWithAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FallbackAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the predicate: <c>bool M(FallbackPredicateArguments&lt;T&gt;)</c> or <c>bool M(Outcome&lt;T&gt;)</c>.</summary>
    public string? ShouldHandle { get; set; }

    /// <summary>
    /// Gets or sets the action that produces the replacement: taking <c>FallbackActionArguments&lt;T&gt;</c>,
    /// <c>Outcome&lt;T&gt;</c> or nothing, and returning <c>ValueTask&lt;Outcome&lt;T&gt;&gt;</c>, <c>Outcome&lt;T&gt;</c>,
    /// <c>ValueTask&lt;T&gt;</c> or <c>T</c>.
    /// </summary>
    public string? FallbackAction { get; set; }

    /// <summary>Gets or sets the event raised before the action: <c>ValueTask M(OnFallbackArguments&lt;T&gt;)</c>.</summary>
    public string? OnFallback { get; set; }
}

/// <summary>A <c>System.Threading.RateLimiting</c> limiter (<see cref="RateLimiterStrategyOptions"/>). Without <see cref="RateLimiter"/>, a concurrency limiter of 1000 permits and no queue.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RateLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>
    /// Gets or sets a field or property of type <c>System.Threading.RateLimiting.RateLimiter</c> (not owned by
    /// the pipeline), or a method <c>ValueTask&lt;RateLimitLease&gt; M(RateLimiterArguments)</c>.
    /// </summary>
    public string? RateLimiter { get; set; }

    /// <summary>Gets or sets the event raised when a lease is refused: <c>ValueTask M(OnRateLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>A concurrency limiter over a <c>System.Threading.RateLimiting.ConcurrencyLimiter</c> the pipeline creates (Polly's <c>AddConcurrencyLimiter</c>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ConcurrencyLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the maximum number of calls in flight. Default 1000.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the maximum number of calls waiting for a permit. Default 0.</summary>
    public int QueueLimit { get; set; }

    /// <summary>Gets or sets the event raised when a lease is refused: <c>ValueTask M(OnRateLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>A lease-free fixed-window limiter (<see cref="FixedWindowLimiterStrategyOptions"/>, beyond Polly).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FixedWindowLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the calls admitted per window. Default 1000.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the window in milliseconds. Default 1 000.</summary>
    public int WindowMs { get; set; } = 1_000;

    /// <summary>Gets or sets the event raised on a rejection: <c>ValueTask M(OnLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>A lease-free sliding-window limiter (<see cref="SlidingWindowLimiterStrategyOptions"/>, beyond Polly).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SlidingWindowLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the calls admitted per window. Default 1000.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the window in milliseconds. Default 1 000.</summary>
    public int WindowMs { get; set; } = 1_000;

    /// <summary>Gets or sets the number of segments the window slides by. Default 10; valid 1 to 100.</summary>
    public int SegmentsPerWindow { get; set; } = 10;

    /// <summary>Gets or sets the event raised on a rejection: <c>ValueTask M(OnLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>A lease-free concurrency limiter without a queue (<see cref="NativeConcurrencyLimiterStrategyOptions"/>, beyond Polly).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class NativeConcurrencyLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the maximum number of calls in flight. Default 1000.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the event raised on a rejection: <c>ValueTask M(OnLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>An adaptive concurrency limit (<see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}"/>, beyond Polly).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AdaptiveConcurrencyLimiterAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the algorithm. Default <see cref="AdaptiveConcurrencyAlgorithm.Gradient"/>.</summary>
    public AdaptiveConcurrencyAlgorithm Algorithm { get; set; } = AdaptiveConcurrencyAlgorithm.Gradient;

    /// <summary>Gets or sets the starting limit. Default 20.</summary>
    public int InitialLimit { get; set; } = 20;

    /// <summary>Gets or sets the lowest limit. Default 1.</summary>
    public int MinLimit { get; set; } = 1;

    /// <summary>Gets or sets the highest limit. Default 1000.</summary>
    public int MaxLimit { get; set; } = 1000;

    /// <summary>Gets or sets the AIMD backoff ratio. Default 0.9.</summary>
    public double BackoffRatio { get; set; } = 0.9;

    /// <summary>Gets or sets the gradient smoothing. Default 0.2.</summary>
    public double Smoothing { get; set; } = 0.2;

    /// <summary>Gets or sets the tolerated latency ratio. Default 1.5.</summary>
    public double Tolerance { get; set; } = 1.5;

    /// <summary>Gets or sets the number of latency samples in the window. Default 600.</summary>
    public int LatencyWindow { get; set; } = 600;

    /// <summary>Gets or sets the AIMD latency threshold in milliseconds; -1 (the default) means none.</summary>
    public int LatencyThresholdMs { get; set; } = -1;

    /// <summary>Gets or sets the predicate for drops: <c>bool M(AdaptiveConcurrencyLimiterPredicateArguments&lt;T&gt;)</c> or <c>bool M(Outcome&lt;T&gt;)</c>.</summary>
    public string? ShouldHandle { get; set; }

    /// <summary>Gets or sets the event raised on a rejection: <c>ValueTask M(OnLimiterRejectedArguments)</c>.</summary>
    public string? OnRejected { get; set; }
}

/// <summary>
/// Hedging (<see cref="Hedging.HedgingStrategyOptions{TResult}"/>). Typed pipelines only. A pipeline that
/// contains hedging is not flattened: the generator builds the equivalent runtime pipeline once and forwards
/// to it, with the same public surface.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HedgingAttribute : ResilienceStrategyAttribute
{
    /// <summary>Gets or sets the maximum number of hedged attempts. Default 1.</summary>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <summary>Gets or sets the delay before a hedged attempt, in milliseconds; 0 is parallel, negative is fallback mode. Default 2 000.</summary>
    public int DelayMs { get; set; } = 2_000;

    /// <summary>Gets or sets the predicate: <c>bool M(HedgingPredicateArguments&lt;T&gt;)</c> or <c>bool M(Outcome&lt;T&gt;)</c>.</summary>
    public string? ShouldHandle { get; set; }

    /// <summary>Gets or sets the action generator: <c>Func&lt;ValueTask&lt;Outcome&lt;T&gt;&gt;&gt;? M(HedgingActionGeneratorArguments&lt;T&gt;)</c>.</summary>
    public string? ActionGenerator { get; set; }

    /// <summary>Gets or sets the delay generator: <c>TimeSpan M(HedgingDelayGeneratorArguments)</c>.</summary>
    public string? DelayGenerator { get; set; }

    /// <summary>Gets or sets the event raised for each hedged attempt: <c>ValueTask M(OnHedgingArguments&lt;T&gt;)</c>.</summary>
    public string? OnHedging { get; set; }
}

/// <summary>Base class of the chaos attributes (Simmy): an injection rate and an on/off switch.</summary>
public abstract class ChaosStrategyAttribute : ResilienceStrategyAttribute
{
    private protected ChaosStrategyAttribute()
    {
    }

    /// <summary>Gets or sets the injection rate, 0 to 1. Default 0.001.</summary>
    public double InjectionRate { get; set; } = 0.001;

    /// <summary>Gets or sets whether the strategy is enabled. Default <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the hook that decides per execution whether the strategy is enabled: <c>bool M(EnabledGeneratorArguments)</c>.</summary>
    public string? EnabledGenerator { get; set; }

    /// <summary>Gets or sets the hook that computes the injection rate per execution: <c>double M(InjectionRateGeneratorArguments)</c>.</summary>
    public string? InjectionRateGenerator { get; set; }
}

/// <summary>Injects an exception (<see cref="Simmy.Fault.ChaosFaultStrategyOptions"/>): <see cref="FaultType"/>, or a <see cref="FaultGenerator"/> hook.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ChaosFaultAttribute : ChaosStrategyAttribute
{
    /// <summary>Gets or sets the type of exception to inject; it needs a public parameterless constructor.</summary>
    public Type? FaultType { get; set; }

    /// <summary>Gets or sets the fault generator: <c>Exception? M(FaultGeneratorArguments)</c>.</summary>
    public string? FaultGenerator { get; set; }

    /// <summary>Gets or sets the event raised after an injection: <c>ValueTask M(OnFaultInjectedArguments)</c>.</summary>
    public string? OnFaultInjected { get; set; }
}

/// <summary>Injects an outcome (<see cref="Simmy.Outcomes.ChaosOutcomeStrategyOptions{TResult}"/>). Typed pipelines only.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ChaosOutcomeAttribute : ChaosStrategyAttribute
{
    /// <summary>Gets or sets the outcome generator (required): <c>Outcome&lt;T&gt;? M(OutcomeGeneratorArguments)</c>.</summary>
    public string? OutcomeGenerator { get; set; }

    /// <summary>Gets or sets the event raised after an injection: <c>ValueTask M(OnOutcomeInjectedArguments&lt;T&gt;)</c>.</summary>
    public string? OnOutcomeInjected { get; set; }
}

/// <summary>Injects latency (<see cref="Simmy.Latency.ChaosLatencyStrategyOptions"/>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ChaosLatencyAttribute : ChaosStrategyAttribute
{
    /// <summary>Gets or sets the injected latency in milliseconds. Default 30 000.</summary>
    public int LatencyMs { get; set; } = 30_000;

    /// <summary>Gets or sets the latency generator: <c>TimeSpan M(LatencyGeneratorArguments)</c>.</summary>
    public string? LatencyGenerator { get; set; }

    /// <summary>Gets or sets the event raised after an injection: <c>ValueTask M(OnLatencyInjectedArguments)</c>.</summary>
    public string? OnLatencyInjected { get; set; }
}

/// <summary>Injects a behavior (<see cref="Simmy.Behavior.ChaosBehaviorStrategyOptions"/>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ChaosBehaviorAttribute : ChaosStrategyAttribute
{
    /// <summary>Gets or sets the behavior (required): <c>ValueTask M(BehaviorGeneratorArguments)</c>.</summary>
    public string? BehaviorGenerator { get; set; }

    /// <summary>Gets or sets the event raised after an injection: <c>ValueTask M(OnBehaviorInjectedArguments)</c>.</summary>
    public string? OnBehaviorInjected { get; set; }
}
