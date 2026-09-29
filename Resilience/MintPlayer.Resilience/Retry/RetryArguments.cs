using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Retry;

/// <summary>The arguments of <see cref="RetryStrategyOptions{TResult}.ShouldHandle"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to; the context it refers to is returned to its pool afterwards.</remarks>
public readonly struct RetryPredicateArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based number of the attempt.</param>
    public RetryPredicateArguments(ResilienceContext context, Outcome<TResult> outcome, int attemptNumber)
        : this((object?)context, outcome, attemptNumber)
    {
    }

    internal RetryPredicateArguments(object? source, Outcome<TResult> outcome, int attemptNumber)
    {
        _source = source;
        Outcome = outcome;
        AttemptNumber = attemptNumber;
    }

    /// <summary>Gets the outcome of the attempt.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the 0-based number of the attempt.</summary>
    public int AttemptNumber { get; }

    internal RetryPredicateArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome(), AttemptNumber);
}

/// <summary>The arguments of <see cref="RetryStrategyOptions{TResult}.DelayGenerator"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct RetryDelayGeneratorArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based number of the attempt.</param>
    public RetryDelayGeneratorArguments(ResilienceContext context, Outcome<TResult> outcome, int attemptNumber)
        : this((object?)context, outcome, attemptNumber)
    {
    }

    internal RetryDelayGeneratorArguments(object? source, Outcome<TResult> outcome, int attemptNumber)
    {
        _source = source;
        Outcome = outcome;
        AttemptNumber = attemptNumber;
    }

    /// <summary>Gets the outcome of the attempt.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the 0-based number of the attempt.</summary>
    public int AttemptNumber { get; }

    internal RetryDelayGeneratorArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome(), AttemptNumber);
}

/// <summary>The arguments of <see cref="RetryStrategyOptions{TResult}.OnRetry"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnRetryArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome of the attempt that is being retried.</param>
    /// <param name="attemptNumber">The 0-based number of the attempt that is being retried.</param>
    /// <param name="retryDelay">The delay before the next attempt.</param>
    /// <param name="duration">How long the attempt that is being retried took.</param>
    public OnRetryArguments(ResilienceContext context, Outcome<TResult> outcome, int attemptNumber, TimeSpan retryDelay, TimeSpan duration)
        : this((object?)context, outcome, attemptNumber, retryDelay, duration)
    {
    }

    internal OnRetryArguments(object? source, Outcome<TResult> outcome, int attemptNumber, TimeSpan retryDelay, TimeSpan duration)
    {
        _source = source;
        Outcome = outcome;
        AttemptNumber = attemptNumber;
        RetryDelay = retryDelay;
        Duration = duration;
    }

    /// <summary>Gets the outcome of the attempt that is being retried.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the 0-based number of the attempt that is being retried.</summary>
    public int AttemptNumber { get; }

    /// <summary>Gets the delay before the next attempt.</summary>
    public TimeSpan RetryDelay { get; }

    /// <summary>Gets how long the attempt that is being retried took.</summary>
    public TimeSpan Duration { get; }

    internal OnRetryArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome(), AttemptNumber, RetryDelay, Duration);
}
