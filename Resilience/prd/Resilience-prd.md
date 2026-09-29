# PRD — MintPlayer.Resilience

A source-generated resilience library for .NET: retry, timeout, circuit breaker, fallback, hedging and
rate limiting. It covers what Polly v8 (`Polly.Core`) offers, but composes the pipeline at compile time,
so there is no per-strategy delegate hop and nothing is boxed per layer, it is AOT-clean, and call sites
with closures do not allocate.

**License: Apache-2.0** (repo standard). There is no maintenance fee and no EULA on the binaries.

Companion plan: [Resilience-plan.md](Resilience-plan.md).

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
| Closure at call site (`ExecuteAsync(ct => Foo(id, ct))`) | allocates; docs say rewrite with static lambda + state by hand | same | **removed by interceptor** |
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
3. **Closure call sites:** Polly allocates 64–100 B per call. We allocate 0 B, with no change to how
   users write the call.
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
[Timeout(Seconds = 10)]                                   // outer: total budget
[Retry(MaxAttempts = 3, Backoff = Backoff.Exponential, DelayMs = 200, UseJitter = true)]
[CircuitBreaker(FailureRatio = 0.5, SamplingSeconds = 30, MinimumThroughput = 10, BreakSeconds = 15)]
[Timeout(Seconds = 2)]                                    // inner: per attempt
public static partial class CatalogPipeline
{
    // Optional predicate hooks, discovered by convention/attribute. They are synchronous, and the
    // generator inlines them.
    [RetryWhen] static bool Transient(Outcome<HttpResponseMessage> o) =>
        o.Exception is HttpRequestException || (o.Result is { } r && (int)r.StatusCode >= 500);
}

