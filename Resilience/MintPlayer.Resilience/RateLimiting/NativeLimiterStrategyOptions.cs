using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>
/// The arguments of the <c>OnRejected</c> event of the lease-free limiters
/// (<see cref="FixedWindowLimiterStrategyOptions"/>, <see cref="SlidingWindowLimiterStrategyOptions"/>,
/// <see cref="NativeConcurrencyLimiterStrategyOptions"/> and
/// <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}"/>). Beyond Polly.
/// </summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnLimiterRejectedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="retryAfter">When a permit may be available, if the limiter knows.</param>
    public OnLimiterRejectedArguments(ResilienceContext context, TimeSpan? retryAfter)
        : this((object?)context, retryAfter)
    {
    }

    internal OnLimiterRejectedArguments(object? source, TimeSpan? retryAfter)
    {
        _source = source;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets how long until a permit may be available: the rest of the window for the window limiters, null for the concurrency limiters.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Options of the lease-free fixed-window rate limiter (beyond Polly): at most <see cref="PermitLimit"/>
/// calls start in each <see cref="Window"/>. Windows are consecutive and aligned to the moment the
/// pipeline is built. Neither an admission nor a rejection allocates; a rejection is
/// <see cref="RejectionKind.RateLimited"/> with <see cref="Outcome{TResult}.RetryAfter"/> = the rest of
/// the current window.
/// </summary>
/// <remarks>
/// The limiter's state belongs to the built pipeline and is shared by every execution (and every result
/// type) of it. For the <c>System.Threading.RateLimiting</c> limiters, with queueing, use
/// <c>AddRateLimiter</c>.
/// </remarks>
public class FixedWindowLimiterStrategyOptions : ResilienceStrategyOptions
{
    /// <summary>Initializes the options: 1000 permits per 1 s window.</summary>
    public FixedWindowLimiterStrategyOptions() => Name = "FixedWindowLimiter";

    /// <summary>Gets or sets the number of calls admitted per window. Default 1000; valid 1 to <see cref="int.MaxValue"/>.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the length of a window. Default 1 s; valid 1 ms to 1 day.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the event raised when a call is refused, before the rejection is returned.</summary>
    public Func<OnLimiterRejectedArguments, ValueTask>? OnRejected { get; set; }

    internal override void Validate()
    {
        if (PermitLimit < 1)
        {
            Invalid($"The field {nameof(PermitLimit)} must be between 1 and {int.MaxValue}.");
        }

        RequireRange(Window, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1), nameof(Window));
    }

    internal FixedWindowLimiterStrategyOptions Snapshot() => (FixedWindowLimiterStrategyOptions)MemberwiseClone();
}

/// <summary>
/// Options of the lease-free sliding-window rate limiter (beyond Polly): at most <see cref="PermitLimit"/>
/// calls start in any <see cref="Window"/>, measured in <see cref="SegmentsPerWindow"/> segments (the
/// current segment and the ones before it), so permits come back one segment at a time instead of all at
/// once at a window boundary. Neither an admission nor a rejection allocates; a rejection is
/// <see cref="RejectionKind.RateLimited"/> with <see cref="Outcome{TResult}.RetryAfter"/> = the time until
/// the oldest counted segment leaves the window.
/// </summary>
/// <remarks>
/// The limit is never exceeded. Under heavy contention a call can be refused while another refused
/// call's tentative count is being taken back. The segment length is <c>Window / SegmentsPerWindow</c>,
/// truncated to a whole microsecond. The state belongs to the built pipeline.
/// </remarks>
public class SlidingWindowLimiterStrategyOptions : ResilienceStrategyOptions
{
    /// <summary>Initializes the options: 1000 permits per 1 s window of 10 segments.</summary>
    public SlidingWindowLimiterStrategyOptions() => Name = "SlidingWindowLimiter";

    /// <summary>Gets or sets the number of calls admitted per window. Default 1000; valid 1 to <see cref="int.MaxValue"/>.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the length of the window. Default 1 s; valid 1 ms to 1 day.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets how many segments the window is divided into. Default 10; valid 1 to 100.</summary>
    public int SegmentsPerWindow { get; set; } = 10;

    /// <summary>Gets or sets the event raised when a call is refused, before the rejection is returned.</summary>
    public Func<OnLimiterRejectedArguments, ValueTask>? OnRejected { get; set; }

    internal override void Validate()
    {
        if (PermitLimit < 1)
        {
            Invalid($"The field {nameof(PermitLimit)} must be between 1 and {int.MaxValue}.");
        }

        RequireRange(Window, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1), nameof(Window));
        if (SegmentsPerWindow is < 1 or > 100)
        {
            Invalid($"The field {nameof(SegmentsPerWindow)} must be between 1 and 100.");
        }
    }

    internal SlidingWindowLimiterStrategyOptions Snapshot() => (SlidingWindowLimiterStrategyOptions)MemberwiseClone();
}

/// <summary>
/// Options of the lease-free concurrency limiter (beyond Polly): at most <see cref="PermitLimit"/> calls
/// in flight, with no queue. A call that finds every permit taken is rejected at once
/// (<see cref="RejectionKind.RateLimited"/>, no <c>RetryAfter</c>). A permit is returned when the call
/// finishes, whether it succeeded, failed, threw or was cancelled. Neither an admission nor a rejection
/// allocates.
/// </summary>
/// <remarks>
/// This is the allocation-free counterpart of <c>AddConcurrencyLimiter</c>, which wraps
/// <see cref="System.Threading.RateLimiting.ConcurrencyLimiter"/>, can queue, and hands out a lease per
/// call. The state belongs to the built pipeline.
/// </remarks>
public class NativeConcurrencyLimiterStrategyOptions : ResilienceStrategyOptions
{
    /// <summary>Initializes the options: 1000 permits.</summary>
    public NativeConcurrencyLimiterStrategyOptions() => Name = "NativeConcurrencyLimiter";

    /// <summary>Gets or sets the maximum number of calls in flight. Default 1000; valid 1 to <see cref="int.MaxValue"/>.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Gets or sets the event raised when a call is refused, before the rejection is returned.</summary>
    public Func<OnLimiterRejectedArguments, ValueTask>? OnRejected { get; set; }

    internal override void Validate()
    {
        if (PermitLimit < 1)
        {
            Invalid($"The field {nameof(PermitLimit)} must be between 1 and {int.MaxValue}.");
        }
    }

    internal NativeConcurrencyLimiterStrategyOptions Snapshot() => (NativeConcurrencyLimiterStrategyOptions)MemberwiseClone();
}
