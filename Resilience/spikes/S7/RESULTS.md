# S7 results: migration from Polly v8 and the Microsoft HTTP presets

**Verdict: PASS (2026-09-29)**, with corrections to PRD §2.1, §2.2 and §2.5 listed below.

Sources read:

- `dotnet/extensions` at `398edf6` (main, 2026-09-28), `src/Libraries/Microsoft.Extensions.Http.Resilience/`.
  In the tables, `MEHR/` stands for that folder.
- Polly at `C:\Repos\polly` (`dc933fd`, main). `git diff 8.8.0 HEAD` is **empty** for every defaults file
  cited below, so these values are the 8.8.0 values.
- learn.microsoft.com "Build resilient HTTP apps" (`dotnet/docs@156931b`).

Snippets and target code: [snippet-a-retry.md](snippet-a-retry.md), [snippet-b-http.md](snippet-b-http.md),
[snippet-c-cb-fallback-timeout.md](snippet-c-cb-fallback-timeout.md).

---

## 1. Verified defaults

### 1.1 `AddStandardResilienceHandler`

Order: `MEHR/Resilience/ResilienceHttpClientBuilderExtensions.StandardResilience.cs` L82-87, outermost first.
The same method sets `HttpClient.Timeout = Timeout.InfiniteTimeSpan` (L91).

| Order | Strategy | Default | Handles | Source |
|---|---|---|---|---|
| 1 | Rate limiter | `ConcurrencyLimiter`, **1000 permits, queue 0** | n/a; rejects with `RateLimiterRejectedException` | `MEHR/Resilience/HttpStandardResilienceOptions.cs` L40-43; Polly `src/Polly.RateLimiting/RateLimiterStrategyOptions.cs` L36-39, `RateLimiterConstants.cs` L7-9 |
| 2 | Total timeout | **30 s** | n/a; throws `TimeoutRejectedException` | `HttpStandardResilienceOptions.cs` L53-56; Polly `Timeout/TimeoutStrategyOptions.cs` L24 |
| 3 | Retry | **`MaxRetryAttempts` 3 (= 4 attempts in total)**, `Exponential`, base `Delay` **2 s**, `UseJitter` true (DecorrelatedJitterV2), `MaxDelay` **null** | status **≥ 500**, **408**, **429**; `HttpRequestException`; **`TimeoutRejectedException`** (so the attempt timeout); a **connection timeout**: an `OperationCanceledException` from `System.Private.CoreLib` whose inner exception is `TimeoutException`, while the caller's token is not cancelled. It does **not** handle `BrokenCircuitException` or `RateLimiterRejectedException`, and retries **every HTTP method** | `MEHR/Polly/HttpRetryStrategyOptions.cs` L27-33; `MEHR/Polly/HttpClientResiliencePredicates.cs` L39-55, L63-72; Polly `Retry/RetryConstants.cs` L9-15, `RetryStrategyOptions.TResult.cs` L29-90 |
| 3′ | └ `ShouldRetryAfterHeader` | **true**: installs a `DelayGenerator`. When the response has `Retry-After` (delta, or a date minus `TimeProvider.System` now; a past date gives 0), **that value replaces the backoff and is not capped by `MaxDelay`** (Polly caps only computed delays). No header gives `null`, which falls back to the backoff. Setting it to false clears `DelayGenerator` | | `HttpRetryStrategyOptions.cs` L46-66; `MEHR/Internal/RetryAfterHelper.cs` L16-34; Polly `Retry/RetryResilienceStrategy.cs` L86-95 |
| 3″ | └ between attempts | The previous response is **disposed** before the delay. The **same** `HttpRequestMessage` is re-sent: the standard handler does not clone it | | Polly `RetryResilienceStrategy.cs` L109-112; `MEHR/Resilience/ResilienceHandler.cs` L62-86 |
| 4 | Circuit breaker | `FailureRatio` **0.1**, `MinimumThroughput` **100**, `SamplingDuration` **30 s**, `BreakDuration` **5 s** | status ≥ 500 / 408 / 429, `HttpRequestException`, `TimeoutRejectedException`. It does **not** handle the connection-timeout OCE, because it uses the one-argument `IsTransient` | `MEHR/Polly/HttpCircuitBreakerStrategyOptions.cs` L22-25; `HttpClientResiliencePredicates.cs` L25-30; Polly `CircuitBreaker/CircuitBreakerConstants.cs` L13-21 |
| 5 | Attempt timeout | **10 s** | n/a | `HttpStandardResilienceOptions.cs` L93-97 |

