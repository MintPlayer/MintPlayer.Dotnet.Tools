# S7 snippet (c): circuit breaker + fallback + timeout

The target code is illustrative.

## Polly v8 (source)

Combined from `C:\Repos\polly\docs\strategies\fallback.md` L150-172 (`fallback-pattern-after-retries`:
a shared `PredicateBuilder`, `Outcome.FromResultAsValueTask`) and
`C:\Repos\polly\docs\strategies\circuit-breaker.md` L44-65 (ratio/throughput options, `StateProvider`).

```csharp
var stateProvider = new CircuitBreakerStateProvider();

var transient = new PredicateBuilder<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .Handle<TimeoutRejectedException>()
    .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError);

ResiliencePipeline<HttpResponseMessage> pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
    {
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<BrokenCircuitException>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError),
        FallbackAction = static args => Outcome.FromResultAsValueTask(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }),
        OnFallback = args =>
        {
            logger.LogWarning(args.Outcome.Exception, "Serving cached catalog");
            return default;
        },
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(10),
        MinimumThroughput = 8,
        BreakDuration = TimeSpan.FromSeconds(30),
        ShouldHandle = transient,
        StateProvider = stateProvider,
    })
    .AddTimeout(TimeSpan.FromSeconds(2))
    .Build();

var response = await pipeline.ExecuteAsync(static (c, ct) => c.GetAsync("/catalog", ct), client, ct);
bool healthy = stateProvider.CircuitState == CircuitState.Closed;
```

## Target: declarative

```csharp
[ResiliencePipeline<HttpResponseMessage>]
[Fallback(ShouldHandle = nameof(FallbackWhen), FallbackAction = nameof(CachedCatalog), OnFallback = nameof(LogFallback))]
[CircuitBreaker(FailureRatio = 0.5, SamplingDurationMs = 10_000, MinimumThroughput = 8, BreakDurationMs = 30_000,
                ShouldHandle = nameof(Transient))]
[Timeout(TimeoutMs = 2_000)]
public sealed partial class CatalogPipeline(ILogger<CatalogPipeline> logger)
{
    private static readonly PredicateBuilder<HttpResponseMessage> Transient = new PredicateBuilder<HttpResponseMessage>()
        .Handle<HttpRequestException>()
        .Handle<TimeoutRejectedException>()
        .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError);

    private static readonly PredicateBuilder<HttpResponseMessage> FallbackWhen = new PredicateBuilder<HttpResponseMessage>()
        .Handle<BrokenCircuitException>()
        .Handle<HttpRequestException>()
        .Handle<TimeoutRejectedException>()
        .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError);

    // The fallback action stays async: it may call a secondary service.
    private static ValueTask<Outcome<HttpResponseMessage>> CachedCatalog(FallbackActionArguments<HttpResponseMessage> args) =>
        Outcome.FromResultAsValueTask(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });

    private ValueTask LogFallback(OnFallbackArguments<HttpResponseMessage> args)
    {
        logger.LogWarning(args.Outcome.Exception, "Serving cached catalog");
        return default;
    }
}

var response = await catalogPipeline.ExecuteAsync(static (c, ct) => c.GetAsync("/catalog", ct), client, ct);
bool healthy = catalogPipeline.CircuitBreaker.State == CircuitState.Closed;   // generated accessor, no provider object
```

## Target: runtime builder

The Polly source compiles unchanged after the `using` swap: `PredicateBuilder<T>` converts implicitly to
the synchronous `Func<…, bool>`, and `FallbackAction`/`OnFallback` keep their `ValueTask` signatures.

## Differences

| # | Polly | MintPlayer.Resilience | Kind |
|---|---|---|---|
| c1 | `CircuitBreakerStateProvider` / `CircuitBreakerManualControl` objects passed in options | builder: same types; declarative: generated `Pipeline.CircuitBreaker.State` / `.Isolate()` / `.Close()` (the controller is a static per pipeline) | shape |
| c2 | An open circuit throws `BrokenCircuitException` (~5 µs) | `ExecuteAsync`: same exception type. `TryExecuteAsync`: an `Outcome` carrying a cached stackless `BrokenCircuitException`, so `Handle<BrokenCircuitException>()` in the fallback still matches | semantic (Try API only) |
| c3 | `FallbackAction`: `Func<FallbackActionArguments<T>, ValueTask<Outcome<T>>>` | unchanged (async is intrinsic here) | none |
| c4 | `OnFallback`, `OnOpened`, `OnClosed`, `OnHalfOpened`: `Func<Args, ValueTask>` | unchanged | none |
| c5 | `BreakDurationGenerator`: `Func<BreakDurationGeneratorArguments, ValueTask<TimeSpan>>` | `Func<BreakDurationGeneratorArguments, TimeSpan>` (synchronous like every other generator) | signature |
| c6 | Circuit state is per pipeline instance built | declarative: one static controller per pipeline class, shared by every consumer, the same as a Polly pipeline registered once as a singleton | semantic |
