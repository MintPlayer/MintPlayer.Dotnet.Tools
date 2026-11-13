using MintPlayer.Resilience.Hedging;

namespace MintPlayer.Resilience;

/// <summary>Adds a hedging strategy to a builder.</summary>
public static class HedgingResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds a hedging strategy. The strategies added after it run once per attempt; those added before it see
    /// only the winning outcome.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddHedging<TResult>(this ResiliencePipelineBuilder<TResult> builder, HedgingStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(context => new HedgingStrategy<TResult>(options, context), options);
    }
}
