# MintPlayer.Resilience

Resilience pipelines for .NET: retry (with a shared retry budget), timeout, fallback, circuit breaker,
rate and concurrency limiting (including lease-free and adaptive limiters) today, with hedging and chaos
to follow. The public API mirrors Polly v8 name for name, so moving over is mostly a namespace swap. It
targets .NET 10 and .NET 11, is AOT- and trimming-safe, and is Apache-2.0 licensed.

> **Status: in development.** Milestones 1–3 (the runtime core, the circuit breaker, limiters, hedging and chaos) and
> the source generator of milestone 4 are done; its analyzers follow. Benchmarks against Polly 8.8.0 follow; this
> README will only quote measured numbers.

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

## Declarative pipelines

Put the strategies on a `sealed partial class` as attributes, and the source generator in this package
compiles them into the class: one flat, pooled async method with retry, timeout and fallback inlined and their
values folded in. The breaker, limiters and chaos strategies are driven through the runtime's own hooks, so they
behave exactly as in the builder.

```csharp
[ResiliencePipeline]
[Timeout(TimeoutMs = 10_000)]                             // outer: total budget
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, DelayMs = 200, UseJitter = true)]
[CircuitBreaker(FailureRatio = 0.5, SamplingDurationMs = 30_000, MinimumThroughput = 10, BreakDurationMs = 15_000)]
[Timeout(TimeoutMs = 2_000)]                              // inner: per attempt
public sealed partial class CatalogPipeline
{
    [RetryWhen] static bool Transient(Outcome<HttpResponseMessage> o) =>
        o.Exception is HttpRequestException || (o.Result is { } r && (int)r.StatusCode >= 500);
}

var response = await CatalogPipeline.ExecuteAsync(static (c, ct) => c.GetAsync("/items", ct), client, ct);
```

- **Order:** attributes run in declaration order, the first one outermost. Keep them on one declaration of the
  class. Durations are integer milliseconds (`…Ms`); -1 means "not set" on an optional one (`MaxDelayMs`).
- **Hooks:** name a method on the strategy (`[Retry(OnRetry = nameof(Log))]`), or mark it with a hook attribute:
  `[RetryWhen]`, `[OnRetry]`, `[DelayGenerator]`, `[TimeoutGenerator]`, `[OnTimeout]`, `[BreakWhen]`,
  `[OnOpened]`, `[OnClosed]`, `[OnHalfOpened]`, `[BreakDurationGenerator]`, `[FallbackWhen]`, `[FallbackWith]`,
  `[OnFallback]`, `[HedgeWhen]`, `[OnHedging]`. A hook attribute binds to the only strategy of its kind, or to the
  one whose `Name` it passes (`[OnTimeout("Attempt")]`); a name on the strategy wins. Predicates may take the
  arguments struct or just the `Outcome<T>`; events may return `ValueTask` or `void`; a fallback action may
  return `T`, `Outcome<T>`, `ValueTask<T>` or `ValueTask<Outcome<T>>`.
- **Result type:** `[ResiliencePipeline<T>]`, or a hook that names `T` (as above), makes a typed pipeline. Without
  either, the pipeline is generic, like the non-generic `ResiliencePipeline`: `ExecuteAsync<TResult>` plus void
  overloads, with generic hooks (`static bool Handle<TResult>(Outcome<TResult> o)`).
- **Static or DI form:** with only static hooks the members are static and the state is process-wide. An instance
  hook or a constructor with parameters makes the DI form: instance members, state per instance, hooks that use
  injected services (`public partial class OrdersPipeline(ILogger<OrdersPipeline> logger)`).
- **Every strategy has an attribute:** `[Timeout]`, `[Retry]`, `[CircuitBreaker]` (with the slow-call ratio),
  `[Fallback]`, `[RateLimiter]`, `[ConcurrencyLimiter]`, `[FixedWindowLimiter]`, `[SlidingWindowLimiter]`,
  `[NativeConcurrencyLimiter]`, `[AdaptiveConcurrencyLimiter]`, `[Hedging]`, `[ChaosFault]`, `[ChaosOutcome]`,
  `[ChaosLatency]`, `[ChaosBehavior]`, and a class-level `[RetryBudget]`. A pipeline with hedging is not
  flattened: the generated members forward to the equivalent runtime pipeline, built once.