**Validation** (`ValidateOnStart`): the attempt timeout must be ≤ the total timeout, and the circuit
breaker's `SamplingDuration` must be ≥ 2 × the attempt timeout
(`MEHR/Resilience/Internal/Validators/HttpStandardResilienceOptionsCustomValidator.cs` L17-30), plus
data-annotation validation of each option object.

**Pipeline scope:** one pipeline per named client. It is per authority only after
`.SelectPipelineByAuthority()` (`MEHR/Resilience/HttpStandardResiliencePipelineBuilderExtensions.cs` L82).

### 1.2 `AddStandardHedgingHandler`

Two handlers: `MEHR/Hedging/ResilienceHttpClientBuilderExtensions.Hedging.cs` L114-140. `HttpClient.Timeout`
is set to infinite at L143.

| Order | Strategy | Default | Handles | Source |
|---|---|---|---|---|
| 0a | Routing (internal) | no-op unless routing groups are configured | n/a | L121 |
| 0b | Request snapshot (internal) | snapshots method, URI, version, headers and options. **The content is shared by reference, not buffered, and a `StreamContent` request throws `InvalidOperationException`** | n/a | L122; `MEHR/Internal/RequestMessageSnapshot.cs` L25-95 |
| 1 | Total timeout | **30 s** | n/a | `MEHR/Hedging/HttpStandardHedgingResilienceOptions.cs` L48-51 |
| 2 | Hedging | `MaxHedgedAttempts` **1** (valid range 1-10), `Delay` **2 s**; `ActionGenerator` clones from the snapshot and moves to the next route | the same as retry (≥ 500 / 408 / 429, `HttpRequestException`, `TimeoutRejectedException`, connection-timeout OCE) **plus `BrokenCircuitException`** | `MEHR/Hedging/HttpHedgingStrategyOptions.cs` L25-28; `HttpClientHedgingResiliencePredicates.cs` L40-56; Polly `Hedging/HedgingConstants.cs` L9-15; generator at L81-111 |
| 3 | Rate limiter (per endpoint) | 1000 permits, queue 0 | n/a | `MEHR/Hedging/HedgingEndpointOptions.cs` L28-31 |
| 4 | Circuit breaker (per endpoint) | 0.1 / 100 / 30 s / 5 s | the same as the standard circuit breaker | `HedgingEndpointOptions.cs` L41-44 |
| 5 | Attempt timeout (per endpoint) | **10 s** | n/a | `HedgingEndpointOptions.cs` L55-59 |

The inner (endpoint) handler is **per authority by default** (`.SelectPipelineByAuthority()`, L140).

**Validation:** the endpoint timeout must be ≤ the total timeout; the sampling duration must be
≥ 2 × the endpoint timeout; and, when there is no `DelayGenerator`, `MaxHedgedAttempts × Delay` must be
≤ the total timeout (`MEHR/Hedging/Internals/Validators/HttpStandardHedgingResilienceOptionsCustomValidator.cs` L17-45).

**The Microsoft Learn hedging table is misleading:** its "Min attempts 1, Max attempts 10" is the
validation range. The default is 1 hedged attempt, i.e. at most 2 concurrent requests.

## 2. PRD corrections

