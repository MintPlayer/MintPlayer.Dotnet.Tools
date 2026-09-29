using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience;

/// <summary>Adds a retry strategy to a builder.</summary>
public static class RetryResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a retry strategy.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder AddRetry(this ResiliencePipelineBuilder builder, RetryStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(context => new RetryStrategyFactory(options, context), options);
        return builder;
    }

    /// <summary>Adds a retry strategy.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddRetry<TResult>(this ResiliencePipelineBuilder<TResult> builder, RetryStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(context => new RetryStrategy<TResult>(options, context), options);
    }
}

/// <summary>Adds a timeout strategy to a builder.</summary>
public static class TimeoutResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a timeout strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="timeout">The timeout; valid 10 ms to 1 day.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddTimeout<TBuilder>(this TBuilder builder, TimeSpan timeout)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddTimeout(new TimeoutStrategyOptions { Timeout = timeout });

    /// <summary>Adds a timeout strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddTimeout<TBuilder>(this TBuilder builder, TimeoutStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(context => new TimeoutStrategyFactory(options, context), options);
        return builder;
    }
}

/// <summary>Adds a circuit-breaker strategy to a builder.</summary>
public static class CircuitBreakerResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a circuit breaker. Its state is shared by every result type the pipeline executes.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder AddCircuitBreaker(this ResiliencePipelineBuilder builder, CircuitBreakerStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(context => new CircuitBreakerStrategyFactory(options, context), options);
        return builder;
    }

    /// <summary>Adds a circuit breaker.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddCircuitBreaker<TResult>(this ResiliencePipelineBuilder<TResult> builder, CircuitBreakerStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(
            context =>
            {
                var snapshot = options.Snapshot();
                var controller = CircuitBreakerSetup.CreateController(snapshot, context);
                CircuitBreakerSetup.Attach(snapshot, controller);
                return new CircuitBreakerStrategy<TResult>(
                    controller,
                    snapshot.ShouldHandle,
                    new CircuitEventHandlers<TResult>(snapshot.OnOpened, snapshot.OnClosed, snapshot.OnHalfOpened));
            },
            options);
    }
}

/// <summary>Adds a fallback strategy to a builder.</summary>
public static class FallbackResiliencePipelineBuilderExtensions
{
    /// <summary>Adds a fallback strategy.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddFallback<TResult>(this ResiliencePipelineBuilder<TResult> builder, FallbackStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(_ => new FallbackStrategy<TResult>(options), options);
    }
}