- **Pooling:** the returned `ValueTask` is pooled, as in the builder; `[ResiliencePipeline(PooledAsync = false)]`
  opts out.
- **Reload:** `[ResiliencePipeline(Reloadable = true)]` generates a `CatalogPipelineOptions` class (one section per
  strategy, named by its `Name`, whose defaults are the attribute values) and `TryApply(options, out error)`. Each
  execution reads one snapshot at entry, invalid values are rejected with the builder's rules, and a breaker or
  limiter whose section did not change keeps its state.
- **Tests:** `UseTimeProvider(fakeTime)` replaces the clock (and recreates the state) of the pipeline.

## Rejections without exceptions

`TryExecuteAsync` returns an `Outcome<T>` instead of throwing. A rejection (a timeout, an open or
isolated circuit, a limiter refusal) is a value, `Outcome.Rejection`, so it allocates nothing:

```csharp
var outcome = await pipeline.TryExecuteAsync(static (c, ct) => c.GetAsync("/items", ct), client, ct);
if (outcome.Rejection == RejectionKind.Timeout) { /* … */ }
```

`ExecuteAsync` throws Polly's exception types (`TimeoutRejectedException`, `BrokenCircuitException`,
`IsolatedCircuitException`, `RateLimiterRejectedException`, all deriving from
`ResilienceRejectedException`), a fresh instance per throw. Reading `Outcome.Exception` on a rejection
also creates a fresh instance; test `Outcome.Rejection` to avoid it.

## Circuit breaker

```csharp
using MintPlayer.Resilience.CircuitBreaker;

var state = new CircuitBreakerStateProvider();
var control = new CircuitBreakerManualControl();
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,                            // open at 50 % failures…
        MinimumThroughput = 10,                        // …once 10 calls were measured…
        SamplingDuration = TimeSpan.FromSeconds(30),   // …in the last 30 s
        BreakDuration = TimeSpan.FromSeconds(15),
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(static r => (int)r.StatusCode >= 500),
        SlowCallDurationThreshold = TimeSpan.FromSeconds(2), // beyond Polly: also open when…
        SlowCallRatio = 0.8,                                 // …80 % of the calls take ≥ 2 s
        StateProvider = state,
        ManualControl = control,
    })
    .Build();
```

The semantics are Polly's: the health window is 10 event-anchored windows over `SamplingDuration`,
one probe runs after the break (half-open) and every other call is rejected until it finishes, and
`OnOpened` / `OnHalfOpened` / `OnClosed` run one at a time, in transition order, awaited by the call that
caused them. A rejected call returns `RejectionKind.CircuitOpen` (with `RetryAfter` = the rest of the
break) or `RejectionKind.CircuitIsolated`, and allocates nothing.

The controller is lock-free: the whole circuit state is one `long` changed by compare-and-swap, and
the health counters are striped per core, so a closed-circuit call is one read plus one core-local
increment.

**Slow calls (beyond Polly).** With `SlowCallDurationThreshold` set, each call's duration (admission to
outcome) is measured, and the circuit also opens when slow calls reach `SlowCallRatio` of the window,
behind the same `MinimumThroughput`. A slow call counts whether it succeeds or fails, and a slow probe
counts as a failed probe.

## Rate and concurrency limiting

Polly's strategy over `System.Threading.RateLimiting`, same names:

```csharp
using System.Threading.RateLimiting;
using MintPlayer.Resilience.RateLimiting;

builder.AddConcurrencyLimiter(permitLimit: 100, queueLimit: 50);          // owns a ConcurrencyLimiter
builder.AddRateLimiter(new SlidingWindowRateLimiter(new() { /* … */ }));  // any RateLimiter, not owned
builder.AddRateLimiter(new RateLimiterStrategyOptions
{
    RateLimiter = args => limiter.AcquireAsync(1, args.CancellationToken),
    OnRejected = args => { /* args.Lease */ return default; },
});
```

The lease is disposed when the call finishes, whatever its outcome. A refusal is
`RejectionKind.RateLimited`, with `RetryAfter` from the lease's metadata. The BCL lease is the one
allocation left on this path; Polly additionally walks the stack on every rejection (17–25 KB).

**Lease-free limiters (beyond Polly).** Written for this library: no lease object, so neither an
admission nor a rejection allocates, and they are lock-free (compare-and-swap on packed counters):