| PRD | Says | Actually |
|---|---|---|
| §2.5 table, Retry | "3 attempts" | **3 retries = 4 attempts**, named `MaxRetryAttempts` |
| §2.5 table, Retry | "handles 408/429/5xx" | **status ≥ 500** (not capped at 599), 408 and 429 |
| §2.5 table, Retry | "+ `HttpRequestException` + attempt timeout" | + **any** `TimeoutRejectedException`, + the **connection-timeout OCE** (§1.1), which the PRD omits |
| §2.5 table, Retry | "honours `Retry-After`" | yes, but the header value **replaces** the backoff, **ignores `MaxDelay`**, and uses `TimeProvider.System`, not the pipeline's `TimeProvider`. Parity must copy all three |
| §2.5 table, Retry | (silent) | retries **POST and other unsafe methods** by default. Opt-out: `DisableForUnsafeHttpMethods()` / `DisableFor(...)` |
| §2.5 table, Retry | "2 s base" | correct; `MaxDelay` is **unset** |
| §2.5 table, Circuit breaker | (silent on predicate) | a **different predicate from retry**: it does not count the connection-timeout OCE |
| §2.5 table, Rate limiter | "concurrency 1000, queue 0" | correct (`ConcurrencyLimiter`) |
| §2.5 table, timeouts | 30 s / 10 s | correct; the handler also sets `HttpClient.Timeout = Infinite` |
| §2.5 table | (silent) | Microsoft validates that attempt ≤ total and that sampling ≥ 2 × attempt; ours should too (MPR0004 for declarative pipelines) |
| §2.5 reason 3 | "the handler clones the request and buffers the content per attempt … the same approach as Microsoft's handler" | **Wrong.** Microsoft's standard handler **re-sends the same `HttpRequestMessage`**. Only hedging snapshots the request, and it **shares the content without buffering** and **throws on `StreamContent`**. Decide deliberately: copy that behaviour for parity, or buffer and document the difference |
| §2.5 hedging sentence | "mirrors Microsoft's hedging preset" | the preset is **two handlers** (outer: routing, snapshot, total 30 s, hedging 1 × 2 s; inner per authority: limiter, circuit breaker, attempt 10 s). The hedging predicate adds `BrokenCircuitException` |
| §2.5 example | `o.Retry.MaxAttempts = 5` | Microsoft: `o.Retry.MaxRetryAttempts = 5` |
| §2.1 | `[Retry(MaxAttempts = 3, Backoff = Backoff.Exponential, DelayMs = 200 …)]` | use Polly's names: `MaxRetryAttempts`, `BackoffType = DelayBackoffType.Exponential` (§4) |
| §2.1 | `SamplingSeconds`, `BreakSeconds`, `[Timeout(Seconds = 10)]` | one suffix rule: Polly name + `Ms` (`SamplingDurationMs`, `BreakDurationMs`, `TimeoutMs`) |
| §2.2 | `ResiliencePipeline.For<T>()`, `RetryOptions<T>`, `CircuitBreakerOptions`, `MaxAttempts` | `new ResiliencePipelineBuilder<T>()`, `RetryStrategyOptions<T>`, `CircuitBreakerStrategyOptions<T>`, `MaxRetryAttempts` |

## 3. Diff list: everything a migrating user sees

With the §4 naming, the runtime builder accepts every snippet as-is after the `using` swap, except where
`ShouldHandle`/generators return a `ValueTask`.

1. **Namespace/package**: `Polly` → `MintPlayer.Resilience`; `Microsoft.Extensions.Http.Resilience` → `MintPlayer.Resilience.Http`.
2. **Predicates are synchronous**: `ShouldHandle` is `Func<XxxPredicateArguments<T>, bool>`. `ValueTask.FromResult(e)`, `new ValueTask<bool>(e)` and `new(e)` become `e`. `PredicateBuilder` needs no change (implicit conversion). An `await` inside a predicate cannot be migrated.
3. **Generators are synchronous**: `DelayGenerator` returns `TimeSpan?`, `BreakDurationGenerator` and `TimeoutGenerator` return `TimeSpan`, the hedging `DelayGenerator` returns `TimeSpan`.
4. **Unchanged async**: events (`OnRetry`, `OnTimeout`, `OnFallback`, `OnOpened`/`OnClosed`/`OnHalfOpened`, `OnHedging`, `OnRejected`) keep `Func<Args, ValueTask>`. `FallbackAction` and `ActionGenerator` keep their async shapes.
5. **Argument structs**: same names, members and semantics (`AttemptNumber` is 0-based; `RetryDelay`, `Duration`, `BreakDuration`, `IsManual`, `Timeout`).
6. **Attribute form only**: `TimeSpan` values become `…Ms` ints. Delegates are referenced by `nameof(Method)`. State provider / manual control become generated accessors.
7. **Pooled `ValueTask`** (S2): double-await, `.Result` and multiple awaiters now throw or crash. MPR0006.
8. **Rejection via the Try API** returns an `Outcome` instead of throwing. `ExecuteAsync` throws the same exception types as Polly.
9. **Our additions** (not diffs): `AddResilienceHandler<TPipeline>()`, `TryExecuteAsync`, the retry budget, adaptive limit and slow-call circuit breaker.

