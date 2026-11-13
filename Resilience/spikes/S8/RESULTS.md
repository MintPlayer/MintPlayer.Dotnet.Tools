# S8: telemetry cost — results

**Question:** can telemetry, when on, cost ≤ 120 ns per execution? When off, it must cost 0 ns for
generated pipelines (compiled out) and one branch for runtime ones. Polly 8.8.0 adds ~230 ns (its
own `TelemetryBenchmark`).

**Status:** the design, the names, event parity with Polly and the allocation results are done.
**The timing verdict is still open:** the BenchmarkDotNet class is written but has not been run,
because other spikes were running in parallel.

```
cd Resilience\spikes\S8
dotnet run -c Release                 # Part 1: events emitted (ours vs Polly); Part 2: exact bytes/op
dotnet run -c Release -- --bench      # BDN: HappyPathTelemetryBenchmarks (4 sinks x 6 methods) + OneRetryTelemetryBenchmarks
```

**Pass criterion in the BDN output:** at `Sink=MeterNoOp`, `Ours_On − Ours_Off ≤ 120 ns`. That sink is
Polly's own ~230 ns setup: a no-op `MeterListener` on every instrument, and `NullLoggerFactory`.
Compare it with `Polly_On − Polly_Off` in the same row.

## 1. Names

Polly's names come from `Polly.Extensions/Telemetry/TelemetryListenerImpl.cs`, `ResilienceTelemetryTags.cs`,
`Log.cs` and `TelemetrySource.cs` (at 8.8.0+15). The Part 1 output shows them live.

| | Polly 8.8.0 | **Ours: `TelemetryNames.Default`** | Ours: `TelemetryNames.PollyCompatible` |
|---|---|---|---|
| Meter / ActivitySource | `Polly` (version = assembly version); no ActivitySource | `MintPlayer.Resilience` (Meter + ActivitySource) | `Polly` |
| Event counter | `resilience.polly.strategy.events`, `Counter<int>`, no unit | `resilience.strategy.events`, unit `{event}` | Polly's name, no unit |
| Attempt duration | `resilience.polly.strategy.attempt.duration`, `Histogram<double>`, `ms` | `resilience.strategy.attempt.duration`, **`s`**, bucket advice | Polly's name, `ms` |
| Pipeline duration | `resilience.polly.pipeline.duration`, `Histogram<double>`, `ms` | `resilience.pipeline.duration`, **`s`**, bucket advice | Polly's name, `ms` |
| Tags | `event.name`, `event.severity`, `pipeline.name`, `pipeline.instance`, `strategy.name`, `operation.key`, `exception.type`, `attempt.number`, `attempt.handled` | same keys, except **`error.type`** replaces `exception.type` | identical to Polly |
| `event.name` values | `PipelineExecuting`, `PipelineExecuted`, `ExecutionAttempt`, `OnRetry`, `OnTimeout`, `OnFallback`, `OnCircuitOpened`, `OnCircuitClosed`, `OnCircuitHalfOpened`, `OnHedging`, `OnRateLimiterRejected`, … | same, plus `OnCircuitRejected` (Polly has no rejection event) | same |
| `event.severity` | `None`/`Debug`/`Information`/`Warning`/`Error`/`Critical` | same | same |
| Logger category | `Polly` | `MintPlayer.Resilience` | `Polly` |
| Log events (id:name) | 0:`ResilienceEvent` (every strategy event), 1:`StrategyExecuting`, 2:`StrategyExecuted`, 3:`ExecutionAttempt` | 1:`PipelineExecuting`, 2:`PipelineExecuted`, 3:`ExecutionAttempt`, 10:`OnRetry`, 11:`OnTimeout`, 12:`OnFallback`, 20:`OnCircuitOpened`, 21:`OnCircuitClosed`, 22:`OnCircuitHalfOpened`, 23:`OnCircuitRejected` | same as Default |

**Semantic conventions:**

- We know of no OpenTelemetry semantic conventions for resilience (retry, circuit breaker, timeout). The
  only retry-related attribute we know of is HTTP's `http.request.resend_count`. **Check the current
  semconv registry again before M-telemetry ships.**