```csharp
builder.AddFixedWindowLimiter(permitLimit: 100, window: TimeSpan.FromSeconds(1));
builder.AddSlidingWindowLimiter(permitLimit: 100, window: TimeSpan.FromSeconds(1), segmentsPerWindow: 10);
builder.AddNativeConcurrencyLimiter(permitLimit: 100);                    // no queue
```

- **Fixed window:** at most `PermitLimit` calls start per window; `RetryAfter` is the rest of the window.
- **Sliding window:** at most `PermitLimit` calls in any window, counted per segment, so there is no
  double burst across a boundary; `RetryAfter` is when the oldest counted segment leaves the window.
- **Concurrency:** at most `PermitLimit` calls in flight; the permit comes back on success, failure,
  exception or cancellation. Use `AddConcurrencyLimiter` when you need a queue.

Their state belongs to the built pipeline, shared by every result type a non-generic pipeline runs.

**Adaptive concurrency limit (beyond Polly).** A concurrency limit that moves with the service
(Netflix concurrency-limits):

```csharp
builder.AddAdaptiveConcurrencyLimiter(new AdaptiveConcurrencyLimiterStrategyOptions<HttpResponseMessage>
{
    Algorithm = AdaptiveConcurrencyAlgorithm.Aimd,    // or Gradient (default)
    InitialLimit = 20, MinLimit = 1, MaxLimit = 500,
    LatencyThreshold = TimeSpan.FromMilliseconds(250), // slower counts as a drop
    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
        .HandleResult(static r => r.StatusCode == HttpStatusCode.ServiceUnavailable),
});
```

- **AIMD:** +1 per round trip while at least half used; × `BackoffRatio` on a drop (a handled outcome
  or a call slower than `LatencyThreshold`), once per burst.
- **Gradient:** follows `Tolerance × no-load latency / latency`; it settles where the queueing it causes
  balances a √limit headroom. The no-load latency is the lowest seen, so start from a limit the service
  handles without queueing, and prefer AIMD where the baseline latency moves for good.

A refused call is `RejectionKind.RateLimited` with no `RetryAfter`, and allocates nothing.

## Retry budget (beyond Polly)

A per-call `MaxRetryAttempts` cannot stop a retry storm: when a dependency fails, every caller multiplies
its load. A `RetryBudget` (Finagle / Envoy style) caps retries at a share of recent traffic, shared by
every strategy that uses it:

```csharp
var budget = new RetryBudget(retryRatio: 0.2, minRetriesPerSecond: 10, timeToLive: TimeSpan.FromSeconds(10));

new RetryStrategyOptions<HttpResponseMessage>
{
    Budget = budget,                                   // share one instance across pipelines
    OnBudgetExhausted = static args => { /* log */ return default; },
};
```

Each execution deposits one request; each retry must withdraw one. Over any `timeToLive`, retries stay
within `minRetriesPerSecond × timeToLive + retryRatio × requests`. When the budget is spent, the strategy
stops and returns the last outcome. Lock-free and allocation-free.

## Hedging

Hedging sends a second request when the first one is slow, and keeps whichever answers first:

```csharp
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddHedging(new HedgingStrategyOptions<HttpResponseMessage>
    {
        MaxHedgedAttempts = 2,                      // 1–10; at most 3 concurrent attempts
        Delay = TimeSpan.FromMilliseconds(500),     // start the next attempt after 500 ms without an answer
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => (int)r.StatusCode >= 500),
    })
    .AddTimeout(TimeSpan.FromSeconds(2))            // per attempt: strategies after hedging run per attempt
    .Build();
```

- The first outcome that `ShouldHandle` does not handle wins. A handled outcome starts the next attempt
  at once. When every attempt is handled, the primary's outcome is returned (as in Polly).
- `Delay = TimeSpan.Zero` starts every attempt at once (parallel mode). A negative delay (for example
  `Timeout.InfiniteTimeSpan`) starts the next attempt only after a handled outcome (fallback mode).
  `DelayGenerator` sets the delay per attempt.
- The losers are cancelled and awaited before the call returns, and their results are disposed
  (`HttpResponseMessage` included).
- Each attempt runs on its own copy of the `ResilienceContext`. When the call completes, the winner's
  properties are copied back to the caller's context; the losers' changes are dropped.
