using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;

namespace S1;

public static class PollySetup
{
    // Same order, options and predicate as the other implementations. No telemetry (plain Polly.Core).
    public static ResiliencePipeline<int> Create()
    {
        static ValueTask<bool> Handle(Polly.Outcome<int> o) => new(o.Exception is not null || o.Result == -1);

        return new ResiliencePipelineBuilder<int>()
            .AddFallback(new FallbackStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                FallbackAction = static _ => Outcome.FromResultAsValueTask(0),
            })
            .AddTimeout(Config.OuterTimeout)
            .AddRetry(new RetryStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                MaxRetryAttempts = Config.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = Config.RetryDelay,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                ShouldHandle = static args => Handle(args.Outcome),
                FailureRatio = Config.FailureRatio,
                MinimumThroughput = Config.MinimumThroughput,
                SamplingDuration = Config.Sampling,
                BreakDuration = Config.Break,
            })
            .AddTimeout(Config.InnerTimeout)
            .Build();
    }
}