- The OTel conventions that do apply are:
  - durations in seconds, as `double`;
  - `error.type` as the stable error attribute;
  - explicit bucket advice. OTel's default buckets (0…10000) assume milliseconds, so they collapse a
    seconds histogram.

**Decision:**

- **Default:** our own meter, with Polly's semantic names minus the `polly.` segment and Polly's tag
  keys. Only two things change from Polly, both to follow OTel: seconds, and `error.type`.
- **Opt-in `PollyCompatible` naming:** for teams migrating existing dashboards and alerts. Part 1 shows
  that its instrument names, units, tag keys, tag values and tag order match Polly's exactly.
- **Why the naming choice is free:** names are bound once, when a pipeline is built. Changing the
  scheme costs nothing per execution.

## 2. Design (`Telemetry.cs`, `TelemetryPipeline.cs`, `Log.cs`)

- **Shared instruments:** one `ResilienceInstruments` per naming scheme (Meter, ActivitySource, and
  the three instruments), shared by every pipeline.
- **`PipelineTelemetry` per pipeline instance.** It pre-builds every tag array as a `KeyValuePair[]`:
  - pipeline executing/executed;
  - attempt × {unhandled, handled, handled-last}, for attempts 0…MaxRetries;
  - one array for each strategy event.

  A measurement is then `if (instrument.Enabled) instrument.Record(value, prebuiltArray)`: no
  `TagList` build, no boxing (booleans and `attempt.number` are boxed once, when the arrays are built).
- **Rare paths:**
  - An exception adds `error.type` through a `TagList` built from the pre-built array; 6–8 tags stay
    inline, so there is no allocation.
  - Attempts beyond 16 use the same fallback.
- **Durations:** `Stopwatch.GetTimestamp()` deltas × a precomputed ticks→unit factor. The happy path
  takes 4 timestamps, the same as Polly.
- **Logging:** `[LoggerMessage]` source-gen with a fixed level where possible, so a disabled level
  costs one `IsEnabled` call.
  - No `{Result}` payload. Polly's `object? result` boxes value-type results; see §4.
  - `OnCircuitRejected` logs at `Debug`, because an open circuit rejects every call; the counter
    carries the rate.
- **Tracing:**
  - `ActivitySource.HasListeners()` gates an opt-in `resilience.pipeline` span per execution.
  - Strategy events are added as `ActivityEvent`s to `Activity.Current` only when
    `IsAllDataRequested`.
  - Polly has no tracing, so this is outside the fairness budget.
- **Off:**
  - **Generated pipelines:** telemetry off means the generator emits S1's `FlatPooledPipeline`
    unchanged. The spike measures that exact class as `Ours_Off`: 0 ns and 0 B by construction.
  - **Runtime pipelines, as measured:** `FlatTelemetryPipeline` holds a nullable `PipelineTelemetry`.
    That is one predicted null-check per call site (about 7 on the happy path), measured as
    `Ours_RuntimeOff`.
  - **Recommendation for M1's interpreter:** make it generic over
    `TTelemetry : struct, IPipelineTelemetry`, with a `NoTelemetry` struct. The JIT then removes the
    calls entirely, which gives "compiled out" for runtime pipelines too. The alternative is Polly's
    pattern: one check at entry that picks the plain or the instrumented execute method. Either meets
    "one branch".

## 3. What fires per execution (fairness)

Part 1 captured every measurement and log call for our pipeline and Polly's. Both are fallback →
timeout → retry → breaker → timeout, named `catalog`/`default`.

**Happy path: both emit exactly the same set.**

| # | Metric (tags) | Log |
|---|---|---|
| 1 | `strategy.events` +1 `[PipelineExecuting/Debug]` (4 tags) | id 1, `Debug` |
| 2 | `attempt.duration` `[ExecutionAttempt/Information #0 handled=False]` (7 tags) | id 3, `Information` |
| 3 | `pipeline.duration` `[PipelineExecuted/Information]` (4 tags) | id 2, `Information` |

So the budget counts **3 instrument operations + 4 timestamps + 3 log-level checks**, the same as Polly.

**Notes:**

- The attempt event comes from the retry strategy. A Polly pipeline without retry or hedging emits
  only #1 and #3.
