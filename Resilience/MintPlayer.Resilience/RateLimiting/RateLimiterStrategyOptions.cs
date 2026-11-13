using System.Threading.RateLimiting;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>
/// Options of the rate-limiter strategy, which runs each call under a lease from a
/// <c>System.Threading.RateLimiting</c> limiter (Polly's <c>RateLimiterStrategyOptions</c>).
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="RateLimiter"/> unset, a <see cref="ConcurrencyLimiter"/> is created from
/// <see cref="DefaultRateLimiterOptions"/> when the pipeline is built. The lease is disposed when the
/// call finishes, whatever its outcome.
/// </para>
/// <para>
/// A refused lease is <see cref="RejectionKind.RateLimited"/>, with
/// <see cref="Outcome{TResult}.RetryAfter"/> taken from the lease's <see cref="MetadataName.RetryAfter"/>
/// metadata. The BCL lease itself allocates on some limiters (plan S5: 40–64 B for a fixed window); the
/// lease-free limiters (<c>AddFixedWindowLimiter</c>, <c>AddSlidingWindowLimiter</c>,
/// <c>AddNativeConcurrencyLimiter</c>) allocate nothing.
/// </para>
/// </remarks>
public class RateLimiterStrategyOptions : ResilienceStrategyOptions
{
    /// <summary>Initializes the options with Polly's defaults: a concurrency limiter of 1000 permits and no queue.</summary>
    public RateLimiterStrategyOptions() => Name = "RateLimiter";

    /// <summary>
    /// Gets or sets the delegate that acquires a lease for a call. When null, a
    /// <see cref="ConcurrencyLimiter"/> built from <see cref="DefaultRateLimiterOptions"/> is used. As in
    /// Polly (and like <c>ActionGenerator</c>, unlike the synchronous generators), it returns a
    /// <see cref="ValueTask{TResult}"/>, because acquiring may wait in the limiter's queue.
    /// </summary>
    public Func<RateLimiterArguments, ValueTask<RateLimitLease>>? RateLimiter { get; set; }

    /// <summary>Gets or sets the options of the default <see cref="ConcurrencyLimiter"/>, used when <see cref="RateLimiter"/> is null. Default 1000 permits, queue 0.</summary>
    public ConcurrencyLimiterOptions DefaultRateLimiterOptions { get; set; } = new()
    {
        PermitLimit = 1000,
        QueueLimit = 0,
    };

    /// <summary>Gets or sets the event raised when a lease is refused, before the lease is disposed and the rejection returned.</summary>
    public Func<OnRateLimiterRejectedArguments, ValueTask>? OnRejected { get; set; }

    /// <summary>A limiter used directly (without going through a delegate), set by <c>AddRateLimiter(RateLimiter)</c>; not owned.</summary>
    internal RateLimiter? Instance { get; set; }

    internal override void Validate()
    {
        if (RateLimiter is null && Instance is null)
        {
            RequireNotNull(DefaultRateLimiterOptions, nameof(DefaultRateLimiterOptions));
        }
    }

    internal RateLimiterStrategyOptions Snapshot() => (RateLimiterStrategyOptions)MemberwiseClone();
}

/// <summary>The arguments of <see cref="RateLimiterStrategyOptions.RateLimiter"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct RateLimiterArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public RateLimiterArguments(ResilienceContext context) => _source = context;

    internal RateLimiterArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution. Reading it rents a context when the caller passed none; prefer <see cref="CancellationToken"/>.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the cancellation token of the execution, without renting a context (beyond Polly).</summary>
    public CancellationToken CancellationToken => _source switch
    {
        ExecutionFrame frame => frame.CancellationToken,
        ResilienceContext context => context.CancellationToken,
        _ => default,
    };
}

/// <summary>The arguments of <see cref="RateLimiterStrategyOptions.OnRejected"/>.</summary>
/// <remarks>Valid only during the call it is passed to; the lease is disposed afterwards.</remarks>
public readonly struct OnRateLimiterRejectedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="lease">The refused lease.</param>
    public OnRateLimiterRejectedArguments(ResilienceContext context, RateLimitLease lease)
        : this((object?)context, lease)
    {
    }

    internal OnRateLimiterRejectedArguments(object? source, RateLimitLease lease)
    {
        _source = source;
        Lease = lease;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the refused lease, whose metadata may say why and when to retry.</summary>
    public RateLimitLease Lease { get; }
}