- `ActionGenerator` supplies a custom action per hedged attempt (for example, another endpoint).
  `args.Callback(args.ActionContext)` runs the rest of the pipeline again. Returning `null` skips the
  attempt. `OnHedging` is raised before each hedged attempt.

## Chaos engineering

Polly 8's Simmy strategies are ordinary strategies here too (`MintPlayer.Resilience.Simmy`), so they can
stay in a production pipeline behind a flag:

```csharp
builder
    .AddChaosLatency(new ChaosLatencyStrategyOptions
    {
        EnabledGenerator = _ => featureFlags.ChaosEnabled,   // synchronous
        InjectionRate = 0.05,
        Latency = TimeSpan.FromSeconds(2),
    })
    .AddChaosFault(0.02, () => new TimeoutException())
    .AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage>
    {
        InjectionRate = 0.01,
        OutcomeGenerator = new OutcomeGenerator<HttpResponseMessage>()
            .AddResult(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), weight: 80)
            .AddException<HttpRequestException>(weight: 20),
    });
```

- `AddChaosFault`, `AddChaosOutcome`, `AddChaosLatency` and `AddChaosBehavior` share `InjectionRate`
  (0–1), `InjectionRateGenerator`, `Enabled`, `EnabledGenerator` and `Randomizer`. Each has its own
  `On…Injected` event.
- A strategy that is disabled, or has a zero rate, costs one branch and allocates nothing. A call that is
  not injected allocates nothing either.

## Differences from Polly v8

- **Predicates and generators are synchronous.** `ShouldHandle` returns `bool`, `DelayGenerator` returns
  `TimeSpan?`, `TimeoutGenerator` returns `TimeSpan`. `PredicateBuilder` converts implicitly, as in Polly.
  `BreakDurationGenerator` returns `TimeSpan`. Events (`OnRetry`, `OnTimeout`, `OnFallback`,
  `OnOpened`, `OnHalfOpened`, `OnClosed`, `OnRejected`) and `FallbackAction` stay `ValueTask`-returning,
  and so does `RateLimiterStrategyOptions.RateLimiter`, because acquiring a lease may wait in a queue.
  - Hedging: `ShouldHandle` returns `bool` and `DelayGenerator` returns `TimeSpan`; `ActionGenerator`
    and `OnHedging` keep their shapes.
  - Chaos: `EnabledGenerator` returns `bool`, `InjectionRateGenerator` returns `double`,
    `LatencyGenerator` returns `TimeSpan`, `FaultGenerator` returns `Exception?` and `OutcomeGenerator`
    returns `Outcome<T>?`. `BehaviorGenerator` (an action) and the `On…Injected` events stay
    `ValueTask`-returning. `FaultGenerator` and `OutcomeGenerator<T>` convert implicitly, as in Polly.
- **Hedging details:** when `ActionGenerator` returns `null` in parallel mode, the strategy waits for a
  running attempt instead of asking again at once (Polly spins). A throwing `ShouldHandle` ends the
  hedging with its exception.
- **Chaos details:** a disabled strategy, or one with a zero rate, does not check the cancellation token
  and does not call the randomizer. Polly checks and calls both.
- **Rate limiter details:** `RateLimiterArguments` adds `CancellationToken`, which does not rent a
  context the way `Context` does, and `AddRateLimiter(RateLimiter)` calls the limiter directly.
- **Circuit breaker details:**
  - The health window is also cleared when the half-open probe is admitted, not only on close (a
    lock-free close cannot clear it atomically). Only the break-duration generator could see this, so
    a re-open after a failed probe reports the failure rate and count from when the circuit broke.
  - A manual isolation does not call the break-duration generator; `OnOpened` reports
    `TimeSpan.MaxValue`.
  - `RetryAfter` is never negative; a stalled half-open period reports zero.
  - An `OnHalfOpened` that throws fails the probe and re-opens the circuit; Polly leaves it half-open.
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
happy path allocates nothing. Hedging is the exception: it runs the rest of the pipeline once per attempt,
concurrently, on its own frames. Its per-attempt state is pooled, but the tasks it waits on are not.

## Compiler requirements

The source generator that ships in this package is built against Roslyn 5.9 and needs the .NET SDK 10.0.4xx
or later; an older compiler skips it (the declarative members are then missing).
