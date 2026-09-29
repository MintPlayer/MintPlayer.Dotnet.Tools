using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MintPlayer.Resilience;
using MintPlayer.Resilience.Retry;

namespace ResilienceSamples;

// The two declarative pipelines of PRD §2.1, verbatim apart from the namespace: they must generate and compile.

/// <summary>The PRD's static-form pipeline: total timeout, retry, circuit breaker, per-attempt timeout.</summary>
[ResiliencePipeline]
[Timeout(TimeoutMs = 10_000)]                             // outer: total budget
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, DelayMs = 200, UseJitter = true)]
[CircuitBreaker(FailureRatio = 0.5, SamplingDurationMs = 30_000, MinimumThroughput = 10, BreakDurationMs = 15_000)]
[Timeout(TimeoutMs = 2_000)]                              // inner: per attempt
public sealed partial class CatalogPipeline   // not `static`: it must be usable as a type argument (S6)
{
    // Optional predicate hooks, discovered by convention/attribute. They are synchronous, and the
    // generator inlines them.
    [RetryWhen] static bool Transient(Outcome<HttpResponseMessage> o) =>
        o.Exception is HttpRequestException || (o.Result is { } r && (int)r.StatusCode >= 500);
}

/// <summary>The PRD's DI-form pipeline: an instance hook that logs through an injected logger.</summary>
[ResiliencePipeline]
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, DelayMs = 200)]
public partial class OrdersPipeline(ILogger<OrdersPipeline> logger)
{
    [OnRetry] ValueTask LogRetry(OnRetryArguments<HttpResponseMessage> args)
    {
        logger.LogWarning("Retry {Attempt}", args.AttemptNumber);
        return default;
    }
}