// Call sites
var r1 = await CatalogPipeline.ExecuteAsync(static (id, ct) => client.GetAsync($"/items/{id}", ct), id, ct);
var r2 = await CatalogPipeline.ExecuteAsync(ct => client.GetAsync($"/items/{id}", ct), ct); // closure → intercepted, 0 B
Outcome<HttpResponseMessage> o = await CatalogPipeline.TryExecuteAsync(...);                // never throws on rejection
```

**The generator emits:**

- **One flat `async ValueTask<T>` method per result type**, with the strategies inlined as nested
  `try`/loops in attribute order.
- **Constants folded in:** retry count, backoff and delays.
- **Shared runtime state only where it is intrinsic:**
  - circuit-breaker state as a `static readonly` controller;
  - limiter instances;
  - the pooled timeout CTS.
- **Telemetry compiled out** unless the pipeline is registered with telemetry enabled (see S8).
- **`[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`** on the generated method (see
  S2).

### 2.2 Runtime builder (dynamic / config-driven)

For pipelines only known at runtime (from config, or with reload), there is a builder with Polly-shaped
names:

```csharp
var pipeline = ResiliencePipeline.For<HttpResponseMessage>()
    .AddTimeout(TimeSpan.FromSeconds(10))
    .AddRetry(new RetryOptions<HttpResponseMessage> { MaxAttempts = 3, ShouldHandle = ... })
    .AddCircuitBreaker(new CircuitBreakerOptions { ... })
    .Build();
```

- **Composition is generic-struct nesting**, e.g. `Pipeline<T, Timeout<Retry<CircuitBreaker<Terminal>>>>`.
  The JIT specializes the whole chain, so the builder path also avoids delegate hops. The generated and
  runtime paths share the same strategy structs.
- **Fallback:** where the generic chain cannot be expressed (fully dynamic strategy lists), use a classic
  array-of-components pipeline, like the Assertions reflection fallback. This is decided in S1.

### 2.3 Strategies (v1 surface)

| Strategy | Notes |
|---|---|
| Retry | Constant / linear / exponential / decorrelated-jitter backoff, `MaxDelay`, `DelayGenerator`, `OnRetry`, `Retry-After` aware (reuse `MintPlayer.Http` `GetRetryAfter`). |
| Timeout | Pooled CTS (`TryReset`), `TimeProvider`, `OnTimeout`. |
| Circuit breaker | Failure ratio over a sliding window, minimum throughput, break duration (generator), half-open probing, manual control, state provider. The state controller is lock-free if S4 passes. |
| Fallback | Value, or a delegate over the outcome. |
| Hedging | Max hedged attempts, delay, action generator; pooled execution contexts. |
| Rate / concurrency limiter | Wraps `System.Threading.RateLimiting` (the lease cost is intrinsic). |
| **Retry budget** *(beyond Polly)* | Retries capped as a percentage of recent traffic (Finagle/Envoy style), shared across pipeline instances. Stops retry storms that a per-call `MaxAttempts` cannot prevent. |
| **Adaptive concurrency limit** *(beyond Polly)* | The limit adjusts itself from observed latency (AIMD / gradient, Netflix concurrency-limits style) instead of a fixed number. |
| **Slow-call circuit breaking** *(beyond Polly)* | The circuit breaker can also trip on a slow-call ratio over a duration threshold, not just failures (resilience4j). |
| Chaos (fault injection) | `Fault`, `Outcome`, `Latency` and `Behavior` injection, with `InjectionRate` and an `Enabled` generator. They are ordinary strategies in the core package, so they can go in a production pipeline behind a flag, as Simmy does in Polly 8. In generated pipelines a disabled chaos strategy compiles to one branch. |

### 2.4 Context, outcome, DI, telemetry

- **`ResilienceContext`**: pooled, with typed `ResiliencePropertyKey<T>` properties, only when the
  caller asks for it. The generated overloads without a context never touch the pool (this saves the
  ~30 ns measured on Polly).
- **`Outcome<T>`**: a readonly struct. Rejections carry a cached, stackless exception instance, so there
  is no `ExceptionDispatchInfo.Capture` on our own rejections.
- **DI**: `services.AddResiliencePipeline<CatalogPipeline>()` for generated pipelines, and
  `AddResiliencePipeline(key, builder => …)` for runtime ones. It adds a keyed registry and reload via
  `IOptionsMonitor` (see S6).
- **Telemetry**: an OpenTelemetry `Meter` / `ActivitySource` plus `ILogger` source-generated
  `LoggerMessage`. Tags are pre-bound per strategy instance.

### 2.5 HttpClient integration (`MintPlayer.Resilience.Http`)

**Decision: the pipeline lives in a `DelegatingHandler`, not in extension methods on `HttpClient`.**

```csharp
// DI / IHttpClientFactory: the primary path
services.AddHttpClient<CatalogClient>()
        .AddResilienceHandler<CatalogPipeline>();          // generated pipeline
services.AddHttpClient("github")
        .AddStandardResilienceHandler(o => o.Retry.MaxAttempts = 5);

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
3. **Retries need to rebuild the request.** An `HttpRequestMessage` cannot be sent twice. The handler
   clones the request and buffers the content per attempt in one place (the same approach as
   Microsoft's handler). Extension methods would have to reimplement that everywhere.
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

| Order | Strategy | Default |
|---|---|---|
| 1 | Rate limiter | concurrency 1000, queue 0 |
| 2 | Total timeout | 30 s |
| 3 | Retry | 3 attempts, exponential, 2 s base, jitter, handles 408/429/5xx + `HttpRequestException` + attempt timeout, honours `Retry-After` |
| 4 | Circuit breaker | failure ratio 10 %, min throughput 100, sampling 30 s, break 5 s |
| 5 | Attempt timeout | 10 s |

`AddStandardHedgingHandler` mirrors Microsoft's hedging preset the same way. The reason: a team switching
over should see identical production behaviour, and "same defaults as Microsoft" is one sentence of
documentation instead of a table of differences. The values above are from memory. S7 verifies them
against the Microsoft source, and a parity test pins them.

### 2.6 Analyzers

| Id | Rule |
|---|---|
| MPR0001 | Polly v8 `ResiliencePipelineBuilder` usage detected. Code fix converts it to a `[ResiliencePipeline]` class when all options are constant. |
| MPR0002 | Capturing lambda passed to a runtime (non-generated) pipeline. Suggests the generated pipeline or a `static` lambda + state. |
| MPR0003 | Non-idempotent-looking callback (HTTP POST without idempotency key) under retry/hedging. Warning only. |
| MPR0004 | Invalid attribute combination (e.g. inner timeout ≥ outer timeout, retry outside a total timeout of 0). |
| MPR0005 | `ExecuteAsync` result not awaited. |

---

## 3. Packages & repo layout

```
Resilience/
  MintPlayer.Resilience/                      # runtime + generator bundled under analyzers/dotnet/roslyn5.9/cs
  MintPlayer.Resilience.SourceGenerator/      # IsPackable=false, packed into the core (Assertions pattern)
  MintPlayer.Resilience.Http/                 # IHttpClientBuilder integration
  MintPlayer.Resilience.Testing/              # pipeline descriptors, fake TimeProvider helpers, chaos hooks
  MintPlayer.Resilience.Tests/
  MintPlayer.Resilience.SourceGenerator.Tests/
  MintPlayer.Resilience.Benchmarks/           # vs Polly.Core pinned [8.8.0] (last plain BSD-3)
  prd/Resilience-prd.md, prd/Resilience-plan.md
```

- **Target frameworks:** net10.0; net11.0. `IsAotCompatible`.
- **Generator:** netstandard2.0, Roslyn 5.9, built on `MintPlayer.SourceGenerators.Tools`
  (`IncrementalGenerator`, `Producer`, `[GenerateEquality]` models, fixed hint-name set).
- **Tests:** xUnit plus MintPlayer.Assertions.
- **CI:** `.github/workflows/resilience-benchmark.yml` (allocation ceilings asserted, as in
  `assertions-benchmark.yml`).

---

## 4. Success criteria

Measured against Polly.Core **8.8.0** under identical workloads, with a fairness gate: both libraries
must retry, open the circuit and time out on the same scripted fault sequence before any number counts.

1. **Sync happy path**, 5 strategies (timeout, retry, CB, timeout, fallback — no limiter): ≤ 0.5× Polly's
   mean, 0 B.
2. **Async-suspending callback** (`await Task.Yield()`), same pipeline: allocations ≤ 1 pooled box. The
   target is ≤ 10 % of Polly's bytes. Polly's number is measured in S1.
3. **Closure call site** via the generated pipeline: 0 B. Polly's is measured.
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
- **Interceptors.** Stable since .NET 9.0.2xx, but they require `<InterceptorsNamespaces>` opt-in in the
  consumer project (can be set from the package's `.props`). Capturing lambdas that mutate captured
  locals, or capture `ref`/`this` in structs, cannot be lowered to static + state. Those fall back to the
  normal overload (S3 defines the exact boundary).
- **Pooling async builder hazards.** A `ValueTask` from a pooled builder must not be awaited twice. User
  code that stores and re-awaits the task would break. This is the reason for an opt-out (S2).
- **Attribute order as semantics.** Relying on the declaration order of attributes needs verification.
  The fallback is an explicit `Order =` (S1).
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