That fits on one screen, so the plan's first Pass criterion is met.

## 4. Naming: mirror or rename

**Mirror name for name** (the difference in migration effort is large; the cost is nothing):

- Builders and pipelines: `ResiliencePipelineBuilder`, `ResiliencePipelineBuilder<T>`, `ResiliencePipeline`,
  `ResiliencePipeline<T>`, `Build()`, `ExecuteAsync`/`Execute` overloads including `(callback, state, ct)`,
  `ExecuteOutcomeAsync`.
- Extension methods: `AddRetry`, `AddTimeout(TimeSpan)`/`AddTimeout(options)`, `AddCircuitBreaker`,
  `AddFallback`, `AddHedging`, `AddRateLimiter`, `AddConcurrencyLimiter(permitLimit, queueLimit)`,
  `AddChaosFault`/`AddChaosOutcome`/`AddChaosLatency`/`AddChaosBehavior`.
- Options: `RetryStrategyOptions<T>`, `CircuitBreakerStrategyOptions<T>`, `TimeoutStrategyOptions`,
  `FallbackStrategyOptions<T>`, `HedgingStrategyOptions<T>`, `RateLimiterStrategyOptions`, and every
  property name on them (`MaxRetryAttempts`, `Delay`, `MaxDelay`, `BackoffType`, `UseJitter`,
  `ShouldHandle`, `DelayGenerator`, `OnRetry`, `Randomizer`, `FailureRatio`, `MinimumThroughput`,
  `SamplingDuration`, `BreakDuration`, `BreakDurationGenerator`, `ManualControl`, `StateProvider`,
  `MaxHedgedAttempts`, `ActionGenerator`, `FallbackAction`, `Name`).
- `DelayBackoffType`, `PredicateBuilder`/`PredicateBuilder<T>` (`Handle`, `HandleInner`, `HandleResult`),
  `Outcome`/`Outcome<T>` (`FromResult`, `FromException`, `FromResultAsValueTask`), `ResilienceContext`,
  `ResilienceContextPool`, `ResiliencePropertyKey<T>`, every `*Arguments` struct, `CircuitState`,
  `CircuitBreakerStateProvider`, `CircuitBreakerManualControl`, `ResiliencePipelineProvider<TKey>`,
  `ResiliencePipelineRegistry<TKey>`, `AddResiliencePipeline(key, (builder, context) => …)`.
- Exceptions: `TimeoutRejectedException`, `BrokenCircuitException`, `IsolatedCircuitException`,
  `RateLimiterRejectedException`, `ExecutionRejectedException`. User predicates match on these types.
- HTTP: `AddStandardResilienceHandler`, `AddStandardHedgingHandler`, `AddResilienceHandler(name, …)`,
  `RemoveAllResilienceHandlers`, `HttpStandardResilienceOptions` (`RateLimiter`, `TotalRequestTimeout`,
  `Retry`, `CircuitBreaker`, `AttemptTimeout`), `HttpStandardHedgingResilienceOptions` (`TotalRequestTimeout`,
  `Hedging`, `Endpoint`), `HedgingEndpointOptions`, `Http*StrategyOptions`, `ShouldRetryAfterHeader`,
  `DisableFor`, `DisableForUnsafeHttpMethods`, `HttpClientResiliencePredicates.IsTransient`,
  `SelectPipelineByAuthority`, `SelectPipelineBy`, `ResilienceHandler`.

**Deliberately different:**

- Predicate and generator delegate types (synchronous). This is the design; the implicit
  `PredicateBuilder` conversion absorbs most of the friction.
- Attribute property names: Polly name + `Ms` for durations; delegate hooks as `nameof` string properties
  on the strategy attribute, instead of the PRD's `[RetryWhen]` convention. A pipeline can hold two
  strategies of the same kind (two timeouts), so a convention attribute is ambiguous; a per-attribute
  `nameof` is not.
- `TryExecuteAsync` (new; returns an `Outcome` on rejection). Polly's `ExecuteOutcomeAsync` is kept too,
  with Polly's shape: the callback returns `Outcome<T>`.
