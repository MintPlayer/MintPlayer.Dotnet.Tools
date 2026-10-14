using System.Threading.RateLimiting;
using MintPlayer.Resilience.RateLimiting;

namespace MintPlayer.Resilience;

/// <summary>Adds a <c>System.Threading.RateLimiting</c> rate or concurrency limiter to a builder (Polly's extensions).</summary>
public static class RateLimiterResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a concurrency limiter over a <see cref="ConcurrencyLimiter"/> the pipeline creates and owns.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="permitLimit">The maximum number of calls in flight.</param>
    /// <param name="queueLimit">The maximum number of calls waiting for a permit. Default 0.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddConcurrencyLimiter<TBuilder>(this TBuilder builder, int permitLimit, int queueLimit = 0)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = permitLimit,
            QueueLimit = queueLimit,
        });

    /// <summary>Adds a concurrency limiter over a <see cref="ConcurrencyLimiter"/> the pipeline creates and owns.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The limiter options.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddConcurrencyLimiter<TBuilder>(this TBuilder builder, ConcurrencyLimiterOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddRateLimiter(new RateLimiterStrategyOptions { DefaultRateLimiterOptions = options });
    }

    /// <summary>
    /// Adds a rate limiter over <paramref name="limiter"/>. The limiter is not owned: dispose it yourself.
    /// It is called directly with the execution's cancellation token, so no context is rented.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="limiter">The limiter; may be shared with other pipelines.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddRateLimiter<TBuilder>(this TBuilder builder, RateLimiter limiter)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(limiter);
        return builder.AddRateLimiter(new RateLimiterStrategyOptions { Instance = limiter });
    }

    /// <summary>Adds a rate limiter.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddRateLimiter<TBuilder>(this TBuilder builder, RateLimiterStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(_ => new RateLimiterStrategyFactory(options), options);
        return builder;
    }
}

/// <summary>
/// Adds the lease-free limiters (beyond Polly) to a builder: fixed window, sliding window and
/// concurrency. Admissions and rejections allocate nothing; the limiter state belongs to the built
/// pipeline and is shared by every result type it executes.
/// </summary>
public static class NativeLimiterResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a lease-free fixed-window rate limiter: at most <paramref name="permitLimit"/> calls start per <paramref name="window"/>.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="permitLimit">The calls admitted per window.</param>
    /// <param name="window">The window length.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddFixedWindowLimiter<TBuilder>(this TBuilder builder, int permitLimit, TimeSpan window)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddFixedWindowLimiter(new FixedWindowLimiterStrategyOptions { PermitLimit = permitLimit, Window = window });

    /// <summary>Adds a lease-free fixed-window rate limiter.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddFixedWindowLimiter<TBuilder>(this TBuilder builder, FixedWindowLimiterStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(
            context =>
            {
                var snapshot = options.Snapshot();
                return new NativeLimiterStrategyFactory(new FixedWindowLimiter(context.TimeProvider, snapshot.PermitLimit, snapshot.Window), snapshot.OnRejected);
            },
            options);
        return builder;
    }

    /// <summary>Adds a lease-free sliding-window rate limiter: at most <paramref name="permitLimit"/> calls start in any <paramref name="window"/>.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="permitLimit">The calls admitted per window.</param>
    /// <param name="window">The window length.</param>
    /// <param name="segmentsPerWindow">How many segments the window is divided into. Default 10.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddSlidingWindowLimiter<TBuilder>(this TBuilder builder, int permitLimit, TimeSpan window, int segmentsPerWindow = 10)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddSlidingWindowLimiter(new SlidingWindowLimiterStrategyOptions { PermitLimit = permitLimit, Window = window, SegmentsPerWindow = segmentsPerWindow });

    /// <summary>Adds a lease-free sliding-window rate limiter.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddSlidingWindowLimiter<TBuilder>(this TBuilder builder, SlidingWindowLimiterStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(
            context =>
            {
                var snapshot = options.Snapshot();
                return new NativeLimiterStrategyFactory(
                    new SlidingWindowLimiter(context.TimeProvider, snapshot.PermitLimit, snapshot.Window, snapshot.SegmentsPerWindow),
                    snapshot.OnRejected);
            },
            options);
        return builder;
    }

    /// <summary>Adds a lease-free concurrency limiter: at most <paramref name="permitLimit"/> calls in flight, no queue.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="permitLimit">The maximum number of calls in flight.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddNativeConcurrencyLimiter<TBuilder>(this TBuilder builder, int permitLimit)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddNativeConcurrencyLimiter(new NativeConcurrencyLimiterStrategyOptions { PermitLimit = permitLimit });

    /// <summary>Adds a lease-free concurrency limiter.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddNativeConcurrencyLimiter<TBuilder>(this TBuilder builder, NativeConcurrencyLimiterStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(
            _ =>
            {
                var snapshot = options.Snapshot();
                return new NativeLimiterStrategyFactory(new NativeConcurrencyLimiter(snapshot.PermitLimit), snapshot.OnRejected);
            },
            options);
        return builder;
    }
}

/// <summary>Adds the adaptive concurrency limiter (beyond Polly) to a builder.</summary>
public static class AdaptiveConcurrencyLimiterResiliencePipelineBuilderExtensions
{
    /// <summary>Adds an adaptive concurrency limiter. Its state is shared by every result type the pipeline executes.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder AddAdaptiveConcurrencyLimiter(this ResiliencePipelineBuilder builder, AdaptiveConcurrencyLimiterStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(context => new AdaptiveConcurrencyLimiterStrategyFactory(options, context), options);
        return builder;
    }

    /// <summary>Adds an adaptive concurrency limiter.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddAdaptiveConcurrencyLimiter<TResult>(this ResiliencePipelineBuilder<TResult> builder, AdaptiveConcurrencyLimiterStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(
            context =>
            {
                var snapshot = options.Snapshot();
                return new AdaptiveConcurrencyLimiterStrategy<TResult>(AdaptiveLimiterSetup.CreateController(snapshot, context), snapshot.ShouldHandle, snapshot.OnRejected);
            },
            options);
    }
}
