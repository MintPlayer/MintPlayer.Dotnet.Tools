# Plan — MintPlayer.Resilience

Companion to [Resilience-prd.md](Resilience-prd.md).

**Status (2026-10-02):**

- Branch `issues/#189`, draft PR [#190](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/pull/190).
- Done and committed: M0 (spikes S1–S8, GO), M1, M2, M3, and M4 part A (the generator).
- **Next: M4 part B** — the analyzers MPR0004–MPR0007 and the S5 rejection code fix. Not started.
- Not started: M5–M9 and the final sweep. No test suite has been run yet (by the working rules below).
- Open question for the owner: an implicit `T → Outcome<T>` conversion (PRD §5).

**Working rules:**

- **Everything lands in ONE pull request.** Milestones are commit boundaries.
- **Test suites run once, after the last milestone.** Intermediate milestones are verified by reading the
  code and type-checking.
- **The benchmark exception is the spikes.** Their whole point is a measurement, so they are measured
  when they run.

## Spikes (M0)

Each spike is throwaway code under `Resilience/spikes/`, deleted before the PR is finalized. Each result
is recorded in "Spike results" below.

| # | Question | Method | Pass |
|---|---|---|---|
| S1 | Does a flat, hand-written equivalent of the generated method beat Polly 8.8.0 enough to justify the project? Does generic-struct nesting (`Pipeline<T, Retry<Timeout<…>>>`) give the runtime builder the same result? | Hand-write the 5-strategy pipeline three ways: flat method, struct-nested chain, Polly. BDN `[MemoryDiagnoser]` on sync-happy, async-suspending (`Task.Yield`) and one-retry workloads. Also check `GetAttributes()` declaration-order stability across partial declarations. | Flat: sync ≤ 0.5× Polly, async ≤ 1 box. Struct-nested within 20 % of flat. **If async allocations are not at least 5× lower, stop and report: the project is not worth it.** |
| S2 | Is `PoolingAsyncValueTaskMethodBuilder` on the generated method safe and worth it? | Same async benchmark with and without it. Test a double-await / `.AsTask()` misuse to see the failure mode. Run a concurrency stress test. | 0 B steady state. Misuse fails loudly (not silent corruption). Decide default-on + opt-out, or opt-in. |
| S3 | Can interceptors lower capturing lambdas at `ExecuteAsync` call sites to static + state across net10/net11 with Roslyn 5.9? | Prototype a generator using `InterceptableLocation`. Cover: capture of locals, `this`, a mutated local, a `ref` local, nested lambdas, async lambdas, generic methods. Set `InterceptorsNamespaces` from the package `.props`. | Common captures (read-only locals, `this`) lowered to 0 B. Every unsupported shape falls back to the plain overload with no behaviour change. The exact boundary is documented. |
| S4 | Lock-free circuit breaker? | Packed state in a `long` (state + break-until ticks) via `Interlocked.CompareExchange`, with striped/bucketed health counters. Compare against Polly's `lock` under 1/4/16 threads. Verify transitions with a fake `TimeProvider`, and fuzz half-open races (only one probe may go through). | Throughput ≥ Polly at 16 threads. No lost transitions over 10⁶ fuzzed interleavings. Otherwise keep a lock (correctness first). |
| S5 | Rejection without throwing: `TryExecuteAsync` returning `Outcome<T>`, plus cached stackless exceptions for `ExecuteAsync`. | Benchmark open-circuit and rate-limited rejection both ways. Check that the cached exception does not leak mutable state (`Data`, stack) across calls. | ≤ 250 ns / 0 B on `TryExecuteAsync`. Settle the cached-exception safety question (or allocate on throw and accept the cost). |
| S6 | Reload for generated pipelines: how are options bound so a generated pipeline can pick up `IOptionsMonitor` changes? | Two variants: constants folded (no reload), or options read from a `static` volatile snapshot swapped on change. Measure the overhead of the snapshot read. | Snapshot variant ≤ 5 ns overhead → reload is opt-in per pipeline via `[ResiliencePipeline(Reloadable = true)]`. |
| S7 | Migration: how close can the runtime builder's names get to Polly v8, and can MPR0001 convert real code mechanically? Are Microsoft's standard-preset defaults what PRD §2.5 lists? | Take 3 real-world Polly v8 snippets (plain retry, the standard HTTP handler preset, CB + fallback). Write the target code by hand, and list every name or semantic difference. Read `HttpStandardResilienceOptions` / `HttpStandardHedgingResilienceOptions` in the dotnet/extensions source, and correct the §2.5 table. | Diff list ≤ one screen. The code fix is feasible for constant-options pipelines. The preset table is verified. |
| S8 | Telemetry cost when enabled. Polly adds ~230 ns. | Pre-bound `TagList` per strategy instance, `Counter<T>.Add` / `Histogram.Record` only when `Enabled`, `LoggerMessage` source-gen, `ActivitySource.HasListeners` gate. | Telemetry on ≤ 120 ns overhead per execution. Off = 0 ns: compiled out for generated pipelines, one branch for runtime ones. |

**Go/no-go after M0:** if S1 fails its stop condition, the plan ends there with a written recommendation:
use Fences, or pin Polly 8.8.0.

**M0 status: ✅ complete (2026-09-30). GO.**

- S1, S2 and S4–S8 pass.
- S3 fails for interceptors. Its technique moves to the MPR0002 code fix (M5).
- Results are in "Spike results" below.

## Milestone 1 — Runtime core ✅

- `Outcome<T>`, `ResilienceContext` + pool, `ResiliencePropertyKey<T>`, `TimeProvider` plumbing.
- Strategy structs: Retry (all backoff types, jitter formulas matching Polly's decorrelated jitter),
  Timeout (pooled CTS), Fallback.
- The runtime builder in the shape S1 chose (struct-nested, plus the array fallback).

**Done when:** it compiles for net10/net11 with `IsAotCompatible` and no warnings.

**Implementation notes:**

- **Shape:** S1 decision 2 replaces the "struct-nested, plus array fallback" bullet above. The
  interpreter is `Pipeline/PipelineCore<T>.RunAsync`: ONE async method, pooled builder, generic over the
  callback struct, the result shape (`T` or `Outcome<T>`) and a telemetry struct (`NoTelemetry` today).
- **Hook contract** (`Pipeline/PipelineStrategy.cs`), which M2, M3 and M6 plug into:
  - `ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)`: `false` short-circuits with
    `frame.Outcome` set.
  - `ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)`: `true` re-runs the inner part
    (retry).
  - Per-execution state goes in `frame.Slots[index]`. Hooks complete synchronously on the happy path;
    slow paths are pooled async methods.
  - A hook exception becomes the outcome that the outer strategies see.
- **Result-agnostic strategies** (timeout now; CB, limiters and chaos next) implement `StrategyFactory`,
  created once per `Build()` so shared state such as a breaker controller lives there.
  `Create<TResult>()` makes the typed hooks. Add them through
  `ResiliencePipelineBuilderBase.AddStrategyFactory`, which serves both builders. Typed-only strategies
  (fallback) use `ResiliencePipelineBuilder<T>.AddStrategy`.
- **Non-generic `ResiliencePipeline`:** it builds one `PipelineCore<TResult>` per result type on first
  use. Non-generic options (`RetryStrategyOptions : RetryStrategyOptions<object>`) are adapted, which
  boxes value-type results only when a user delegate runs, as in Polly.
- **Context:** `args.Context` is lazy. The frame rents a `ResilienceContext` only when a delegate reads
  it.
- **Rejections:** `Outcome<T>` stores the rejection kind and ticks (`RetryAfter`, or the timeout for
  `Timeout`) plus the cause, which becomes the `InnerException`. `Outcome.Rejected<T>(kind, ticks,
  cause)` is internal; M2 and M3 use it in `EnterAsync`. All four exception types exist, deriving from
  `ResilienceRejectedException`. Polly's `ExecutionRejectedException` base is not mirrored.
- **`PredicateBuilder.Handle<TException>()`** matches rejections by type without creating the exception.
  M2 and M3 must add their implicit conversions (CB, hedging predicates) to `PredicateBuilder.cs`.
- **Void executions** return through a small pooled adapter (`ToVoidTask`), which is a second box only
  when the callback suspends.
- **`UsePooledAsync(false)`** converts a suspended execution to a `Task` instead of duplicating the
  interpreter.
- **Validation:** options throw `ValidationException` when added (Polly's ranges), written by hand
  because DataAnnotations reflection is not AOT-safe.
- **For M4:** the shared state types (`CancellationTokenSourcePool`, `RetryHelper`, `DefaultPredicates`)
  are `internal`. Generated code needs them public, e.g. `[EditorBrowsable(Never)]`. *(Done in M4 part A,
  through `GeneratedPipelineSupport`.)*
- **Not in M1:** Polly's `ExecuteOutcomeAsync`, and `Retry-After` awareness (M7, via `DelayGenerator`).
  Retry skips disposing value-type results, to avoid boxing them.

## Milestone 2 — Circuit breaker ✅

- Controller per S4: sliding health window, half-open probing, manual control, state provider.
- `BrokenCircuitException` / `IsolatedCircuitException`, cached per S5.

**Done when:** the state machine covers closed → open → half-open → closed/open, isolated, and
manual reset.

**Implementation notes:**

- **Files:** `CircuitBreaker/CircuitController.cs` is the S4 port: the packed-word controller,
  `RollingHealth` (striped, event-anchored windows), `MicroClock`, and `EventSequencer`.
  `CircuitBreakerStrategy.cs` holds the hooks, the non-generic `CircuitBreakerStrategyFactory`, and
  `CircuitBreakerSetup` (state provider and manual control). The S4 spike is untouched.
- **"Cached per S5" is superseded by M1:** a rejection is `Outcome.Rejected(CircuitOpen | CircuitIsolated,
  remaining-break ticks, last handled exception)`, and a fresh exception is created per throw.
  - `RetryAfter` is clamped at ≥ 0; Polly can report a negative value while a half-open probe hangs.
  - Isolated rejections carry no `RetryAfter` and no inner exception, as in Polly.
- **Events are sequenced.** Polly runs them one at a time through a scheduled executor. Here, the CAS
  winner of generation `g` waits (`EventSequencer`) until the events of `g − 1` are done, then raises
  its own and awaits it inside the hook.
  - Transitions skip the sequencer entirely when no event is set. Every transition must pass through
    it, or none may, otherwise the generation chain has gaps and the next event deadlocks.
  - **M6 (telemetry) must keep that invariant** when it adds events.
- **Slow calls (beyond Polly):** `SlowCallDurationThreshold` (`TimeSpan?`, null = off, 1 ms–1 day) and
  `SlowCallRatio` (0 < r ≤ 1, default 1.0).
  - The duration is measured admission → outcome, excluding the `OnHalfOpened` event. It is stored in
    `slot.Long` (µs) only when the threshold is set.
- **`BreakDurationGenerator`** is synchronous (S7). It is not called for a manual isolation, which
  reports `TimeSpan.MaxValue`; Polly calls it there too.
  - Its failure rate and count are snapshot at Closed → Open and reused on a re-open from half-open
    (the S4 window-clear deviation).
  - When two threads race to open the circuit, the generator may run for both.
- **Deviation:** an `OnHalfOpened` that throws counts as a failed probe and re-opens the circuit. Polly
  leaves the circuit half-open forever in that case.
- **State lives in the factory.** One controller per `Build()` is shared by every result type of a
  non-generic pipeline. `CircuitBreakerSetup.Attach` returns the manual-control registration
  (`IDisposable`), which is currently dropped.
  - **M6 must dispose it** on pipeline dispose/reload, and keep the controller across reloads (PRD:
    "state survives a reload").
- **Tests** (written, not run): `CircuitControllerTests`, `CircuitBreakerTests`,
  `CircuitBreakerParityTests`, `CircuitBreakerStressTests` and `AllocationTests` (+4).
  - The parity test compares 400 random scripts against Polly 8.8.0, which is a test-only
    `Polly.Core [8.8.0]` reference. It checks our pipeline and the bare controller (random stripes),
    including retry-after.
  - The stress tests are `[Trait("Category","Stress")]`.

## Milestone 3 — Rate limiting, hedging & chaos ✅

- Rate and concurrency limiter over `System.Threading.RateLimiting`.
- Native lease-free fixed/sliding-window and concurrency limiters, so rejections allocate 0 B (S5).
- Hedging with pooled execution contexts and linked CTS. Only the intrinsic allocations remain.

- Chaos strategies: fault, outcome, latency and behavior injection, with injection rate and an
  `Enabled` generator.
- Beyond Polly:
  - retry budget (percentage of recent traffic, shared);
  - adaptive concurrency limiter (AIMD / gradient);
  - slow-call ratio in the circuit breaker (this part lands with M2).

**Done when:** all three are usable from the runtime builder.

**Status:** both halves are ✅: the limiters (rate/concurrency limiting, lease-free limiters, retry
budget, adaptive limit), then hedging and chaos.

**Implementation notes (limiter half):**

- **Files:** `RateLimiting/` holds the BCL-backed strategy (`RateLimiterStrategy*.cs`), the lease-free
  limiters (`NativeLimiters.cs`, `NativeLimiterStrategyOptions.cs`), the adaptive limiter
  (`AdaptiveConcurrencyLimiter*.cs`) and every `Add*` extension (`LimiterBuilderExtensions.cs`).
  `Retry/RetryBudget.cs`; `Pipeline/SegmentedCounter.cs` is the lock-free rolling counter behind the
  sliding window and the budget. `System.Threading.RateLimiting` 10.0.0 is a package reference: it is
  not in `Microsoft.NETCore.App`.
- **BCL strategy (Polly parity):** `RateLimiterStrategyOptions` (`RateLimiter`, `DefaultRateLimiterOptions`
  = concurrency 1000 / queue 0, `OnRejected`), `AddRateLimiter(RateLimiter | options)`,
  `AddConcurrencyLimiter(permitLimit, queueLimit)` and `(ConcurrencyLimiterOptions)`.
  - The `RateLimiter` delegate stays `Func<RateLimiterArguments, ValueTask<RateLimitLease>>`: S7 made
    only predicates and duration generators synchronous; acquisition can queue, like `ActionGenerator`.
  - `RateLimiterArguments.CancellationToken` is an addition (no context rental). `AddRateLimiter(RateLimiter)`
    calls the limiter directly instead of through a delegate.
  - Lease in `slot.Object`, disposed on exit; a refused lease is disposed after `OnRejected`. `RetryAfter`
    comes from `MetadataName.RetryAfter`. A cancelled queue wait surfaces as the limiter's OCE.
- **Lease-free limiters (names chosen):** `AddFixedWindowLimiter` / `FixedWindowLimiterStrategyOptions`,
  `AddSlidingWindowLimiter` / `SlidingWindowLimiterStrategyOptions` (`SegmentsPerWindow` 1–100),
  `AddNativeConcurrencyLimiter` / `NativeConcurrencyLimiterStrategyOptions` (no queue). All share
  `OnLimiterRejectedArguments` (`Context`, `RetryAfter`) and reject with `RateLimited` at 0 B.
  - Fixed window: one packed `long` (window number, count), exact under contention; windows are aligned
    to `Build()`.
  - Sliding window and budget: count first, then check, and take back on refusal. They never over-admit;
    under contention a call can be refused while another refusal is being taken back.
  - All are `TimeProvider`-driven through `MicroClock` (µs).
- **`RejectionKind` is unchanged:** the adaptive limiter rejects with `RateLimited` and
  `RateLimiterRejectedException`, so existing predicates keep working.
- **Adaptive limiter:** `AddAdaptiveConcurrencyLimiter` (generic and non-generic, like the breaker),
  `AdaptiveConcurrencyLimiterStrategyOptions<T>`, `AdaptiveConcurrencyAlgorithm { Aimd, Gradient }`,
  `ShouldHandle` (drops) with a `PredicateBuilder` conversion. Deviations from Netflix, all found by
  simulation (capacity 20, 100 offered per round):
  - Netflix's per-sample updates collapse on a burst: 35 simultaneous drops took the limit from 39 to 1.
    Here there is **one backoff per generation** (a call admitted before a backoff cannot back off
    again), AIMD adds `1/limit` per sample (+1 per round trip), and the gradient moves `Smoothing/limit`
    per sample.
  - The gradient uses the **no-load (minimum) latency**, not Gradient2's long-term average: the average
    followed the overload and the limit ran to its maximum. The minimum is only forgotten at the floor
    (max(MinLimit, 4) + 1). Result: AIMD with a 1.5× threshold settles at 27–31, and the gradient at 36
    (the fixed point `n = Tolerance × capacity + √n`).
  - Known limitation (documented on `LatencyWindow`): a permanent baseline rise by `r > Tolerance`, or a
    start deep in overload, pins the gradient at `(1/(1 − Tolerance/r))²`.
- **Retry budget:** `RetryBudget(retryRatio 0.2, minRetriesPerSecond 10, timeToLive 10 s, timeProvider)`,
  with `Deposit` / `TryWithdraw` / `Balance`. The window is 10 segments. `RetryStrategyOptions.Budget` +
  `OnBudgetExhausted` (`OnRetryBudgetExhaustedArguments<T>`).
  - The retry deposits once per execution (in `EnterAsync`) and withdraws before each retry, after the
    predicate and the last-attempt check.
  - Exhaustion returns the current outcome, as if it were the last attempt.
- **For M6:**
  - The pipeline must dispose `RateLimiterStrategyFactory.Wrapper` (the owned default
    `ConcurrencyLimiter`), as Polly does.
  - Limiter and adaptive state lives in the factory (per `Build()`). If reload rebuilds strategies, it
    must carry the state over, as it does for the breaker.
  - Telemetry events are still to add: rejected, and limit changed.
- **Tests** (written, not run):
  - `RateLimiterTests`, `NativeLimiterTests`, `RetryBudgetTests`, `AdaptiveConcurrencyLimiterTests`.
  - `LimiterAllocationTests`, in the allocation collection.
  - `LimiterStressTests`, tagged `[Trait("Category","Stress")]`.

**Implementation notes (hedging & chaos half):**

- **Interpreter extension:** `ForkingStrategy<T>` (in `Pipeline/PipelineStrategy.cs`) is a strategy that
  runs the inner remainder itself. `PipelineCore` precomputes `_nextFork[i]` (the first forking strategy
  at or after `i`), and `RunAsync` takes a `start` depth.
  - The walk inward stops at the fork. In place of the callback, the interpreter calls
    `ForkingStrategy.ExecuteAsync(frame, index, inner)`.
  - `inner` is a pooled `InnerRunner<TCallback>` (an `InnerPipeline<T>`). Each
    `inner.ExecuteAsync(context)` rents a new `ExecutionFrame` bound to that context and runs `RunAsync`
    from `fork + 1`, with `OutcomeShape` and `NoTelemetry`.
  - Without a fork, the cost is one comparison per round. The fork's own Enter/Exit are never called.
  - Nested hedging works, because each inner run finds the next fork.
- **Hedging** (`Hedging/`) ports Polly's `HedgingResilienceStrategy`, `HedgingExecutionContext` and
  `TaskExecution` one-for-one. It is typed-only (`AddHedging` on `ResiliencePipelineBuilder<T>`), as in
  Polly.
  - The execution context and the attempts are pooled per strategy, and the attempt CTSs come from
    `CancellationTokenSourcePool`. What still allocates is intrinsic: a `Task` per attempt, `WhenAny`,
    and the `WaitAsync` timer.
  - Hedging always materializes the frame's context. Every attempt, the primary included, runs on its
    own `ResilienceContext`, copied with `InitializeFrom`: operation key, flags and properties, plus the
    attempt's own token.
  - When the call completes, the accepted attempt's properties are merged into the primary context
    (`AddOrReplaceProperties`). The losers' changes are dropped.
  - Losers are cancelled **and awaited** before the strategy returns. Results that were not accepted are
    disposed; value types are skipped, as in retry.
  - When every attempt is handled, the primary's outcome wins (Polly's `_tasks.First(completed)`).
  - `OnHedging.AttemptNumber` is Polly's `attemptNumber − 1`. `HedgingPredicateArguments.AttemptNumber` is
    `int?`, as in Polly.
  - A synchronous `Execute` runs hedged attempts through `Task.Run`.
  - `Delay`: zero means parallel, negative means fallback mode. It is not validated, as in Polly.
- **Hedging deviations:**
  - In parallel mode, an `ActionGenerator` that returns `null` makes the strategy wait for a completion.
    Polly spins instead.
  - A throwing `ShouldHandle` becomes the outcome, where Polly faults the task.
  - An `OnHedging` that throws releases the attempt, then becomes the outcome.
- **Chaos** (`Simmy/`, namespaces `MintPlayer.Resilience.Simmy[.Fault|.Outcomes|.Latency|.Behavior]` as
  in Polly):
  - Fault, latency and behavior are `StrategyFactory`s, so they serve both builders. Outcome is
    typed-only.
  - S7 rule: `EnabledGenerator` returns `bool`, `InjectionRateGenerator` `double`, `LatencyGenerator`
    `TimeSpan`, `FaultGenerator` `Exception?` and `OutcomeGenerator` `Outcome<T>?`. `BehaviorGenerator` and
    the events stay `ValueTask`.
  - `FaultGenerator` and `OutcomeGenerator<T>` are weighted builders with implicit conversions. Each has
    an internal constructor that takes a weight generator, for tests.
  - Polly's order is kept: token check, enabled, token, rate (clamped to 0–1), token, then
    `randomizer() < rate`. The token is checked again before continuing. The latency event is raised
    after the delay.
  - `ChaosSettings.IsOff` (disabled, or rate 0 without generators) turns the hook into one branch. It
    skips Polly's token check and randomizer call; this is documented.
  - Not injecting allocates 0 B. Generator args resolve `Context` lazily, as the other args do.
  - Polly 8 has no `Fault` property, only `FaultGenerator` plus the `AddChaosFault(rate, Func<Exception?>)`
    shorthand. Mirrored as is.
- **For M4:** the generator must emit `ForkingStrategy` handling, or call the runtime hedging strategy.
  The simplest route is to fall back to the interpreter for pipelines that contain hedging. *(Done in
  M4 part A: a hedging pipeline forwards to a runtime pipeline built once.)*
- **For M6:**
  - Inner runs use `NoTelemetry`, so per-attempt telemetry (`ExecutionAttempt`, `OnHedging`) must be
    reported by the hedging strategy.
  - The chaos events have no telemetry yet.
- **Tests** (written, not run):
  - `HedgingTests`: win/lose, delay timing, parallel and fallback modes, max attempts, disposal, action
    generator, events, delay generator, context fork and merge, caller cancellation, retry and timeout
    inside and outside hedging, sync `Execute`, and a concurrency smoke test.
  - `ChaosTests`: rates via deterministic randomizers, enabled and generators, cancellation, validation,
    each generator helper by weight, latency via `FakeTimeProvider`, behavior, and chaos inside hedging.
  - `ChaosAllocationTests`, in the allocation collection: 0 B when disabled, at rate 0, and when not
    injecting.

## Milestone 4 — Source generator: declarative pipelines ⏳ (in progress: generator ✅, analyzers ⏳)

- `MintPlayer.Resilience.SourceGenerator` on `MintPlayer.SourceGenerators.Tools`:
  - `[GenerateEquality]` models;
  - a fixed hint-name set, e.g. `ResiliencePipelines.g.cs`;
  - packed under `analyzers/dotnet/roslyn5.9/cs` in the core package (the Assertions
    `TargetsForTfmSpecificContentInPackage` pattern).
- Emits the flat method from S1 per `[ResiliencePipeline]` class. Covers:
  - `[RetryWhen]` / `[FallbackWith]` / `[OnRetry]` hooks;
  - both the static form and the non-static DI form (a constructor with injected services and instance
    hooks; S7);
  - `ExecuteAsync` / `TryExecuteAsync` / sync `Execute` overloads;
  - the pooling builder per S2;
  - reloadable pipelines per S6.
- MPR0004 (invalid combinations) and MPR0005 (not awaited).

**Done when:** the sample `CatalogPipeline` from PRD §2.1 generates, compiles and passes the analyzers
clean.

**Status:** in progress.

- **Part A, the generator: ✅** (commit 6fedf40).
- **Part B, the analyzers: ⏳ not started.** It was begun once and stopped by the owner before it changed
  anything. It completes the milestone:
  - [ ] MPR0004: every `PipelineModel.SkipReason`, plus mismatched hook signatures, ambiguous hook
        attributes, inner ≥ outer timeout, and constants outside the builder's ranges (see the part A
        notes below).
  - [ ] MPR0005 (not awaited), MPR0006 (pooled `ValueTask` misuse, S2), MPR0007 (attributes split across
        partials, S1). Rules go in `Diagnostics/`; `DiagnosticIds.cs` reserves the ids.
  - [ ] The S5 rejection code fix (PRD §2.4/§2.6): `o.Exception is BrokenCircuitException` →
        `o.Rejection == RejectionKind.CircuitOpen`, for every rejection type. **Id: MPR0008** (info).
  - [ ] A separate code-fix assembly, `MintPlayer.Resilience.CodeFixes`.
        - It is `IsPackable=false`, and holds the `Microsoft.CodeAnalysis.Workspaces` reference. The
          generator must not reference Workspaces (RS1038).
        - It is packed next to the generator under `analyzers/dotnet/roslyn5.9/cs`.
        - M5 and M8 add their code fixes to it.
        - **Decision (2026-10-02):** a separate assembly, not the MintPlayer.Assertions precedent of
          accepting RS1038 in the generator. It keeps the generator loadable in command-line builds, and the
          projects at 0 warnings.
  - [ ] Analyzer and code-fix tests (written now, run in the final sweep).
  - [ ] Docs: the package README lists the rules; PRD §2.6 status column updated.

**Implementation notes (part A, generator):**

- **Files:** `Resilience/MintPlayer.Resilience.SourceGenerator/` (`Generators/ResiliencePipelineGenerator.cs`,
  `PipelineParser.cs`, `StrategySchema.cs` (the per-kind table: attribute, values + defaults, hook slots and
  signatures), `PipelineEmitter*.cs`, `Models/PipelineModel.cs`). The attributes and the M6 seam are in the core,
  `Declarative/`. One hint name: `ResiliencePipelines.g.cs`.
- **Shape of the flat method (deviation, by design):** `__RunAsync` is one pooled async method on the same
  `ExecutionFrame<T>` as the interpreter (so argument structs resolve `Context` lazily, identically). **Retry,
  timeout and fallback are inlined** as statement-for-statement ports of their strategies, values folded, hooks
  called directly. **Breaker, limiters, adaptive limiter and chaos are driven through their runtime hooks**
  (`GeneratedStrategy<T>.EnterAsync/ExitAsync`, built once from a one-strategy builder pipeline): their state is
  intrinsic and the breaker's event sequencing is too subtle to duplicate, so their parity is by construction.
  **Hedging** is not flattened: the members forward to a runtime pipeline built once (same surface).
- **Result type:** `[ResiliencePipeline<T>]`, else inferred from a hook's concrete `Outcome<T>`/args/fallback return,
  else generic (`ExecuteAsync<TResult>` + void overloads, like non-generic `ResiliencePipeline`; hooks must be
  generic methods). In the generic form, inlined hooks see the concrete `TResult`, but delegated strategies are
  built on the non-generic builder and see `object` (Polly's behaviour; a value-type result is boxed whenever such
  a predicate runs). Hedging and chaos-outcome need a typed pipeline.
- **Forms:** static unless a bound hook or member is an instance member or a constructor takes parameters (DI
  form: instance members, state per instance, `Create(IServiceProvider)` resolves the widest constructor).
- **Hook scheme:** every slot is a string property named like the Polly option (`nameof`), plus hook attributes
  for the common ones (`[RetryWhen]`, `[OnRetry]`, `[DelayGenerator]`, `[TimeoutGenerator]`, `[OnTimeout]`,
  `[BreakWhen]`, `[OnOpened]`, `[OnClosed]`, `[OnHalfOpened]`, `[BreakDurationGenerator]`, `[FallbackWhen]`,
  `[FallbackWith]`, `[OnFallback]`, `[HedgeWhen]`, `[OnHedging]`) binding to the only strategy of the kind or the one
  they name; the explicit name wins. Adapted shapes: args or `Outcome<T>` (predicates, fallback), `void` events,
  fallback returning `T`/`Outcome<T>`/`ValueTask<T>`/`ValueTask<Outcome<T>>`. Members: `Retry.Budget`,
  `CircuitBreaker.ManualControl`/`StateProvider`, `RateLimiter.RateLimiter` (a `RateLimiter` field or a method).
  Class-level `[RetryBudget]` is a pipeline-owned budget.
- **Made public for generated code** (`[EditorBrowsable(Never)]`, namespace `MintPlayer.Resilience.Pipeline`):
  `ExecutionFrame(<T>)` (`Slots` stays internal), `ICallback<T>` and the 8 callback structs, `IOutcomeShape`,
  `ResultShape`, `OutcomeShape`, `IPipelineTelemetry`, `NoTelemetry`, `VoidResult`, `CancellationTokenSourcePool`; new
  `GeneratedPipelineSupport` (backoff, delays, default predicate, disposal, args factories, `Validate`, `Wait`,
  `ToVoidTask`, `ReleaseAttachments`) and `GeneratedStrategy(<T>)`. `RetryHelper`/`DefaultPredicates` stay internal
  behind the support class; argument constructors stay internal.
- **Snapshot (S6):** each class has a nested `__Runtime` (clock, randomizer, CTS pool, runtime strategies, and for a
  reloadable pipeline the options and the folded values), read once per execution with `Volatile.Read`.
  `UseTimeProvider(time, randomizer?)` replaces it (new state; tests use it). Reloadable: generated
  `<Class>Options` with one `<Kind>Section` property per strategy (key = `Name`, else the kind, `Timeout2` for a
  second unnamed one), `TryApply(options, out error)`; values validated with the builder's rules (bad values →
  `false`, previous snapshot stays). **A runtime strategy whose section is unchanged keeps its instance (breaker
  health survives); a changed section recreates it (state reset)**, since `CircuitController` takes its settings
  at construction. A hedging (forwarding) pipeline is rebuilt on every reload.
