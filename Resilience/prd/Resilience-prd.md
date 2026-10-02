# PRD — MintPlayer.Resilience

A source-generated resilience library for .NET: retry, timeout, circuit breaker, fallback, hedging and
rate limiting. It covers what Polly v8 (`Polly.Core`) offers, but composes the pipeline at compile time,
so there is no per-strategy delegate hop and nothing is boxed per layer, and it is AOT-clean. A code fix
rewrites capturing call sites so they do not allocate.

**License: Apache-2.0** (repo standard). There is no maintenance fee and no EULA on the binaries.

Companion plan: [Resilience-plan.md](Resilience-plan.md).

**Status (2026-10-02, draft PR #190):** the runtime core, circuit breaker, limiters, hedging, chaos and
the source generator are built (plan M1–M3, M4 part A). The analyzers and code fixes, DI/registry,
telemetry, HTTP integration, the Testing package, benchmarks and the demo are not built yet. Sections
below mark such items *(planned, Mx)*; everything unmarked is in the code.

---

## 1. Why build this (and why here)

### 1.1 What actually changed with Polly — checked 2026-09-29

The premise "Polly changed its license" is **imprecise**. The facts, from primary sources:

- **The source license is unchanged.** The repo is still BSD-3-Clause
  ([LICENSE](https://github.com/App-vNext/Polly/blob/main/LICENSE)), and NuGet lists BSD-3 for
  `Polly.Core` 8.8.0 (2026-09-14).
- **The Open Source Maintenance Fee (OSMF) was adopted**
  ([announcement, 2026-07-14](https://thepollyproject.org/2026/07/14/polly-osmf-announcement.html)).
  It adds an EULA to the **maintainer-built binaries** (NuGet / GitHub releases):
  - US $20/month per organization with ≥ US $20k revenue from a product that uses Polly;
  - collected through GitHub Sponsors;
  - payment requests start **2026-11-16**.
- **Only future releases carry the EULA.** Earlier versions are not affected retroactively
  ([discussion #3183](https://github.com/App-vNext/Polly/discussions/3183)). **8.8.0 is the last plain
  BSD-3 release.**
- **The fee applies to direct dependencies only.** Consumers who get Polly transitively through
  `Microsoft.Extensions.Http.Resilience` do not pay. It is still unclear whether calling Polly APIs
  through a transitive reference counts as direct use.
- **Microsoft has not answered.**
  [dotnet/extensions#7719](https://github.com/dotnet/extensions/issues/7719) (post-OSMF strategy) is open
  and untriaged. `Microsoft.Extensions.(Http.)Resilience` exposes Polly types in its public API.
- **A community fork exists.** [Fences](https://github.com/BrighterCommand/Fences) (Brighter / Ian
  Cooper) is BSD-3, stable from 9.0.0 and API-compatible with Polly 8.7 (swap `using Polly;` →
  `using Paramore.Fences;`). It has about 65 stars.

### 1.2 Consequence for positioning

"A free Polly" already exists: it is Fences, or simply paying $20/month. **Licensing alone is not a
reason to build this.** It is worth building only if it is **measurably better**, and this repo has the
tooling for that:

- the minimal-code incremental generator framework (`MintPlayer.SourceGenerators.Tools`);
- the Assertions precedent: a generated fast path plus a runtime fallback, benchmarked in CI against a
  pinned last-free competitor version.

**Differentiators:**

| | Polly 8.8 | Fences 9 | **MintPlayer.Resilience** |
|---|---|---|---|
| License / fee | BSD-3 source, OSMF EULA on binaries from ~Nov 2026 | BSD-3 | Apache-2.0 |
| Pipeline composition | runtime, one delegate hop + state tuple per strategy | same (fork) | **compile time, one flat method** |
| Async state-machine boxes when the callback really suspends | one per layer | same | **one per pipeline, pooled** |
| Closure at call site (`ExecuteAsync(ct => Foo(id, ct))`) | allocates; docs say rewrite with static lambda + state by hand | same | allocates too, but **an analyzer + one-click code fix** does the rewrite (0 B after the fix) |
| Rejection (open circuit, rate limited, timeout) | throws (~5 µs, ~1.3 KB for open CB) | same | **`Outcome` result, no throw, when you use the Try API** |
| Native AOT | works, but boxes a `StateWrapper` per hop (24 B) | same | **no boxing; trimming/AOT clean by design** |
| Predicates | `Func<Args, ValueTask<bool>>` per outcome | same | compile-time `is`/switch, synchronous `bool` |

### 1.3 Honest performance expectations

This must not be sold like Assertions (25× / 30×). Polly v8 is already well optimized. Its published
numbers (`bench/…/results`, .NET 9):

- Empty pipeline: 28 ns, 0 B.
- Retry, no retries: 110 ns, 0 B.
- Closed circuit breaker: 138 ns, 0 B.
- Timeout: 148 ns, 0 B. The `CancellationTokenSource` is already pooled.
- Five-strategy pipeline: 766 ns, 40 B. The 40 B is the BCL rate-limiter lease.

Polly's "zero-alloc" claim only holds when all of these are true: a pooled context, a static lambda plus
state, a callback that **completes synchronously**, the happy path, no exception, and
`CancellationToken.None`.

**What we claim and will measure:**

1. **Sync happy path:** 1.5–3× faster (flattening removes ~15–35 ns per strategy hop). 0 B on both
   sides.
2. **Real async (the callback suspends):** Polly boxes one state machine per `async` layer (~1.3 KB
   measured on async hedging). We box **one** per pipeline, and zero with
   `PoolingAsyncValueTaskMethodBuilder`. **This is the headline win**, because it is the path production
   code actually takes.
3. **Closure call sites:** a capturing lambda allocates 56–104 B per call in *any* library. The caller
   builds the delegate before the call, so no library, and no interceptor, can avoid it (plan S3).
   - MPR0002 flags these call sites and offers a code fix that rewrites them to a static lambda + state
     tuple: 0 B.
   - The rewrite applies within the boundary S3 verified. Anything outside it is left untouched,
     because a naive rewrite changes results.
4. **Rejected calls:** Polly takes ~5 µs and ~1.3 KB for an open circuit. We target ≤ 250 ns and 0 B
   through the `Outcome` API. Rejection is exactly when a system is under stress.

**What we do not claim:** an order-of-magnitude end-to-end gain on I/O-bound workloads. A 2 ms HTTP call
dominates everything above. The win is lower GC pressure at high request rates, tail latency during
failures, and AOT.

### 1.4 Explicit non-goals

- **The legacy Polly v7 API** (`Policy.Handle<>().WaitAndRetryAsync`, `PolicyWrap`, `PolicyRegistry`).
  Not covered. The migration analyzer (§2.6) points at the v8-shaped equivalents.
- **A binary drop-in for Polly types.** We cannot plug into `Microsoft.Extensions.Http.Resilience`,
  because its public API is typed on Polly. We ship our own `HttpClient` integration instead (§2.5).
  Source-level migration is assisted by the analyzer and by near-identical option names.

---

## 2. API shape

### 2.1 Declarative, generated pipeline (the fast path)

```csharp
[ResiliencePipeline]
[Timeout(TimeoutMs = 10_000)]                             // outer: total budget
[Retry(MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, DelayMs = 200, UseJitter = true)]
[CircuitBreaker(FailureRatio = 0.5, SamplingDurationMs = 30_000, MinimumThroughput = 10, BreakDurationMs = 15_000)]
[Timeout(TimeoutMs = 2_000)]                              // inner: per attempt
public sealed partial class CatalogPipeline   // not `static`: it must be usable as a type argument (S6)
{
    // Optional hooks, bound by a hook attribute ([RetryWhen]) or by name
    // ([Retry(ShouldHandle = nameof(Transient))]). Predicates are synchronous and called directly.
    [RetryWhen] static bool Transient(Outcome<HttpResponseMessage> o) =>
        o.Exception is HttpRequestException || (o.Result is { } r && (int)r.StatusCode >= 500);
}

// Call sites. Callbacks return ValueTask<T>, as in Polly.
var r1 = await CatalogPipeline.ExecuteAsync(static async (s, ct) => await s.client.GetAsync($"/items/{s.id}", ct), (client, id), ct);
var r2 = await CatalogPipeline.ExecuteAsync(async ct => await client.GetAsync($"/items/{id}", ct), ct); // closure: MPR0002 + code fix → r1's form (planned, M5)
Outcome<HttpResponseMessage> o = await CatalogPipeline.TryExecuteAsync(...);                            // never throws on rejection
```

**Rules for the class** (anything else gets no generated code; MPR0004 will say why, *planned, M4 part B*):

- `partial`, not `static`, not generic, and every containing type partial and non-generic.
- All strategy attributes on **one** declaration. They run in declaration order, the first outermost
  (S1; no `Order =` property is needed). MPR0007 *(planned, M4 part B)* flags a split across partials.
- Durations are integer milliseconds (`…Ms`), because a `TimeSpan` is not a valid attribute argument. On
  a nullable duration (`MaxDelayMs`, `SlowCallDurationThresholdMs`, `LatencyThresholdMs`), **-1 (the
  default) means "not set"**. Defaults are Polly's, the same as the runtime options classes.
- A class-level `[RetryBudget]` gives the retries a pipeline-owned budget; `[Retry(Budget = nameof(…))]`
  names a shared one instead.

**Result type:**

- `[ResiliencePipeline<T>]` makes the pipeline typed, like `ResiliencePipeline<T>`.
- Without it, the type is inferred from a hook that names a concrete type (`Outcome<T>`, `…Arguments<T>`, a
  fallback's return type).
- Otherwise the pipeline is **generic**, like the non-generic `ResiliencePipeline`: `ExecuteAsync<TResult>`
  plus void overloads, and result-typed hooks must be generic methods. Inlined hooks see the concrete
  `TResult`, but the delegated strategies (breaker, limiters, chaos) are built on the non-generic builder
  and see `object`, so **a value-type result is boxed whenever one of their predicates runs** (Polly's
  behaviour).
- Hedging and outcome chaos need a typed pipeline.

**Static vs DI form:** the members are `static` when every bound hook and member is static and no
constructor takes parameters. Otherwise they are instance members (the DI form below).

**Hook binding:** every hook slot is a string property on the strategy attribute, named like the Polly
option (`ShouldHandle`, `OnRetry`, `FallbackAction`, …), which takes `nameof` of a method (or, for
`Budget`, `ManualControl`, `StateProvider`, `RateLimiter`, a field or property). The common slots also have
hook attributes on the method: `[RetryWhen]`, `[OnRetry]`, `[DelayGenerator]`, `[TimeoutGenerator]`,
`[OnTimeout]`, `[BreakWhen]`, `[OnOpened]`, `[OnClosed]`, `[OnHalfOpened]`, `[BreakDurationGenerator]`,
`[FallbackWhen]`, `[FallbackWith]`, `[OnFallback]`, `[HedgeWhen]`, `[OnHedging]`. A hook attribute binds to
the only strategy of its kind, or to the one whose `Name` it passes; a name on the strategy wins. Adapted
shapes are accepted: a predicate or fallback over the args or over `Outcome<T>`, `void` events, and a
fallback returning `T`, `Outcome<T>`, `ValueTask<T>` or `ValueTask<Outcome<T>>`.

**The generator emits** (one file, `ResiliencePipelines.g.cs`):

- **One pooled async method per class** (`__RunAsync`, generic over the callback, the result shape and
  a telemetry struct), on the same `ExecutionFrame<T>` as the runtime interpreter. Public members:
  `ExecuteAsync` and `TryExecuteAsync` (plain, state, context and context + state overloads) and sync
  `Execute`.
- **Retry, timeout and fallback are inlined** as nested `try`/loops in attribute order, as
  statement-for-statement ports of the runtime strategies, with hooks called directly.
- **Circuit breaker, limiters, adaptive limiter and chaos are driven through their runtime hooks** (each
  built once from a one-strategy pipeline). Their state is intrinsic and the breaker's event sequencing is
  subtle, so their parity with the runtime builder is by construction.
- **Hedging is not flattened:** the generated members forward to an equivalent runtime pipeline built
  once, with the same public surface.
- **Constants folded in:** retry count, backoff and delays (read from the snapshot instead when the
  pipeline is reloadable).
- **Runtime state in one snapshot** (`__Runtime`: clock, randomizer, CTS pool, the runtime strategies),
  read once per execution. `UseTimeProvider(time, randomizer?)` replaces it, which is how tests drive a
  fake clock.
- **Telemetry compiled out:** the method is generic over a telemetry struct and every member passes the
  no-op `NoTelemetry`. Enabled telemetry is *planned, M6* (S8).
- **`[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`** on the generated method (S2),
  unless `[ResiliencePipeline(PooledAsync = false)]`.
- **An invalid constant** (e.g. `MaxRetryAttempts = -1`) throws `ValidationException` on the first
  execution; MPR0004 *(planned, M4 part B)* moves this to compile time.

**Non-static (DI) form.** Real `OnRetry` / `OnFallback` hooks log through an injected `ILogger` (plan S7).
A declarative pipeline can therefore also be a non-static `partial class` with a constructor. Its hooks
are instance methods and can use injected services; strategy state (a breaker's health) lives on the
instance. The generated static `Create(IServiceProvider)` resolves the widest constructor. Registration as
a singleton by `AddResiliencePipeline<T>()` is *planned, M6*.

```csharp
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
```

### 2.2 Runtime builder (dynamic / config-driven)

For pipelines only known at runtime (from config, or with reload), there is a builder. **Type, option,
extension and exception names mirror Polly v8 one-for-one** (plan S7), so migrating is mostly a
namespace swap:

```csharp
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddTimeout(TimeSpan.FromSeconds(10))
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage> { MaxRetryAttempts = 3, ShouldHandle = ... })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage> { ... })
    .Build();
```

- **Execution is a single async interpreter method** (`PipelineCore<T>.RunAsync`, one pooled box) over
  strategies that expose hooks (`EnterAsync` / `ExitAsync`, synchronous on the happy path), not a chain
  of async layers. S1 measured a struct-nested chain: it was as fast as flat code but boxed once per layer
  when the callback suspended, so the struct-nested shape was dropped.
- **Hedging runs inside the same interpreter** as a *forking* strategy: it runs the inner remainder of the
  pipeline itself, once per attempt, on pooled inner runners. The allocations that remain (a `Task` per
  attempt, `WhenAny`, the delay timer) are intrinsic.
- **The generated and runtime paths share the same strategy state types** (breaker controller, CTS pool,
  limiters): the generator drives the runtime breaker, limiters and chaos strategies directly.
- **The non-generic `ResiliencePipeline`** builds one typed core per result type on first use. Its options
  (`RetryStrategyOptions : RetryStrategyOptions<object>`, …) are adapted, which **boxes a value-type result
  only when a user delegate runs**, as in Polly.
- **Deliberate differences from Polly:**
  - predicates (`ShouldHandle`), delay, break-duration, timeout and chaos generators are synchronous.
    `PredicateBuilder` converts implicitly, and `PredicateBuilder.Handle<TException>()` matches a
    rejection by type without creating the exception;
  - events, `FallbackAction`, hedging's `ActionGenerator`, chaos `BehaviorGenerator` and the
    `RateLimiter` delegate stay `ValueTask`-returning, as in Polly (acquisition can queue);
  - the returned `ValueTask` is pooled; `UsePooledAsync(false)` on the builder opts out. MPR0006
    *(planned, M4 part B)* flags misuse;
  - `TryExecuteAsync` returns an `Outcome` on rejection, without an exception. It takes the place of
    Polly's `ExecuteOutcomeAsync`, which is not provided;
  - options are validated when they are added (Polly's ranges, `ValidationException`), by hand rather
    than through DataAnnotations reflection, which is not AOT-safe;
  - `Retry-After` awareness is not in the retry strategy yet *(planned, M7, through `DelayGenerator`)*.

### 2.3 Strategies (v1 surface)

| Strategy | Notes |
|---|---|
| Retry | `DelayBackoffType` `Constant` / `Linear` / `Exponential`, with `UseJitter` (Polly's decorrelated jitter for exponential), `MaxDelay`, `DelayGenerator`, `OnRetry`. `Retry-After` awareness (reusing `MintPlayer.Http` `GetRetryAfter`) is *planned, M7*. Value-type results are not disposed (no boxing). |
| Timeout | Pooled CTS (`TryReset`), `TimeProvider`, `TimeoutGenerator`, `OnTimeout`. |
| Circuit breaker | Failure ratio over a sliding window, minimum throughput, break duration (synchronous `BreakDurationGenerator`), half-open probing, `CircuitBreakerManualControl`, `CircuitBreakerStateProvider`. The state controller is lock-free (S4): a CAS on packed state plus per-core striped health counters, 12–32× faster than a lock under contention. Events are sequenced per transition, as Polly runs them one at a time. Deviations: `RetryAfter` is clamped at ≥ 0; an `OnHalfOpened` that throws counts as a failed probe and re-opens the circuit (Polly stays half-open); the generator's failure figures are those at Closed → Open, reused on a re-open from half-open. |
| Fallback | A `FallbackAction` delegate over the outcome; on the runtime builder it is typed-only, as in Polly. A generated fallback may return `T`, `Outcome<T>` or their `ValueTask`. |
| Hedging | `MaxHedgedAttempts`, `Delay` (0 = parallel, negative = fallback mode), `DelayGenerator`, `ActionGenerator`, `OnHedging`; typed pipelines only. Execution contexts and attempts are pooled; losers are cancelled and awaited, and only the winner's context properties are merged back. |
| Rate / concurrency limiter | `AddRateLimiter` / `AddConcurrencyLimiter` wrap `System.Threading.RateLimiting` (a package reference, 10.0.0; the lease cost is intrinsic). Lease-free limiters reject at 0 B: `AddFixedWindowLimiter`, `AddSlidingWindowLimiter`, `AddNativeConcurrencyLimiter` (no queue). All reject with `RejectionKind.RateLimited`. |
| **Retry budget** *(beyond Polly)* | `RetryBudget(retryRatio 0.2, minRetriesPerSecond 10, timeToLive 10 s)`: retries capped as a percentage of recent traffic (Finagle/Envoy style), shared by every retry that references the same instance (`RetryStrategyOptions.Budget`, `OnBudgetExhausted`). Stops retry storms that a per-call `MaxRetryAttempts` cannot prevent. |
| **Adaptive concurrency limit** *(beyond Polly)* | `AddAdaptiveConcurrencyLimiter`, `AdaptiveConcurrencyAlgorithm.Aimd` / `Gradient` (default): the limit adjusts itself from observed latency and drops, Netflix concurrency-limits style, with deviations found by simulation: one backoff per generation (Netflix's per-sample backoff collapsed on a burst), and the gradient compares against the no-load (minimum) latency, not Gradient2's long-term average. Known limitation: a permanent baseline rise above `Tolerance`, or a start deep in overload, pins the gradient low. |
| **Slow-call circuit breaking** *(beyond Polly)* | `SlowCallDurationThreshold` (null = off) and `SlowCallRatio` (default 1.0): the circuit breaker can also trip on a slow-call ratio, not just failures (resilience4j). |
| Chaos (fault injection) | `AddChaosFault`, `AddChaosOutcome`, `AddChaosLatency` and `AddChaosBehavior` (namespaces `MintPlayer.Resilience.Simmy.*`, as in Polly), with `InjectionRate`, `Enabled`, `InjectionRateGenerator` and `EnabledGenerator`. They are ordinary strategies in the core package, so they can go in a production pipeline behind a flag, as Simmy does in Polly 8. A disabled chaos strategy (or rate 0 without generators) costs one branch, in both the runtime and the generated pipeline; not injecting allocates 0 B. |

### 2.4 Context, outcome, DI, telemetry

- **`ResilienceContext`**: pooled (`ResilienceContextPool.Shared`), with typed `ResiliencePropertyKey<T>`
  properties. The argument structs resolve `Context` lazily: an execution rents a context only when a
  delegate reads it, so the overloads without a context normally never touch the pool (this saves the
  ~30 ns measured on Polly). Hedging always materializes one, since each attempt gets its own copy.
- **`Outcome<T>`**: a readonly struct: `IsSuccess`, `IsRejected`, `Rejection` (`RejectionKind : byte`
  — `None`, `CircuitOpen`, `CircuitIsolated`, `RateLimited`, `Timeout`), `RetryAfter` (circuit and
  limiter rejections only; isolated rejections carry none), `Result`, `Exception`, `GetResultOrThrow()`,
  `ThrowIfException()`. It is created with Polly's static factories (`Outcome.FromResult`,
  `Outcome.FromException`, and their `…AsValueTask` forms). A rejection carries **no exception
  reference** (only its cause, which becomes the `InnerException`). **`Exception` on a rejected outcome
  creates a fresh exception on every read**; test `Rejection` to avoid the allocation.
- **Never cache or reuse a thrown exception** (plan S5): each throw rewrites the instance's stack, the
  `Data` dictionary leaks between callers, and concurrent throws produce mixed or foreign stack traces.
- **`ExecuteAsync` throws a fresh exception per rejection:** the abstract
  `ResilienceRejectedException { Kind, RetryAfter }` → `BrokenCircuitException` (→
  `IsolatedCircuitException`) / `RateLimiterRejectedException` / `TimeoutRejectedException`. The leaf
  names are Polly's, per S7; Polly's `ExecutionRejectedException` base is not mirrored.
- **An analyzer code fix** *(planned, M4 part B)* rewrites `o.Exception is BrokenCircuitException` into
  `o.Rejection == RejectionKind.CircuitOpen`.
- **DI** *(planned, M6)*: `services.AddResiliencePipeline<CatalogPipeline>()` for generated pipelines,
  and `AddResiliencePipeline(key, builder => …)` for runtime ones. Both add a keyed registry. The seam is
  built: every generated class implements `IGeneratedResiliencePipeline<TSelf>` (`PipelineName`,
  `IsInstancePipeline`, `Create(IServiceProvider)`, `UseTimeProvider`), all static abstract members.
- **Reload** (plan S6): `[ResiliencePipeline(Reloadable = true)]`.
  - **Options class:** the generator emits a `CatalogPipelineOptions` class, with one property per
    strategy, named by the attribute's `Name`, else the strategy kind (`Timeout2` for a second unnamed
    one). Each property is a nested `<Kind>Section` whose values use the runtime option names and types
    (`Delay` as a `TimeSpan`, not `DelayMs`). Its initial values are the attribute values.
  - **Apply:** the generated `TryApply(options, out error)` (and `IReloadableResiliencePipeline<TSelf,
    TOptions>`) validates with the builder's rules. **Bad values return `false` and the previous snapshot
    stays live.** Two racing applies are serialized by a lock.
  - **Snapshots:** each execution reads one immutable snapshot at entry (`Volatile.Read`), so in-flight
    calls keep their values.
  - **Strategy state on reload:** a delegated strategy whose section is unchanged keeps its instance, so
    **a breaker's health survives a reload that does not touch it**. A changed section recreates that
    strategy and resets its state, because the controller takes its settings at construction. A hedging
    pipeline is rebuilt on every reload. Polly rebuilds a closed breaker in every case.
  - **Section:** `DefaultSectionPath` is `"Resilience:<Name>"`, where the name is
    `[ResiliencePipeline(Name = …)]`, else the class name. Binding it from configuration, or from a
    section passed to `AddResiliencePipeline<T>("section")`, is *planned, M6*: binding listens to the
    change tokens and calls `IOptionsFactory.Create` inside try/catch, because plain
    `IOptionsMonitor.OnChange` throws from `Reload()` before listeners run.
  - **Registration** *(planned, M6)*: `AddResiliencePipeline<T>()` is idempotent and goes through the
    static abstract seam above.
- **Telemetry** *(planned, M6; plan S8)*: an OpenTelemetry `Meter` / `ActivitySource` plus `ILogger`
  source-generated `LoggerMessage`.
  - **Tags** are pre-bound per pipeline and strategy instance. Allocation is **0 B/op with telemetry on**.
  - **Default names:**
    - meter and `ActivitySource` `MintPlayer.Resilience`;
    - instruments `resilience.strategy.events`, `resilience.strategy.attempt.duration` and
      `resilience.pipeline.duration`;
    - durations in seconds, with explicit bucket boundaries;
    - `error.type` instead of `exception.type`;
    - all other tag keys as in Polly.
  - **Opt-in `PollyCompatible` naming scheme** for existing dashboards: meter `Polly`, Polly's
    instrument names, milliseconds, `exception.type`, and logger category `Polly`.
  - **Log events** get distinct event ids. Happy-path events are logged at **Debug**; Polly writes two
    Information lines per successful call.
  - **When telemetry is off:**
    - generated pipelines compile it out (0 ns);
    - the runtime interpreter is generic over a telemetry struct, with a no-op implementation that the
      JIT removes.

### 2.5 HttpClient integration (`MintPlayer.Resilience.Http`) *(planned, M7)*

**Decision: the pipeline lives in a `DelegatingHandler`, not in extension methods on `HttpClient`.**

```csharp
// DI / IHttpClientFactory: the primary path
services.AddHttpClient<CatalogClient>()
        .AddResilienceHandler<CatalogPipeline>();          // generated pipeline
services.AddHttpClient("github")
        .AddStandardResilienceHandler(o => o.Retry.MaxRetryAttempts = 5);

// Without DI: the same handler type, composed by hand
var http = new HttpClient(new ResilienceHandler<CatalogPipeline>(new SocketsHttpHandler()));
```

**Why a handler and not generated `HttpClient` extension methods** (e.g.
`client.GetAsync<CatalogPipeline>(url)`):

1. **It can't be forgotten.** A handler covers every request made through the client, including requests
   from third-party SDKs that accept an `HttpClient`. Extension methods are opt-in at each call site, so
   one plain `client.GetAsync` silently skips resilience.
2. **No API surface to duplicate.** An extension approach means mirroring `GetAsync` / `PostAsync` /
   `SendAsync` / `GetFromJsonAsync` × overloads, and keeping that in step with the BCL.
3. **Request replay lives in one place.** Below `HttpClient`, a handler may re-send the same
   `HttpRequestMessage`. **Microsoft's standard handler does exactly that** (plan S7); only its hedging
   handler snapshots the request. For parity we do the same:
   - retry re-sends the same message;
   - hedging snapshots it;
   - non-replayable content (a forward-only `StreamContent`) is detected, and the pipeline fails fast
     with a clear error instead of silently sending an empty body (Microsoft's hedging throws there
     too).

   Extension methods would have to reimplement this at every call site.
4. **No closures anyway.** Inside the handler the callback is always `static (s, ct) => s.inner.SendAsync(s.req, ct)`
   with `(request, base)` passed as state. So the interceptor machinery is not needed on this path, and
   it is 0 B by construction.
5. **It fits the factory's model.** Handler lifetime, `SocketsHttpHandler` rotation, keyed/named
   clients, and `HttpClient` logging ordering all come for free.

**Per-request override:** `request.SetResiliencePipeline<OtherPipeline>()` (via
`HttpRequestMessage.Options`) or `request.DisableResilience()`, for the odd non-idempotent POST on an
otherwise-retrying client.

**Standard preset: decided to copy Microsoft's defaults exactly.** `AddStandardResilienceHandler`
matches `Microsoft.Extensions.Http.Resilience` in both order and values:

| Order | Strategy | Default | Handles |
|---|---|---|---|
| 1 | Rate limiter | `ConcurrencyLimiter`, 1000 permits, queue 0 | n/a |
| 2 | Total timeout | 30 s | n/a |
| 3 | Retry | `MaxRetryAttempts` 3 (= 4 attempts), exponential, `Delay` 2 s, `UseJitter` true, `MaxDelay` null, `ShouldRetryAfterHeader` true | status ≥ 500, 408, 429; `HttpRequestException`; `TimeoutRejectedException`; connection-timeout OCE. All HTTP methods, POST included. |
| 4 | Circuit breaker | ratio 0.1, min throughput 100, sampling 30 s, break 5 s | ≥ 500 / 408 / 429, `HttpRequestException`, `TimeoutRejectedException` (not the connection-timeout OCE) |
| 5 | Attempt timeout | 10 s | n/a |

- **Validation:** attempt timeout ≤ total timeout, and sampling ≥ 2 × attempt timeout.
- **The handler sets `HttpClient.Timeout = Infinite`.**
- **`Retry-After` handling:**
  - it replaces the backoff delay;
  - it is not capped by `MaxDelay`;
  - a past date means 0.
- **Hedging preset:**
  - outer handler: total timeout 30 s, hedging with `MaxHedgedAttempts` 1 (range 1–10) and `Delay` 2 s;
  - inner handler, per authority: rate limiter, the same circuit breaker, attempt timeout 10 s;
  - the hedging predicate also handles `BrokenCircuitException`.
- **Source:** verified against the dotnet/extensions source (398edf6) and Polly 8.8.0. The file and
  line references are in `Resilience/spikes/S7/RESULTS.md`.

`AddStandardHedgingHandler` mirrors Microsoft's hedging preset the same way. The reason: a team switching
over should see identical production behaviour, and "same defaults as Microsoft" is one sentence of
documentation instead of a table of differences. A parity test pins these values.

### 2.6 Analyzers

None of the rules is implemented yet. The ids MPR0004–MPR0007 are reserved in the generator project
(`Diagnostics/DiagnosticIds.cs`); the generator itself reports nothing and skips a class it cannot
generate.

| Id | Rule | Status |
|---|---|---|
| MPR0001 | Polly v8 `ResiliencePipelineBuilder` usage detected. Code fix converts it to a `[ResiliencePipeline]` class when all options are constant. | planned, M8 |
| MPR0002 | Capturing lambda passed to any pipeline's `Execute*` (allocates 56–104 B/call). The code fix rewrites it to a `static` lambda + state tuple, within the boundary verified in S3. | planned, M5 |
| MPR0003 | Non-idempotent-looking callback (HTTP POST without idempotency key) under retry/hedging. Warning only. | planned, M8 |
| MPR0004 | Invalid declarative pipeline. Covers every reason the generator skips a class (not partial / static / generic, a non-partial or generic containing type, a hook or member name that does not exist, a fallback without `FallbackAction`, outcome/behavior/fault chaos without its generator, a typed-only strategy (hedging, outcome chaos) in a generic pipeline), plus a mismatched hook signature, an ambiguous hook attribute (left unbound today), and invalid values: an inner timeout ≥ an outer timeout, and constants outside the builder's ranges. | planned, M4 part B |
| MPR0005 | `ExecuteAsync` result not awaited. | planned, M4 part B |
| MPR0006 | Pooled `ValueTask` misuse: awaited twice, `.Result` without an await, or passed to `WhenAll`/`WhenAny` without `.AsTask()` (error; see plan S2). | planned, M4 part B |
| MPR0007 | Strategy attributes split across `partial` declarations: their order would be undefined (error; see plan S1). | planned, M4 part B |
| MPR0008 (info) | The S5 rejection rule (§2.4): `o.Exception is BrokenCircuitException` (and the other rejection types), with a code fix to `o.Rejection == RejectionKind.CircuitOpen`. | planned, M4 part B |

Code fixes need `Microsoft.CodeAnalysis.Workspaces`, which a generator must not reference (RS1038). They
go in a separate code-fix assembly, packed next to the generator.

---

## 3. Packages & repo layout

```
Resilience/
  MintPlayer.Resilience/                      # runtime + generator bundled under analyzers/dotnet/roslyn5.9/cs
  MintPlayer.Resilience.SourceGenerator/      # IsPackable=false, packed into the core (Assertions pattern)
  MintPlayer.Resilience.Tests/                # runtime tests; Polly.Core [8.8.0] as a test-only parity reference
  MintPlayer.Resilience.SourceGenerator.Tests/ # Samples/ (compile check), snapshot, incrementality, parity, allocation
  spikes/S1…S8/                               # throwaway, deleted before the PR is finalized
  prd/Resilience-prd.md, prd/Resilience-plan.md
  # planned:
  (code-fix assembly)                         # M4 part B / M5: CodeFixProviders + Workspaces, packed next to the generator
  MintPlayer.Resilience.Http/                 # M7: IHttpClientBuilder integration
  MintPlayer.Resilience.Testing/              # M8: pipeline descriptors, fake TimeProvider helpers, chaos assertions
  MintPlayer.Resilience.Benchmarks/           # M9: vs Polly.Core pinned [8.8.0] (last plain BSD-3)
  MintPlayer.Resilience.Demo/                 # M9: comprehensive demo (name not final)
```

- **Target frameworks:** net10.0; net11.0. `IsAotCompatible`. Version 11.0.0.
- **Runtime dependency:** `System.Threading.RateLimiting` 10.0.0, a package reference because it is not
  part of `Microsoft.NETCore.App`.
- **Namespaces mirror Polly's:** `MintPlayer.Resilience` plus `.Retry`, `.Timeout`, `.Fallback`,
  `.CircuitBreaker`, `.Hedging`, `.RateLimiting`, `.Simmy[.Fault|.Outcomes|.Latency|.Behavior]`. The
  types generated code needs are public but `[EditorBrowsable(Never)]` in `.Pipeline`. Code in a namespace
  under `MintPlayer.Resilience` sees `Timeout`, `Retry`, … as those sub-namespaces.
- **Generator:** netstandard2.0, Roslyn 5.9, built on `MintPlayer.SourceGenerators.Tools`
  (`IncrementalGenerator`, `Producer`, `[GenerateEquality]` models, one hint name
  `ResiliencePipelines.g.cs`). The `Microsoft.CodeAnalysis` meta-package is removed, since it brings
  Workspaces (RS1038). The attributes live in the core package (`Declarative/`).
- **Packing:** test, demo and benchmark projects set `<IsPackable>false</IsPackable>`. Packing the core
  warns NU5118 ×3, exactly as MintPlayer.Assertions does; the payload is correct.
- **Tests:** xUnit plus MintPlayer.Assertions, `FakeTimeProvider`; stress tests carry
  `[Trait("Category","Stress")]`.
- **Solution:** the "Resilience" solution folder is in `MintPlayer.Dotnet.Tools.sln`.
- **CI** *(planned, M9)*: `.github/workflows/resilience-benchmark.yml` (allocation ceilings asserted, as
  in `assertions-benchmark.yml`).

---

## 4. Success criteria

Measured against Polly.Core **8.8.0** under identical workloads, with a fairness gate: both libraries
must retry, open the circuit and time out on the same scripted fault sequence before any number counts.

1. **Sync happy path**, 5 strategies (timeout, retry, CB, timeout, fallback — no limiter): ≤ 0.5× Polly's
   mean, 0 B.
2. **Async-suspending callback** (`await Task.Yield()`), same pipeline: allocations ≤ 1 pooled box. The
   target is ≤ 10 % of Polly's bytes. Polly's number is measured in S1.
3. **Closure call sites:** the MPR0002 code fix turns every S3 in-boundary shape into a 0 B call, with
   results identical to the original closure (tests on net10 + net11).
4. **Open-circuit rejection** via `TryExecuteAsync`: ≤ 250 ns, 0 B. Polly `ExecuteAsync`: ~5.1 µs /
   1.3 KB.
5. **Circuit breaker under contention** (16 threads, closed): throughput ≥ Polly's (lock-based).
6. **Native AOT** publish of a sample app: zero trim/AOT warnings.
7. **Behavioural parity suite**: the same scripted fault/timing scenarios (with a fake `TimeProvider`)
   produce the same attempt counts, delays and state transitions as Polly with equivalent options.
8. **Generator is incremental**: no output step re-runs on an unrelated edit (the repo's
   `SourceGenerators.Testing` harness).

## 5. Risks & open questions

- **Competitive risk.** If Microsoft forks or replaces Polly inside `dotnet/extensions`, the ecosystem
  consolidates there. Mitigation: the differentiators in §1.2 hold regardless. Track
  dotnet/extensions#7719.
- **Interceptors are dropped** (plan S3). They swap only the call target, and the closure is allocated
  before the call.
- **Closure removal is the MPR0002 code fix** instead, limited to S3's verified boundary:
  - it applies to a non-static lambda capturing only locals, value parameters or class `this`;
  - nothing it captures may be written anywhere in the enclosing member;
  - no nested use of the captured state, no `base.` access, and no calls to non-static local functions.
- **Compiler floor:** a generator built against Roslyn 5.9 needs **SDK ≥ 10.0.4xx**. It fails with
  CS9057 on 10.0.1xx. Documented in the package README ("Compiler requirements").
- **Pooling async builder hazards.** A `ValueTask` from a pooled builder must not be awaited twice. User
  code that stores and re-awaits the task would break. This is the reason for the opt-out (S2):
  `UsePooledAsync(false)` on the builder, `[ResiliencePipeline(PooledAsync = false)]` on a generated
  pipeline. MPR0006 *(planned, M4 part B)* flags misuse.
- **Attribute order as semantics. Resolved by S1:** within one declaration `GetAttributes()` returns
  source order, so no `Order =` property exists. Across `partial` declarations it is undefined; MPR0007
  *(planned, M4 part B)* makes that an error.
- **Open question (owner, unanswered): an implicit conversion `T → Outcome<T>`** (`Outcome<int> o = 42;`)
  in addition to Polly's static factories (`Outcome.FromResult`, …). It would shorten fallback actions
  and outcome generators. `.ToOutcome()` extension methods were rejected: they pollute IntelliSense on
  every type, and S7 mirrors Polly's names.
- **Behaviour parity.** Circuit-breaker window arithmetic and jitter formulas must match Polly closely
  enough that migrations don't change production behaviour (the parity suite in §4.7).
- **Owner decisions (2026-09-29):**
  1. **v1 ships the full feature set, and more**: every strategy in §2.3 including chaos and the
     beyond-Polly strategies, plus DI/registry/reload, telemetry, HTTP, Testing and analyzers. Nothing
     is deferred to a later version. Tracked in #189.
  2. The package name is **`MintPlayer.Resilience`**.
  3. The standard HTTP preset **copies Microsoft's defaults exactly** (delegated to Claude; rationale in
     §2.5).
  4. HTTP integration is a **`DelegatingHandler` via `IHttpClientBuilder`**, and the same handler is
     usable without DI. No `HttpClient` extension methods (§2.5).
  5. The target frameworks are **net10.0;net11.0 only**. There is no netstandard2.0 / .NET Framework
     support; those users stay on Polly or Fences.
  6. **No Polly v7 compatibility facade**, only a v8-shaped API (§1.4).
  7. Packages start at **11.0.0**, aligned with the .NET major version like MintPlayer.Assertions.
- **Owner decisions (added during implementation):**
  1. Test, demo and benchmark projects set `<IsPackable>false</IsPackable>`. More than one test project
     is fine.
  2. M9 ships a **comprehensive demo** that showcases every spike finding (S1–S8), using the real
     library rather than the spike code, and every feature.
  3. **Documentation stays up to date**: the package READMEs, the package list in the repo README, and
     this PRD and the plan.
