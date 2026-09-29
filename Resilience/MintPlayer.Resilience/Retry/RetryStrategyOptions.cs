using System.ComponentModel;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Retry;

/// <summary>Options of the retry strategy.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public class RetryStrategyOptions<TResult> : ResilienceStrategyOptions
{
    internal static readonly Func<RetryPredicateArguments<TResult>, bool> DefaultShouldHandle = static args => DefaultPredicates.HandleOutcome(args.Outcome);

    private static readonly Func<double> DefaultRandomizer = Random.Shared.NextDouble;

    /// <summary>Initializes the options with Polly's defaults: 3 retries, constant 2 s delay, no jitter.</summary>
    public RetryStrategyOptions() => Name = "Retry";

    /// <summary>Gets or sets the maximum number of retries (so at most <c>MaxRetryAttempts + 1</c> attempts). <see cref="int.MaxValue"/> retries forever. Default 3; valid 1 to <see cref="int.MaxValue"/>.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets how the delay grows between retries. Default <see cref="DelayBackoffType.Constant"/>.</summary>
    public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Constant;

    /// <summary>
    /// Gets or sets whether to add jitter to the delay: ±25 % for constant and linear backoff, Polly's
    /// decorrelated jitter for exponential backoff. Default <see langword="false"/>.
    /// </summary>
    public bool UseJitter { get; set; }

    /// <summary>Gets or sets the base delay. Default 2 s; valid 0 to 1 day. Zero means no delay.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the cap on the computed delay (not on a <see cref="DelayGenerator"/> value). Default none; valid 0 to 1 day.</summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>
    /// Gets or sets the synchronous predicate that decides whether an outcome is retried. Default: every
    /// exception except <see cref="OperationCanceledException"/>, and every rejection. A
    /// <see cref="PredicateBuilder{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<RetryPredicateArguments<TResult>, bool> ShouldHandle { get; set; } = DefaultShouldHandle;

    /// <summary>
    /// Gets or sets a synchronous generator that overrides the computed delay. Returning
    /// <see langword="null"/> or a negative value keeps the computed delay.
    /// </summary>
    public Func<RetryDelayGeneratorArguments<TResult>, TimeSpan?>? DelayGenerator { get; set; }

    /// <summary>Gets or sets the event raised before each retry (before the delay).</summary>
    public Func<OnRetryArguments<TResult>, ValueTask>? OnRetry { get; set; }

    /// <summary>Gets or sets the source of random numbers in [0, 1) used for jitter. Intended for tests.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Func<double> Randomizer { get; set; } = DefaultRandomizer;

    internal override void Validate()
    {
        if (MaxRetryAttempts < 1)
        {
            Invalid($"The field {nameof(MaxRetryAttempts)} must be between 1 and {int.MaxValue}.");
        }

        if (!Enum.IsDefined(BackoffType))
        {
            Invalid($"The field {nameof(BackoffType)} has an unsupported value '{BackoffType}'.");
        }

        RequireRange(Delay, TimeSpan.Zero, TimeSpan.FromDays(1), nameof(Delay));
        if (MaxDelay is { } maxDelay)
        {
            RequireRange(maxDelay, TimeSpan.Zero, TimeSpan.FromDays(1), nameof(MaxDelay));
        }

        RequireNotNull(ShouldHandle, nameof(ShouldHandle));
        RequireNotNull(Randomizer, nameof(Randomizer));
    }

    /// <summary>A shallow copy, taken at <c>Build()</c> so later changes to the options do not leak into a built pipeline.</summary>
    internal RetryStrategyOptions<TResult> Snapshot() => (RetryStrategyOptions<TResult>)MemberwiseClone();
}

/// <summary>Options of the retry strategy for a non-generic <see cref="ResiliencePipelineBuilder"/>.</summary>
public class RetryStrategyOptions : RetryStrategyOptions<object>
{
}