- **Breaker attachments:** `CircuitBreakerSetup.Attach` now returns one attachment for the state provider and the
  manual control (the factory property is renamed `ManualControlRegistration` → `Attachment`; the typed strategy
  keeps it too). A replaced runtime releases its breakers' attachments before the new ones attach (else the state
  provider throws "already initialized"). Phases in `__Runtime`: validate + fill builders, release, build.
- **Validation:** inlined strategies are validated at snapshot creation, so an invalid constant throws
  `ValidationException` on the first execution (MPR0004 should catch it at compile time).
- **For part B (analyzers):** the generator reports nothing. A class it cannot generate gets no code and
  `PipelineModel.SkipReason` says why: not partial / static / generic / non-partial or generic containing type,
  a missing hook or member name, a fallback without action, chaos outcome/behavior/fault without their generator,
  a typed-only strategy in a generic pipeline. An ambiguous hook attribute (two strategies of the kind, no name) is
  left unbound. MPR0004 should cover these plus inner ≥ outer timeout and mismatched hook signatures; MPR0005,
  MPR0006 (S2), MPR0007 (S1) as planned. Rules go in `Diagnostics/` (`DiagnosticIds.cs` reserves the ids). The
  generator project removed the `Microsoft.CodeAnalysis` meta-package (Workspaces → RS1038). Code fixes go in the
  separate `MintPlayer.Resilience.CodeFixes` assembly (see the part B checklist), not back into the generator.