- Namespaces.

## 5. MPR0001 code-fix feasibility

### 5.1 Converted mechanically to a `[ResiliencePipeline]` class

The fix applies when **all** of these hold:

- The builder is a single fluent chain ending in `.Build()`, or the body of a `static` lambda passed to
  `AddResiliencePipeline(key, …)` / `AddResilienceHandler(name, …)` that does not use the context argument.
- Every option value is a compile-time constant, an enum member, `TimeSpan.FromX(const)`,
  `new TimeSpan(const…)`, or `TimeSpan.Zero`/`InfiniteTimeSpan`. Durations fold into `…Ms`.
- Every `ShouldHandle` is one of:
  - a `PredicateBuilder` chain (inline, or a local/static field initialised with one), moved verbatim into a
    `static readonly` field that the generator reads syntactically;
  - a `static` lambda, or a non-capturing lambda, whose body is `ValueTask.FromResult(e)`,
    `new ValueTask<bool>(e)` or `new(e)` with `e` referencing only `args` and statics. It is lifted to a
    `static bool` method with the wrapper removed;
  - a reference to `HttpClientResiliencePredicates.IsTransient`.
- Events (`OnRetry` …) and `FallbackAction` are non-capturing lambdas. They are lifted verbatim to static
  methods.
- Covered: the Microsoft `"CustomPipeline"` snippet (b2) and the Polly doc snippets for the retry and
  fallback patterns.

If the only capture is an injected service (`logger` in snippets a and c), the fix still applies **if
declarative pipelines may be non-static partial classes with a DI constructor** (see §5.3). Otherwise
those snippets fall to §5.2.

### 5.2 Converted to the runtime builder (a `using` swap plus the §3 item 2/3 rewrites)

- Values from configuration or variables: `IOptions`, `context.GetOptions`/`EnableReloads`, parameters.
- Lambdas that capture locals or `this`, other than DI services.
- Builders built conditionally (`if`), passed between methods, or added to with loops.
- `AddStandardResilienceHandler(o => …)`: already runtime; the fix keeps it (snippet b1). A companion
  refactoring *may* offer the declarative preset when every assignment in the lambda is constant.
- `StateProvider`/`ManualControl` instances shared with other code.

### 5.3 Not convertible (diagnostic only, no fix)

- A predicate or generator that awaits (e.g. reads `response.Content`). Synchronous predicates cannot
  express it; the user must restructure (read the body in the callback).
- Custom strategies: `AddStrategy(...)`, `ResilienceStrategy<T>` subclasses, third-party Polly extensions.
- Telemetry plumbing: `TelemetryOptions`, `TelemetryListener`, custom `MeteringEnricher`.
- The Polly v7 API (`Policy.Handle<>()…`), which is out of scope by PRD §1.4.
- Polly types in the user's public API (a method returning `ResiliencePipeline`). The fix would break
  callers, so it offers no automatic change.

**Consequence for the PRD (a gap, not a correction):** §2.1 shows only `static partial class`. Real
`OnRetry`/`OnFallback`/`OnOpened` hooks almost always log through an injected `ILogger`. Without an
instance form of declarative pipelines (DI constructor, hooks as instance methods, circuit-breaker state
still static), 2 of the 3 real snippets cannot be converted to the fast path. Recommend adding it to M4.

## 6. Pass / fail against the plan

| Pass criterion | Result |
|---|---|
| Diff list ≤ one screen | **Pass**: 9 items (§3), provided the §4 naming is adopted |
| The code fix is feasible for constant-options pipelines | **Pass**: §5.1 is purely syntactic. The instance-pipeline gap in §5.3 decides whether logging hooks are covered too |
| The preset table is verified | **Pass**: verified against source (§1); the §2.5 corrections are in §2 |

**Required follow-ups inside #189:**

1. Fix PRD §2.5 (table, reason 3, example) and the §2.1/§2.2 names.
2. Decide how retries handle the request: re-send it like Microsoft, or clone and buffer.
3. Add the instance (DI) form of declarative pipelines to M4.
4. The M7 parity test pins every value and predicate from §1, including `Retry-After` ignoring `MaxDelay`,
   the connection-timeout OCE, disposal of the previous response, and the hedging `BrokenCircuitException`
   rule.
