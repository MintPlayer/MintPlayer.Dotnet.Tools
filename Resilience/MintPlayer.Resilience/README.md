# MintPlayer.Resilience

Resilience pipelines for .NET: retry, timeout and fallback today, with circuit breaker, rate limiting,
hedging and chaos to follow. The public API mirrors Polly v8 name for name, so moving over is mostly a
namespace swap. It targets .NET 10 and .NET 11, is AOT- and trimming-safe, and is Apache-2.0 licensed.

> **Status: in development.** Milestone 1 (the runtime core) is done. Benchmarks against Polly
> 8.8.0 and the source-generated fast path follow; this README will only quote measured numbers.

## Quick start

```csharp
using MintPlayer.Resilience;
using MintPlayer.Resilience.Fallback;
using MintPlayer.Resilience.Retry;

var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
    {
        FallbackAction = static _ => Outcome.FromResultAsValueTask(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
    })
    .AddTimeout(TimeSpan.FromSeconds(10))              // total budget
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,                          // 3 retries = 4 attempts
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(static r => (int)r.StatusCode >= 500),
    })
    .AddTimeout(TimeSpan.FromSeconds(2))               // per attempt
    .Build();

// A static lambda plus state keeps the call allocation-free.
var response = await pipeline.ExecuteAsync(static (state, ct) => state.client.GetAsync(state.url, ct), (client, url), ct);
```

Strategies run in the order they are added; the first one is the outermost.

## Rejections without exceptions

`TryExecuteAsync` returns an `Outcome<T>` instead of throwing. A rejection (a timeout today; an open
circuit or a limiter refusal later) is a value, `Outcome.Rejection`, so it allocates nothing:

```csharp
var outcome = await pipeline.TryExecuteAsync(static (c, ct) => c.GetAsync("/items", ct), client, ct);
if (outcome.Rejection == RejectionKind.Timeout) { /* … */ }
```

`ExecuteAsync` throws Polly's exception types (`TimeoutRejectedException`, `BrokenCircuitException`,
`IsolatedCircuitException`, `RateLimiterRejectedException`, all deriving from
`ResilienceRejectedException`), a fresh instance per throw. Reading `Outcome.Exception` on a rejection
also creates a fresh instance; test `Outcome.Rejection` to avoid it.

## Differences from Polly v8

- **Predicates and generators are synchronous.** `ShouldHandle` returns `bool`, `DelayGenerator` returns
  `TimeSpan?`, `TimeoutGenerator` returns `TimeSpan`. `PredicateBuilder` converts implicitly, as in Polly.
  Events (`OnRetry`, `OnTimeout`, `OnFallback`) and `FallbackAction` stay `ValueTask`-returning.
- **The returned `ValueTask` is pooled.** Await it exactly once. Awaiting it twice, reading `.Result`
  before it completes, or awaiting it from two places throws (or, for two concurrent awaiters, crashes
  the process). Call `.AsTask()` when you need a `Task`, or build the pipeline with
  `.UsePooledAsync(false)` for code that breaks those rules.
- **`TryExecuteAsync`** is new (see above).
- A context is optional. Without one, a `ResilienceContext` is only rented when a strategy delegate reads
  `args.Context`.

## How it works

A pipeline is one async interpreter method over the strategies' synchronous hooks, not a chain of
async layers. The callback runs once per attempt inside that single method, so a callback that really
suspends costs one state-machine box for the whole pipeline, and that box is pooled. The sync-completing
happy path allocates nothing.

## Compiler requirements

The source generator that ships in this package (coming in a later milestone) is built against Roslyn
5.9 and needs the .NET SDK 10.0.4xx or later.