- **For M6:** the seam is `IGeneratedResiliencePipeline<TSelf>` (`PipelineName`, `IsInstancePipeline`,
  `Create(IServiceProvider)`, `UseTimeProvider(instance, time, randomizer)`) and
  `IReloadableResiliencePipeline<TSelf, TOptions>` (`DefaultSectionPath`, `TryApply(instance, options, out error)`),
  all static abstract (explicitly implemented). Telemetry: `__RunAsync` is generic over `TTelemetry` and the members
  pass `NoTelemetry`; delegated strategies get their telemetry from the builder. Disposal: `GeneratedStrategy.Release`
  / `GeneratedPipelineSupport.ReleaseAttachments`; the owned default `ConcurrencyLimiter` is still not disposed.
- **Gotchas:** code in a namespace under `MintPlayer.Resilience` sees `Timeout`, `Retry`, `Fallback`, … as the
  sub-namespaces (`Timeout.InfiniteTimeSpan` fails); the samples live in `ResilienceSamples`. Packing warns NU5118 ×3,
  exactly as MintPlayer.Assertions does (the per-TFM target adds the same files twice); the payload is correct.
- **Tests** (written, not run), `Resilience/MintPlayer.Resilience.SourceGenerator.Tests/`:
  - `Samples/` (compiled by the project, so they are the compile check): PRD `CatalogPipeline` and DI
    `OrdersPipeline`, the parity pipelines, and coverage pipelines using every attribute and hook shape.
  - `Generators/SnapshotTests` (golden `Snapshots/ResiliencePipelines.g.cs`; refresh from `obj/Generated`),
    `GeneratorOutputTests`, `IncrementalOutputCachingTests`, `FixedFileSetGuardTests`.
  - `Behaviour/ParityTests` (generated vs runtime builder, same hooks, same `FakeTimeProvider` timeline),
    `GeneratedPipelineTests` (overloads, forms, reload, DI seam, pooling, manual control), `AllocationTests` (0 B
    sync, sync-completing async, and pooled suspending async above the callback's box). One non-parallel collection.

## Milestone 5 — Closure analyzer + code fix ⏳

Interceptors are dropped (S3).

- **MPR0002 (info/suggestion):** a capturing lambda passed to any pipeline's `Execute*`.
- **Code fix:** rewrites the call to the `static (state, ct) => …` overload, with a state tuple. It is
  offered only inside S3's verified boundary, and is based on `spikes/S3/S3.Generator/SiteAnalyzer.cs`.
- **Out-of-boundary shapes get no fix**, including mutated captures, since the naive rewrite gave wrong
  results in S3.

**Carry-over:**

- [ ] The analyzer may live in the generator assembly; the code fix goes in the code-fix assembly from
      M4 part B (Workspaces reference, RS1038).
- [ ] Cover both the runtime pipelines and the generated `[ResiliencePipeline]` members, whose state
      overloads are `ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<T>>, TState, …)`.

**Done when:** code-fix tests cover all 31 S3 shapes. Every in-boundary shape becomes 0 B with the same
results, and every out-of-boundary shape has no fix offered.

## Milestone 6 — DI, registry, reload, telemetry ⏳

- `AddResiliencePipeline<TGenerated>()` and `AddResiliencePipeline(key, …)`.
- Keyed provider/registry, with `IOptionsMonitor` reload.
- Meter / ActivitySource / LoggerMessage telemetry per S8:
  - default names plus the opt-in `PollyCompatible` scheme;
  - happy-path events at Debug;
  - meter and instrument names documented.

**Carry-over from the implementation notes of M2–M4:**

- [ ] **Disposal:** on pipeline dispose and reload, dispose the breaker attachment (state provider +
      manual control; `CircuitBreakerSetup.Attach`, the factory's `Attachment`) and the rate-limiter
      wrapper `RateLimiterStrategyFactory.Wrapper` (the owned default `ConcurrencyLimiter`), as Polly does.
      Generated pipelines release attachments already (`GeneratedStrategy.Release` /
      `GeneratedPipelineSupport.ReleaseAttachments`), but still do not dispose the owned limiter.
- [ ] **State across reloads:** keep the breaker controller, limiter and adaptive-limiter state (all per
      `Build()`, in the factory) when a reload rebuilds strategies (PRD: "state survives a reload").
      Generated pipelines keep an unchanged section's strategy today; a changed section, and any hedging
      pipeline, resets state. Decide whether that is acceptable and say so in the PRD.
- [ ] **Breaker events:** keep the `EventSequencer` invariant: every transition passes through it, or none
      does, otherwise the next event deadlocks.
- [ ] **Hedging telemetry:** inner runs use `NoTelemetry`, so per-attempt telemetry (`ExecutionAttempt`,
      `OnHedging`) must be reported by the hedging strategy.
- [ ] **Chaos telemetry:** the chaos events have none yet.
- [ ] **Limiter telemetry:** events for rejected and for limit changed (adaptive).
- [ ] **Generated pipelines:** members pass an enabled `TTelemetry` to `__RunAsync` instead of
      `NoTelemetry`; delegated strategies get their telemetry from the builder.
- [ ] **DI seam:** `AddResiliencePipeline<T>()` registers through `IGeneratedResiliencePipeline<TSelf>`
      (`Create(IServiceProvider)`, `IsInstancePipeline`) and binds `IReloadableResiliencePipeline<TSelf,
      TOptions>` (`DefaultSectionPath`, `TryApply`) to configuration per S6 (change tokens +
      `IOptionsFactory.Create` in try/catch).
- [ ] Docs: README sections for DI, reload and telemetry (meter and instrument names).

**Done when:** registry resolution and a reload are wired in the sample app.

## Milestone 7 — HttpClient integration ⏳

- `MintPlayer.Resilience.Http`:
  - `ResilienceHandler<TPipeline> : DelegatingHandler`, usable with or without DI;
  - `IHttpClientBuilder.AddResilienceHandler<T>()` / `AddResilienceHandler(key)`;
  - `AddStandardResilienceHandler()` and `AddStandardHedgingHandler()` with Microsoft's defaults (PRD
    §2.5).
- Request replay with parity to Microsoft (S7):
  - retry re-sends the same message;
  - hedging snapshots it;
  - non-replayable content fails fast with a clear error.
- Per-request override and opt-out via `HttpRequestMessage.Options`.
- `Retry-After` honoured via `MintPlayer.Http`.

**Carry-over:**

- [ ] `Retry-After` awareness was left out of M1: add it through the retry `DelayGenerator`
      (`ShouldRetryAfterHeader`, not capped by `MaxDelay`, a past date means 0; PRD §2.5).
- [ ] The hedging preset is typed and forks; a generated hedging pipeline forwards to the interpreter
      (M4), so the handler works for both forms.
- [ ] `MintPlayer.Resilience.Http` package README and its entry in the repo README; any test project
      it adds sets `<IsPackable>false</IsPackable>`.

**Done when:** a typed client (DI) and a hand-composed `HttpClient` (no DI) both use the handler, and a
parity test pins the preset values against Microsoft's.

## Milestone 8 — Testing package & migration analyzer ⏳

- `MintPlayer.Resilience.Testing`: pipeline descriptor (`GetPipelineDescriptor()`), fake-time helpers,
  and helpers for asserting on chaos injection. The chaos strategies themselves live in the core (M3).
- MPR0001 Polly-migration code fix and MPR0003, informed by S7.

**Carry-over:**

- [ ] The MPR0001 code fix goes in the code-fix assembly (M4 part B). Its target is the attribute form:
      durations become `…Ms` integers, a null nullable duration becomes -1 (the default), and hooks bind by
      `nameof` or hook attributes. Snippets with an injected `ILogger` convert to the DI form.
- [ ] `MintPlayer.Resilience.Testing` package README and its entry in the repo README.

**Done when:** the code fix converts the three S7 snippets.

## Milestone 9 — Benchmarks, CI, docs ⏳

- `MintPlayer.Resilience.Benchmarks` against `Polly.Core [8.8.0]`, with a `Verification.Run()` fairness
  gate: the same scripted faults, and the same attempt counts and state transitions.
- `resilience-benchmark.yml`, nightly plus manual dispatch. It asserts allocation ceilings only (not
  wall-clock), like `assertions-benchmark.yml`.
- README with measured numbers only.
- Solution folder "Resilience" added to `MintPlayer.Dotnet.Tools.sln`. *(Already there for the four
  existing projects; add each new project to it.)*
- Versions set, and `eng/Assert-PackageVersions.ps1` passes.
- **Comprehensive demo** (owner requirement), e.g. `Resilience/MintPlayer.Resilience.Demo/`, with
  `<IsPackable>false</IsPackable>`. It uses the real library, not the spike code, and shows:
  - every spike finding: S1 flat vs interpreted vs Polly-shaped allocation on a suspending callback;
    S2 the pooled `ValueTask` and its opt-out; S3 a capturing call site and the MPR0002 fix; S4 the
    lock-free breaker under contention; S5 `TryExecuteAsync` rejection without an exception vs
    `ExecuteAsync`; S6 a reload that keeps in-flight values and rejects bad values; S7 the Polly-named
    builder and the HTTP presets; S8 telemetry on and off;
  - every feature: each strategy of PRD §2.3 on the runtime builder and as a `[ResiliencePipeline]`
    (static and DI forms, typed and generic), the beyond-Polly strategies, chaos behind a flag, DI and
    the registry, the `HttpClient` handler (with and without DI), and the Testing helpers.
- **Documentation up to date** (owner requirement):
  - every package README (`MintPlayer.Resilience`, `.Http`, `.Testing`) describes what is built, with
    measured numbers only;
  - the repo `README.md` package list includes every Resilience package (it lists none today);
  - the PRD and this plan match the code (statuses, *planned* markers removed, Outcome filled in).
- Delete `Resilience/spikes/` (throwaway, see M0).

**Done when:** the benchmarks run and meet PRD §4 criteria 1–5.

**Done when (demo):** the demo builds and runs end to end, and each spike finding and each feature listed
above has a section in it that prints its result.

**Done when (docs):** every package README, the repo README package list, the PRD and the plan are
checked against the code, with no stale claims and no feature missing.

## Final sweep (tests, once) ⏳

- Unit and behavioural-parity suite (fake `TimeProvider`).
- Generator tests: snapshot plus incrementality harness.
- Analyzer and code-fix tests.
- Native AOT publish of the sample (PRD §4.6).
- Run the demo end to end.
- Check that only the packages are packable: every test, demo and benchmark project sets
  `<IsPackable>false</IsPackable>`.
- Final documentation pass: READMEs, repo README package list, PRD, plan (and this plan's Outcome).

**Done when:** every suite passes, the AOT publish has zero trim/AOT warnings, the demo runs, and the
documentation pass finds nothing stale.

Then mark the single PR (draft #190) ready for review.

## Spike results

### S1 — PASS (2026-09-29)

**Setup:**

- Code: `Resilience/spikes/S1`.
- Environment: BenchmarkDotNet 0.14.0, .NET 11.0.0, Windows 11 x64 (AVX-512).
- Pipeline: fallback → timeout 10 s → retry ×3 (exponential, jitter, 0 delay) → circuit breaker → timeout 2 s.
- The fairness gate passed for all four implementations: same results, 2 attempts on one retry, 4 when
  every attempt fails.

**Results:**

| Workload | Polly 8.8.0 | Flat (generated shape) | Flat + pooling builder | Struct-nested chain |
|---|---|---|---|---|
| Sync happy path | 983 ns, 0 B | **278 ns (0.29×)**, 0 B | 269 ns (0.28×), 0 B | 312 ns (0.32×), 0 B |
| One retry (sync) | 1,962 ns, 0 B | **545 ns (0.28×)**, 0 B | 372 ns (0.19×), 0 B | 588 ns (0.30×), 0 B |
| Async (`Task.Yield`), total alloc | 1,697 B | 396 B | 289 B | 1,385 B |
| Async, **alloc above the callback's own 218 B** | **1,479 B** | **178 B (8.3× less)** | **71 B (21× less)** | 1,167 B (1.3× less) |

- **Async wall-clock is noise.** The error bars were ±4 µs, dominated by `Task.Yield` scheduling; only
  the allocation figures count there.
- **The sync numbers include two intrinsic `CancelAfter` timer operations,** in every column.

**Findings:**

1. **Go.** The flat method beats the stop condition (≥ 5× fewer async allocations) at 8.3×, and 21×
   with the pooling builder. Sync-path time is 0.29× Polly (target ≤ 0.5×). Flat = one state-machine
   box (178 B) → "≤ 1 box" met.
2. **The struct-nested chain is fast but does not deliver the allocation win.**
   - Sync time is within 12 % of flat, which meets the ≤ 20 % target.
   - But each layer's async slow path boxes separately when the callback suspends: 1,167 B, barely
     better than Polly.
   - So the runtime builder must not be a chain of async layers. **Decision for M1:** runtime pipelines
     use a single async interpreter method (one box) over strategies with synchronous hooks. The
     strategy structs expose `Before`/`After`/`ShouldRetry`-style hooks instead of wrapping `next`.
     This works for timeout, retry, circuit breaker, fallback, chaos and the limiters. Hedging keeps its
     own async executor (its allocations are intrinsic). Validate this interpreter with the M1
     benchmark.
3. **The pooling builder leaves 71 B/op on the async path.**
   - Likely cause: the thread-static CTS cache misses because the `Task.Yield` continuation resumes on
     another thread, falling through to `ConcurrentQueue`, or a per-thread builder cache.
   - Carried into S2 to explain before deciding default-on.
4. **Attribute order: the generator must require all strategy attributes on ONE declaration.**
   - Within one declaration, `GetAttributes()` returns source order across several attribute lists
     (`[1,2,3,4]`).
   - Across `partial` declarations, it follows the syntax-tree order of the compilation (`[1,2,3]` vs
     `[3,1,2]`), which is not something a user controls.
   - Strategy attributes split over two declarations → error diagnostic (new rule next to MPR0004).
     Explicit `Order =` is not needed.

### S2 — PASS (2026-09-29)

Code: `Resilience/spikes/S2`. It measures exact bytes per operation with
`GC.GetTotalAllocatedBytes(precise: true)` over 200,000 operations, after a 20,000-operation warm-up.

**Allocations** (bytes/op; the async callback's own box is 104 B in every case):

| Case | Sequential | 16 concurrent |
|---|---|---|
| Callback only | 104 | 104 |
| Plain builder wrapper | 240 (+136) | 240 |
| **Pooled builder wrapper** | **104 (+0)** | **104** |
| Pooled builder + 2 pooled CTS | 104 (+0) | 104 |
| Flat pipeline, plain builder | 280 (+176) | 280 |
| **Flat pipeline, pooled builder** | **104 (+0)** | **104** |

- The results are identical with the CTS thread-static cache off (shared queue only).
- **The S1 "71 B" was a BenchmarkDotNet artifact.** Steady-state cost is exactly 0 B above the
  callback, including under contention.
- **Correctness:** 64 × 20,000 concurrent pooled executions gave 0 wrong results.

**Misuse** (plain builder vs pooled builder):

| Misuse | Plain builder | Pooled builder |
|---|---|---|
| Await the same `ValueTask` twice (immediately, or after the box was re-rented) | works | `InvalidOperationException` |
| `.Result` before completion | works | `InvalidOperationException` |
| Two concurrent awaiters | works | **process crash**: the throw happens inside `OnCompleted` on the thread pool and cannot be caught |
| `.AsTask()` once, then await the `Task` twice | works | works |

No silent corruption was observed; a stale token is detected.

**Decision:**

- **The pooling builder is ON by default**, for generated pipelines and the runtime interpreter. It is
  the difference between 176 B and 0 B per async call.
- **Opt-out:** `[ResiliencePipeline(PooledAsync = false)]` and `RuntimeBuilder.UsePooledAsync(false)`.
- **Why it isn't free:** Polly's `ValueTask` is not pooled, so code migrated from Polly that breaks the
  `ValueTask` rules (double await, `.Result`, several awaiters) worked before and will now throw or
  crash.
- **Required mitigations, added to M4:**
  - A new analyzer rule, **MPR0006** (error): a result of a pipeline `Execute*Async` stored and then
    awaited more than once, read via `.Result` / `.GetAwaiter().GetResult()` without being awaited, or
    passed to `Task.WhenAll` / `WhenAny` without `.AsTask()`. It is modelled on CA2012, but as an error
    and scoped to our API.
  - Document the rule in the README's migration section.

### S3 — FAIL for interceptors; the technique moves to a code fix (2026-09-29)

Details in `Resilience/spikes/S3/RESULTS.md`, covering 31 shapes on net10 + net11.

**Why interceptors can't remove the closure:**

- An interceptor swaps only the call target. The caller has already built the display class and
  delegate when it evaluates the argument.
- A signature that takes state is rejected (CS9144).
- Escape analysis doesn't remove the allocation either.
- Measured: the interceptor ran on all 200k calls and saved 0 B.

**Bytes per call:** plain / intercepted / source-rewritten to static + state, net10 (net11):

| Capture | Plain | Intercepted | Rewritten |
|---|---|---|---|
| Captured local | 88 (80) | 88 (80) | **0** |
| `this` only | 64 (56) | 64 (56) | **0** |
| 2 locals + `this` | 104 (96) | 104 (96) | **0** |
| Async lambda / call site inside an `async` method | 88 (80) | 88 (80) | **0** |

**Rewrite boundary** (results verified identical to the closure):

- **In boundary:** a non-static lambda that captures only locals, value parameters or class `this`,
  where:
  - nothing it captures is written anywhere in the enclosing member;
  - the captured state is not used in nested functions;
  - there is no `base.` access and no call to a non-static local function.
- **Mutated captures must not be rewritten.** The naive rewrite gave wrong results (1 vs 2, 10 vs 11,
  11 vs 12).
- **Already rejected by the compiler:** struct `this`, ref/Span locals, and `ref`/`in`/`out` parameters.

**Decisions:**

- Interceptors are dropped. Closure removal is the MPR0002 code fix (M5 rewritten; PRD §1.2, §1.3, §2.1,
  §2.6, §4 and §5 corrected).
- The differentiator is honest now: every library allocates for a closure, and we offer a correct
  one-click rewrite.

**Other findings:**

- **Compiler floor:** a generator built against Roslyn 5.9 needs **SDK ≥ 10.0.4xx**. It fails with
  CS9057 on 10.0.112.
- **Caching:** `InterceptableLocation.Data` embeds the file checksum, which is moot now.
- **Packaging:** `InterceptorsNamespaces` from a package `.props` is overwritten by a user's csproj
  assignment (CS9137), whereas `buildTransitive/*.targets` survives. Keep this in mind for any
  package-set property.

### S4 — PASS (2026-09-29; throughput measured 2026-09-30)

Details in `Resilience/spikes/S4/RESULTS.md`.

**Design:**

- **Circuit state:** one `long` = state + a 14-bit generation (to prevent ABA) + break-until
  microseconds. Every transition is a CAS from the exact value the caller read, so exactly one thread
  wins each transition and fires its event.
  - Closed `TryEnter` is a single read, with no time lookup.
  - The half-open probe is simply the thread that wins Open → HalfOpen.
- **Health window:** a per-core striped, lock-free port of Polly's rolling windows.
- **Slow-call ratio** (beyond Polly) fits the same design: a third counter, and a slow probe counts as a
  failed probe.
- **Baseline:** `Locked.cs` is a line-for-line port of Polly's controller under a lock, used as the fair
  comparison.

**Correctness** (all pass):

- **Transition tests:** deterministic, with `FakeTimeProvider`.
- **Parity with Polly:** 3,000 random scripts, 750,000 operations and 34,158 transitions, including late
  outcomes and manual control. After every operation, Polly 8.8.0, Locked and LockFree had identical
  state and admission.
- **Race fuzzing:**
  - A: 1.28 M `TryEnter` calls racing at the half-open instant admitted exactly 1 probe in each of
    20,000 rounds.
  - B: 1.28 M operations with 21,519 transitions formed one legal chain in every round, with no lost or
    duplicated transitions.
  - C: 2.4 M counter records, with no lost increments.
- **Mutation testing:** three deliberately broken variants were all caught.
- **Untested gap:** one guard that only matters when a thread stalls across more than 16 window starts.
  It is covered by argument in RESULTS.md, not by a test.

**Deliberate deviation from Polly:** the health window is also cleared when the probe is admitted, not
only on close. Without that, a call admitted right after closing could re-trip the circuit on the old
failures. It is only observable through the break-duration generator's arguments.

**Throughput** (BDN, 2026-09-30): mean time per closed-state `TryEnter` + `Record`, all 0 B.

| Threads | Failures | Polly | Locked (Polly's logic, under a lock) | **LockFree** |
|---|---|---:|---:|---:|
| 1 | 0 % | 146 ns | 49 ns | **25 ns** |
| 1 | 5 % | 147 ns | 50 ns | **33 ns** |
| 4 | 0 % | 351 ns | 151 ns | **8 ns** |
| 4 | 5 % | 359 ns | 127 ns | **11 ns** |
| 16 | 0 % | 361 ns | 163 ns | **5 ns** |
| 16 | 5 % | 350 ns | 140 ns | **11 ns** |

- **Pass.** LockFree beats Locked in every case, and by 12–32× under contention.
- **The feared 5 % failure case** costs about 2× the failure-free case, but is still 12× faster than
  Locked.
- **Decision:** the lock-free controller ships (M2).

### S5 — PASS (2026-09-29; timing measured 2026-09-30)

Details in `Resilience/spikes/S5/RESULTS.md`. The table shows bytes per rejected call, over 200k calls:

| Variant | Circuit open | Concurrency limiter | Fixed-window limiter |
|---|---:|---:|---:|
| Ours `TryExecuteAsync` | **0** | **0** | 64 (BCL lease + `RetryAfter` box) |
| Ours `ExecuteAsync`, fresh exception | 752 | 688 | 752 |
| Polly `ExecuteOutcomeAsync` | 200 | 17,300 | 17,400 |
| Polly `ExecuteAsync` (throws) | 1,266 | 24,370 | 24,690 |

**Polly's rate-limiter rejection costs 17–25 KB.** The cause is `TrySetStackTrace()`, a full stack walk
on every rejection (`RateLimiterResilienceStrategy.cs:77`).

**Caching the exception is unsafe, and saves only about 150 B:**

- The runtime still allocates a stack-trace array on every throw.
- Each throw rewrites the shared instance's stack.
- `TargetSite` stays stuck on the first throw site.
- Under 8 concurrent threads:
  - about 11 % of catches logged another request's stack;
  - about 22 % showed mixed stacks;
  - `Data` entries leaked between callers.
- Repeated `ExceptionDispatchInfo` rethrows grow the trace without bound.

**Decision** (applied in PRD §2.4):

- **`TryExecuteAsync`:** the outcome carries a rejection kind and `RetryAfter`, with no exception
  reference.
- **`ExecuteAsync`:** throws a fresh exception per rejection.
- **`Outcome.Exception` on a rejection:** created lazily when read.

**Fixed-window 64 B:** the lease object comes from the BCL.

- Accepted for the `System.Threading.RateLimiting`-backed strategy.
- Our own limiters (the adaptive concurrency limit, and a native fixed/sliding window) are written
  lease-free in M3.

**Timing** (BenchmarkDotNet, sequential run, .NET 11), mean time per rejected call:

| Variant | Circuit open | Concurrency limiter | Fixed-window limiter |
|---|---:|---:|---:|
| **Ours `TryExecuteAsync`** | **59 ns** | **30 ns** | **47 ns** |
| Ours `ExecuteAsync` (fresh throw) | 2,682 ns | 2,598 ns | 2,633 ns |
| Polly `ExecuteOutcomeAsync` | 158 ns | 36,135 ns | 39,970 ns |
| Polly `ExecuteAsync` (throws) | 3,710 ns | 58,141 ns | 57,014 ns |

- **The ≤ 250 ns target is met with 4× headroom.** It is 2.7× faster than even Polly's non-throwing
  path.
- **Rate-limited rejections:** 30–47 ns against Polly's 36–58 µs, which is 800–1,900× faster. The gap
  is Polly's per-rejection stack walk.
- **A cached throw is no faster than a fresh one** (2,673 vs 2,682 ns), which confirms the decision to
  allocate per throw.

### S6 — PASS (2026-09-29; timing measured 2026-09-30)

Details in `Resilience/spikes/S6/RESULTS.md`.

**Variants tested:**

- **A:** constants folded in, no reload.
- **B:** a `Volatile.Read` class snapshot, read once at entry.
- **C:** a struct snapshot copied into the frame.

**Allocation:** all three are 0 B/op on the sync path, and +0 B on the async path (the snapshot is
hoisted into the pooled state machine for free).

**Correctness:**

- **In-flight executions keep their starting values.** After a reload (retries 3 → 1, timeout
  2 s → 50 ms), the next call used the new values.
- **No mixed values under concurrency.** 16 workers × 20,000 executions across 7,043 reloads, with
  zero executions seeing a mix of old and new values.
- **Circuit-breaker health state survives a reload.**
- **A removed JSON key falls back to the attribute default.**

**Problems found and fixed in the design:**

1. **`IOptionsMonitor.OnChange` binding is fragile.** An unconvertible value throws from `Reload()`
   before any listener runs. Binding therefore listens to the change tokens and calls
   `IOptionsFactory.Create` inside try/catch: the bad value is rejected and the old snapshot stays live.
2. **A duplicate `AddResiliencePipeline` call bound the section twice.** It is made idempotent.
3. **A `static` class cannot be a type argument (CS0718).** Declarative pipelines are `sealed partial`
   classes (PRD §2.1 fixed).
4. **Two racing reloads could publish out of order.** They are serialized with a lock.

**Design notes:**

- A static pipeline means one configuration per process. This is accepted, and documented. Use the
  non-static DI form for per-container configuration.
- **Decision:** variant B. C copies ~64 B per execution for no measurable gain.
**Timing** (BDN, 2026-09-30), sync-completing callback, all 0 B:

| Variant | Mean |
|---|---|
| A (constants) | 178.2 ns |
| B (class snapshot) | 182.7 ns (**+4.5 ns**) |
| C (struct snapshot) | 181.8 ns (+3.6 ns) |

- **Pass, narrowly.** The ≤ 5 ns gate is met.
- **The difference is within the error bars** (±3.6 ns), so treat it as "a few ns".
- **Reload stays opt-in per pipeline**, as planned.

### S8 — PASS (2026-09-29; timing measured 2026-09-30)

Details in `Resilience/spikes/S8/RESULTS.md`, including the names table.

**Event parity:**

- Both libraries emit the same events per successful execution: 3 metric operations, 3 log calls and
  4 timestamps.
- The one-retry and always-fail runs match line for line.
- Our circuit-breaker, rejection and timeout events are checked as well.

**Allocation, telemetry on** (bytes/op):

| | Sync | One retry | Async (above the callback) | Held-open circuit |
|---|---|---|---|---|
| Ours (no-op listener, real OpenTelemetry SDK, or logging at Information) | **0** | **0** | **+0** | **0** |
| Polly | 48 | 96 | +240 | 824 |

- **Polly boxes the result for every outcome-carrying event, even with no listener.** It formats the
  result before checking whether logging is enabled. Polly's own benchmark hides this because its result
  is a `string`.
- **Tracing, when a tracer is attached,** costs us about 560 B (the `Activity`). Polly has no tracing.

**Naming** (applied in PRD §2.4):

- The defaults are Polly's names without the `polly.` segment, plus two OpenTelemetry-guided changes:
  durations in seconds, and `error.type` instead of `exception.type`.
- The opt-in `PollyCompatible` scheme was verified tag-for-tag against Polly's output.
- There are no OpenTelemetry semantic conventions for resilience yet. Re-check the registry before
  shipping.

**Telemetry off:**

- Generated pipelines: telemetry is compiled out.
- Runtime pipelines, as measured: about 7 null checks on the happy path.
- **Decision for M1:** make the interpreter generic over a telemetry struct, so the no-op version is
  JIT-eliminated.

**Timing** (BDN, 2026-09-30), happy path. The overhead is On − Off:

| Listener | Ours off | Ours on | **Ours overhead** | Polly off | Polly on | Polly overhead |
|---|---:|---:|---:|---:|---:|---:|
| **Meter, no-op** (the gate row) | 187 ns | 267 ns | **+80 ns** | 693 ns | 935 ns | +243 ns (48 B) |
| Real OpenTelemetry SDK | 175 ns | 674 ns | +498 ns | 694 ns | 1,519 ns | +825 ns (48 B) |
| Meter + logging at Information | 178 ns | 331 ns | +153 ns | 735 ns | 1,106 ns | +370 ns (48 B) |
| One retry, meter no-op | 328 ns | 480 ns | +152 ns | 1,181 ns | 1,643 ns | +462 ns (96 B) |

- **Pass.** The overhead is +80 ns against the ≤ 120 ns gate, 3× less than Polly's, at 0 B.
  `Ours_On_Lean` (without the redundant executing counter) is +65 ns.
- **Runtime telemetry off costs nothing measurable:** 181–189 ns, the same as compiled-out.
- **With telemetry on, our whole pipeline still costs less than Polly's with telemetry off** in every
  row: 267 vs 693 ns, and 674 vs 694 ns with the real OpenTelemetry SDK.

### S7 — PASS (2026-09-29)

Details in `Resilience/spikes/S7/RESULTS.md`, with three before/after snippets.

**Preset defaults:**

- Verified against the dotnet/extensions source (398edf6) and Polly (`git diff 8.8.0` is empty on every
  file that holds a default).
- PRD §2.5 is corrected: 3 *retries* = 4 attempts, status ≥ 500, and the connection-timeout OCE is
  handled.
- The circuit breaker uses a different predicate from retry.
- The `Retry-After` nuances and the hedging preset's two-handler shape are now documented.

**Request replay:**

- The PRD's claim that Microsoft clones the request was wrong. The standard handler re-sends the same
  `HttpRequestMessage`; only hedging snapshots it.
- **Decision (parity):** do the same, and fail fast on non-replayable content.

**Naming:**

- **Decision:** mirror Polly v8 type, option, extension and exception names one-for-one.
- The PRD names `ResiliencePipeline.For<T>()`, `RetryOptions`, `MaxAttempts` and `SamplingSeconds` are
  replaced.
- Attributes use `…Ms` integer properties, because a `TimeSpan` is not a valid attribute argument.

**Migration diff:** 9 items, on one screen (the Pass criterion):

1. Namespace swap.
2. Predicates are synchronous `bool`; `PredicateBuilder` converts implicitly.
3. Delay generators are synchronous.
4. Events and `FallbackAction` stay `ValueTask`.
5. The argument structs are the same.
6. Attributes use `…Ms` values and `nameof` hooks.
7. The returned `ValueTask` is pooled (MPR0006).
8. `TryExecuteAsync` returns an `Outcome` on rejection.
9. The rest are additions.

**MPR0001 feasibility:**

- **Mechanical conversion to a `[ResiliencePipeline]` class:** a single fluent chain or a static
  configure lambda, with every value a constant or `TimeSpan.FromX(const)`, predicates as
  `PredicateBuilder` chains or non-capturing lambdas, and hooks that don't capture.
- **Conversion to the runtime builder:** config or options values, captured lambdas, conditional
  builders, reload.
- **No fix:** awaiting predicates, custom strategies, telemetry plumbing, Polly types in the user's
  public API.

**Gap found:**

- Real hooks log through an injected `ILogger`, so 2 of the 3 snippets need a non-static declarative
  pipeline.
- **Decision:** the DI form is added to PRD §2.1 and M4.

## Outcome

_(filled in at completion: defects found, benchmarks, target met / not met)_