- **`Ours_On_Lean`** drops #1. It is redundant: `pipeline.duration`'s count is the execution count.
  It is not in the fair comparison.
- **One retry:** both emit attempt #0 handled (Warning), `OnRetry`/Warning, attempt #1 (Information),
  and PipelineExecuted.
- **Always fail:** both emit 4 attempts (the last at Error), 3 × `OnRetry`, and `OnFallback`. Our
  output matches Polly's line for line.
- **Our breaker and timeout events also verified:**
  - `OnCircuitOpened`/Error at the 10th failure, then `OnCircuitRejected`;
  - after the break, `OnCircuitHalfOpened`/Warning, then `OnCircuitClosed`/Information;
  - `OnTimeout`/Error × 4 with `error.type=System.TimeoutException`.
- **Polly logs 2 lines at Information for every successful execution.** At the default ASP.NET
  `Information` level, that is a log line pair per call. **Recommendation:** ship the happy-path
  events (unhandled `ExecutionAttempt`, `PipelineExecuted`) at `Debug` in `Default` naming, and keep
  Polly's levels in `PollyCompatible`.

## 4. Allocations

Exact bytes/op (`GC.GetTotalAllocatedBytes(precise: true)`, 200,000 ops after a 20,000-op warm-up).
The `Async` column includes the callback's own 104 B box.

| Variant | Sync (None / MeterNoOp / OTel SDK / +LogInfo) | OneRetry (all 4 sinks) | Async (all 4 sinks) | + tracing listener (Sync / OneRetry / Async) |
|---|---|---|---|---|
| Ours off (generated) | 0 | 0 | 104 | 0 / 0 / 104 |
| Ours runtime, telemetry = null | 0 | 0 | 104 | 0 / 0 / 104 |
| **Ours on** | **0** | **0** | **104 (+0)** | 564 / 624 / 640 (the `Activity`) |
| Polly off | 0 | 0 | 1,855 | 1,855 |
| **Polly on** | **48** | **96** | **2,094 (+240)** | same |
| Open circuit, all sinks: ours on / Polly on / Polly off | 0 / 824 / 800 | | | |

**Findings:**

1. **Ours allocates 0 B per operation with telemetry on,** whatever is listening:
   - with a no-op MeterListener;
   - with the real OpenTelemetry SDK aggregating (1.15.3 `MeterProvider`);
   - with logging enabled at Information through a real `LoggerFactory`;
   - on the async path, where the pooled builder still holds;
   - on the open-circuit path, including the `error.type` tags.
2. **Polly with telemetry allocates 24 B for every event that carries an outcome, even when nobody
   listens.**
   - `TelemetryListenerImpl.LogEvent` calls `GetResult` → `_resultFormatter(context, (object)result)`
     *before* the logger checks `IsEnabled`. Every `Outcome<int>` is boxed.
   - The happy path boxes twice: `ExecutionAttempt` + `PipelineExecuted`.
   - Polly's own benchmark does not show this, because its result is a `string`.
3. **Tracing costs ~560 B per execution** (an `Activity` and its tags). It only happens when a tracer
   samples our source, so it stays opt-in.

## 5. Recommendation

- **Adopt this design for M-telemetry:**
  - pre-built tag arrays per pipeline/strategy instance;
  - `Enabled`-guarded `Record`/`Add`;
  - `[LoggerMessage]` with no result payload;
  - the `HasListeners` gate;
  - `Default` names with an opt-in `PollyCompatible` scheme.
- **Allocations: met,** at 0 B/op with telemetry on and any sink, vs Polly's 48–240 B.
- **Off = 0 for generated pipelines: met by construction.** For runtime pipelines, go generic over a
  telemetry struct.
- **The 120 ns time budget is not measured yet. Run `--bench`.**
  - Read `Ours_On − Ours_Off` at `Sink=MeterNoOp`; it must be ≤ 120 ns.
  - At `Sink=OTel`, it shows the realistic cost, where the SDK's tag lookup and bucket search dominate.
    That cost belongs to the sink, and Polly pays it too.
  - If the result is over budget, try these first:
    - `Ours_On_Lean`, which drops the redundant executing counter;
    - reusing the attempt-end timestamp as the pipeline-end timestamp when no strategy runs after the
      retry.
