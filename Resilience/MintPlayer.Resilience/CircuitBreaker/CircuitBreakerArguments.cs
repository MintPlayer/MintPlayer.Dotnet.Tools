using System.ComponentModel;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>The arguments of <see cref="CircuitBreakerStrategyOptions{TResult}.ShouldHandle"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to; the context it refers to is returned to its pool afterwards.</remarks>
public readonly struct CircuitBreakerPredicateArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome of the execution.</param>
    public CircuitBreakerPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
        : this((object?)context, outcome)
    {
    }

    internal CircuitBreakerPredicateArguments(object? source, Outcome<TResult> outcome)
    {
        _source = source;
        Outcome = outcome;
    }

    /// <summary>Gets the outcome of the execution.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    internal CircuitBreakerPredicateArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome());
}

/// <summary>The arguments of <see cref="CircuitBreakerStrategyOptions{TResult}.OnOpened"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnCircuitOpenedArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution that opened the circuit.</param>
    /// <param name="outcome">The outcome that opened the circuit (a default result for a manual isolation).</param>
    /// <param name="breakDuration">How long the circuit stays open (<see cref="TimeSpan.MaxValue"/> when isolated).</param>
    /// <param name="isManual">Whether the circuit was opened through <see cref="CircuitBreakerManualControl"/>.</param>
    public OnCircuitOpenedArguments(ResilienceContext context, Outcome<TResult> outcome, TimeSpan breakDuration, bool isManual)
        : this((object?)context, outcome, breakDuration, isManual)
    {
    }

    internal OnCircuitOpenedArguments(object? source, Outcome<TResult> outcome, TimeSpan breakDuration, bool isManual)
    {
        _source = source;
        Outcome = outcome;
        BreakDuration = breakDuration;
        IsManual = isManual;
    }

    /// <summary>Gets the outcome that opened the circuit.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution that opened the circuit.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets how long the circuit stays open.</summary>
    public TimeSpan BreakDuration { get; }

    /// <summary>Gets a value indicating whether the circuit was opened manually.</summary>
    public bool IsManual { get; }

    internal OnCircuitOpenedArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome(), BreakDuration, IsManual);
}

/// <summary>The arguments of <see cref="CircuitBreakerStrategyOptions{TResult}.OnClosed"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnCircuitClosedArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution that closed the circuit.</param>
    /// <param name="outcome">The outcome that closed the circuit (a default result for a manual close).</param>
    /// <param name="isManual">Whether the circuit was closed through <see cref="CircuitBreakerManualControl"/>.</param>
    public OnCircuitClosedArguments(ResilienceContext context, Outcome<TResult> outcome, bool isManual)
        : this((object?)context, outcome, isManual)
    {
    }

    internal OnCircuitClosedArguments(object? source, Outcome<TResult> outcome, bool isManual)
    {
        _source = source;
        Outcome = outcome;
        IsManual = isManual;
    }

    /// <summary>Gets the outcome that closed the circuit.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution that closed the circuit.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets a value indicating whether the circuit was closed manually.</summary>
    public bool IsManual { get; }

    internal OnCircuitClosedArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome(), IsManual);
}

/// <summary>The arguments of <see cref="CircuitBreakerStrategyOptions{TResult}.OnHalfOpened"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnCircuitHalfOpenedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the probe execution.</param>
    public OnCircuitHalfOpenedArguments(ResilienceContext context) => _source = context;

    internal OnCircuitHalfOpenedArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the probe execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="CircuitBreakerStrategyOptions{TResult}.BreakDurationGenerator"/>.</summary>
/// <remarks>
/// Valid only during the call it is passed to. <see cref="FailureRate"/> and <see cref="FailureCount"/>
/// describe the health window when the circuit last broke from closed; see the remarks on
/// <see cref="CircuitBreakerStrategyOptions{TResult}.BreakDurationGenerator"/>.
/// </remarks>
public readonly struct BreakDurationGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="failureRate">The failure rate of the health window.</param>
    /// <param name="failureCount">The number of failures in the health window.</param>
    /// <param name="context">The context of the execution that opens the circuit.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public BreakDurationGeneratorArguments(double failureRate, int failureCount, ResilienceContext context)
        : this(failureRate, failureCount, context, 0)
    {
    }

    /// <summary>Initializes the arguments.</summary>
    /// <param name="failureRate">The failure rate of the health window.</param>
    /// <param name="failureCount">The number of failures in the health window.</param>
    /// <param name="context">The context of the execution that opens the circuit.</param>
    /// <param name="halfOpenAttempts">The number of half-open probes since the circuit was last closed.</param>
    public BreakDurationGeneratorArguments(double failureRate, int failureCount, ResilienceContext context, int halfOpenAttempts)
        : this(failureRate, failureCount, (object?)context, halfOpenAttempts)
    {
    }

    internal BreakDurationGeneratorArguments(double failureRate, int failureCount, object? source, int halfOpenAttempts)
    {
        _source = source;
        FailureRate = failureRate;
        FailureCount = failureCount;
        HalfOpenAttempts = halfOpenAttempts;
    }

    /// <summary>Gets the failure rate (failures / throughput) of the health window.</summary>
    public double FailureRate { get; }

    /// <summary>Gets the number of failures in the health window.</summary>
    public int FailureCount { get; }

    /// <summary>Gets the context of the execution that opens the circuit.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the number of half-open probes since the circuit was last closed (0 on the first break).</summary>
    public int HalfOpenAttempts { get; }
}
