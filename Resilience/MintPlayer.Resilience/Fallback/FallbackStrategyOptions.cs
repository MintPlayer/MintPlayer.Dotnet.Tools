using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Fallback;

/// <summary>Options of the fallback strategy.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public class FallbackStrategyOptions<TResult> : ResilienceStrategyOptions
{
    internal static readonly Func<FallbackPredicateArguments<TResult>, bool> DefaultShouldHandle = static args => DefaultPredicates.HandleOutcome(args.Outcome);

    /// <summary>Initializes the options.</summary>
    public FallbackStrategyOptions() => Name = "Fallback";

    /// <summary>
    /// Gets or sets the synchronous predicate that decides whether the fallback replaces an outcome.
    /// Default: every exception except <see cref="OperationCanceledException"/>, and every rejection. A
    /// <see cref="PredicateBuilder{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<FallbackPredicateArguments<TResult>, bool> ShouldHandle { get; set; } = DefaultShouldHandle;

    /// <summary>Gets or sets the action that produces the replacement outcome. Required.</summary>
    public Func<FallbackActionArguments<TResult>, ValueTask<Outcome<TResult>>>? FallbackAction { get; set; }

    /// <summary>Gets or sets the event raised before the fallback action runs.</summary>
    public Func<OnFallbackArguments<TResult>, ValueTask>? OnFallback { get; set; }

    internal override void Validate()
    {
        RequireNotNull(ShouldHandle, nameof(ShouldHandle));
        RequireNotNull(FallbackAction, nameof(FallbackAction));
    }
}

/// <summary>The arguments of <see cref="FallbackStrategyOptions{TResult}.ShouldHandle"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct FallbackPredicateArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome to test.</param>
    public FallbackPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        _source = context;
        Outcome = outcome;
    }

    internal FallbackPredicateArguments(ExecutionFrame frame, Outcome<TResult> outcome)
    {
        _source = frame;
        Outcome = outcome;
    }

    /// <summary>Gets the outcome to test.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="FallbackStrategyOptions{TResult}.FallbackAction"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct FallbackActionArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome being replaced.</param>
    public FallbackActionArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        _source = context;
        Outcome = outcome;
    }

    internal FallbackActionArguments(ExecutionFrame frame, Outcome<TResult> outcome)
    {
        _source = frame;
        Outcome = outcome;
    }

    /// <summary>Gets the outcome being replaced.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="FallbackStrategyOptions{TResult}.OnFallback"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnFallbackArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome being replaced.</param>
    public OnFallbackArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        _source = context;
        Outcome = outcome;
    }

    internal OnFallbackArguments(ExecutionFrame frame, Outcome<TResult> outcome)
    {
        _source = frame;
        Outcome = outcome;
    }

    /// <summary>Gets the outcome being replaced.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}
