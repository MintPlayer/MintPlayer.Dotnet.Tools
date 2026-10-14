# S6: reload for generated pipelines

**Status:** correctness and allocation checks PASS. The timing benchmark (the ≤ 5 ns gate) is written but has
**not been run yet**.

- Correctness and allocations: `dotnet run -c Release`
- Timing: `dotnet run -c Release -- --bench` (BenchmarkDotNet 0.14.0, `ReloadBenchmarks`)

## Variants

All three are copies of S1's flat pipeline: timeout 10 s "Total", retry ×3, circuit breaker, timeout 2 s
"Attempt". They all use the pooling builder, the same `Breaker`, and the same `CtsPool`. The only difference is
where the numbers come from.

| | Where the options come from | Extra work per execution |
|---|---|---|
| **A** `ConstPipeline` | `static readonly` / `const` (JIT constants at tier 1). No reload. | none |
| **B** `CatalogPipeline` | `static CatalogSnapshot s_current` (an immutable class). It is read **once** at entry with `Volatile.Read`, then its fields are read. | 1 load, plus dependent field loads |
| **C** `StructPipeline` | `readonly struct CatalogValues` in a holder. It is copied into the frame / state machine at entry. | 1 load + a ~64 B copy |

On x64, `Volatile.Read` compiles to a plain `mov`. It is kept so that the JIT cannot hoist the read out of a
caller's loop once `ExecuteAsync` is inlined.

The breaker thresholds are a `BreakerSettings` struct. They are passed as `in` to `Record` on each call, so the
controller holds only health state.

## Measured (checks run on 2026-09-29)

**Allocations, sync-completing callback:**

- A, B and C are all **0.00 B/op**. This is measured with `GC.GetAllocatedBytesForCurrentThread` over 200 k ops,
  which is exact because this path never leaves the calling thread.
- The process-wide counter shows 0.000–0.13 B/op. That comes from background threads (tiered JIT, timers), not
  from the pipeline.

**Allocations, async-suspending callback** (`Task.Yield`), process-wide:

- A, B and C are all 104.01 B/op.
- That equals the callback's own box, so the pipelines add **+0.00**.
- Hoisting the snapshot into the state machine costs nothing, because the pooled box is reused (see S2).

**Correctness** (all PASS; `Program.cs`, tests 1–5):

1. **In-flight vs next execution.**
   - Setup: E1 is parked inside its first attempt. Configuration is then reloaded: `MaxRetries` 3→1 and
     `Attempt:Timeout` 2 s→50 ms.
   - The next execution makes 2 attempts. Another execution times out at 50 ms: 2 attempts in 164 ms.
   - After release, E1 finishes with **4 attempts**. Its post-reload 200 ms attempts are **not** cancelled, so E1
     kept its entry snapshot for everything.
2. **Invalid reloads.**
   - Semantic errors (`MaxRetries = -1`) and binder errors (`"abc"`) are both rejected.
   - The previous snapshot stays live, the failure is reported exactly once, nothing throws out of
     `IConfigurationRoot.Reload()`, and the next valid reload applies.
3. **Circuit breaker.**
   - 5 failures are counted under `MinimumThroughput=100`. A reload then sets it to 6, and the 6th failure opens
     the circuit, so the counts survived the reload and the new threshold applied.
   - A later unrelated reload does **not** close the open circuit.
4. **Concurrency.**
   - 16 workers × 20,000 executions ran while a thread toggled `MaxRetries` 1/3. There were 7,043 reloads.
   - Every execution made exactly 2 or 4 attempts (232,906 / 87,094 / **0 other**), so no execution saw a torn
     snapshot or a changed retry count mid-run.
5. **Reloadable JSON file.**
   - An edit applies through the file watcher.
   - A **removed** key falls back to the attribute default (3), not to the last value. This is because
     `OptionsFactory` builds a fresh instance whose initializers are the attribute values.
   - An unconvertible value in the file is rejected, the process stays alive, and the watcher still applies the
     next write.

**Compile check:** `-p:DefineConstants=CHECK_CS0718` gives **CS0718**: *static types cannot be used as type
arguments*. So `AddResiliencePipeline<CatalogPipeline>()` cannot work with the PRD's `static partial class`.

## Findings

1. **Reads once at entry, and in-flight executions keep their snapshot.**
   - Immutability gives this for free. There is no locking, no tracking and no drain.
   - Polly, by contrast, rebuilds the whole pipeline on reload (`ReloadableComponent` calls the builder
     callback again). It keeps every pipeline wrapped in `ExecutionTrackingComponent`, an Interlocked
     increment/decrement per execution, so it can poll every 1 s until in-flight executions drain before it
     disposes the old one. We need none of that: an old snapshot is simply garbage once no execution references it.
2. **Circuit-breaker health survives a reload** (a deliberate break from Polly).
   - Polly's rebuild creates a fresh breaker, which is *closed*. So a config reload during an outage re-opens the
     floodgates.
   - We keep the state, window counts and running break. New thresholds apply from the next `Record`.
   - An already-running break keeps the `BreakDuration` it opened with.
   - Consequence (seen in the allocation section): lowering `MinimumThroughput` while many failures are already
     counted opens the circuit on the next call. That is correct, and should be documented.
   - A future `ResetCircuitOnReload` opt-in is just `Breaker.Reset()`. It is not needed for v1.
3. **Do not bind through `IOptionsMonitor.OnChange`.**
   - `OptionsMonitor` rebuilds the options *before* it invokes listeners. A binder error therefore throws an
     `AggregateException` out of `IConfigurationRoot.Reload()`, and the listener never runs (test 2b, kept as
     `MonitorReloadBinding`).
   - The same applies to `OptionsBuilder.Validate()`, which is why neither binding uses it.
   - The recommended `ReloadBinding` instead listens to the same `IOptionsChangeTokenSource<TOptions>` and calls
     `IOptionsFactory<TOptions>.Create` inside `try/catch`.
   - It validates in the generated `TryApply`, and on failure it reports the error and keeps the old snapshot.
     Startup still fails fast, with `ValidateOnStart` semantics.
4. **Registration must be idempotent.** The first run of this spike called `AddResiliencePipeline` twice. That
   produced two `BindConfiguration`s, so two change sources, so every reload was applied twice. The guard now
   checks for the binding's service type.
5. **One configuration per process.**
   - The generated class is static, so only one service provider may bind it at a time. Test 5 has to stop the
     first provider's binding first.
   - Tests that build several hosts must dispose them in between.
   - A pipeline that needs per-provider or per-tenant options should use a keyed runtime pipeline instead (§2.2).
   - The analyzer or docs should say this.
6. **Concurrent `TryApply`.** Two reloads that race can publish in either order, and the last writer wins. The
   package should serialize `TryApply` with a lock; it is on the reload path, not the hot path.
7. **C versus B.**
   - C removes nothing measurable in allocations.
   - It adds a ~64 B copy per execution, plus ~64 B of state-machine size (pooled, so free in bytes).
   - Pick **B** unless the benchmark shows C ahead by more than noise. B also gives a stable identity (`Version`)
     for telemetry ("pipeline reloaded, v12").

## Proposed API

```csharp
[ResiliencePipeline(Reloadable = true)]                 // ConfigurationSection defaults to "Resilience:CatalogPipeline"
[Timeout(Seconds = 10, Name = "Total")]
[Retry(MaxRetries = 3)]
[CircuitBreaker(FailureRatio = 0.5, MinimumThroughput = 10, SamplingSeconds = 30, BreakSeconds = 15)]
[Timeout(Seconds = 2, Name = "Attempt")]
public sealed partial class CatalogPipeline;            // NOT static: CS0718 + static abstract interface members

services.AddResiliencePipeline<CatalogPipeline>();                                  // default section
services.AddResiliencePipeline<CatalogPipeline>("MyApp:Downstream:Catalog");        // override
```

```json
{ "Resilience": { "CatalogPipeline": {
    "Total":   { "Timeout": "00:00:10" },
    "Retry":   { "MaxRetries": 1, "Delay": "00:00:00.200" },
    "CircuitBreaker": { "FailureRatio": 0.5, "MinimumThroughput": 10 },
    "Attempt": { "Timeout": "00:00:00.500" } } } }
```

**The generator emits, for a reloadable pipeline:**

- **An options class, `CatalogPipelineOptions`.**
  - It has one property per strategy, typed as the runtime package's `TimeoutStrategyOptions` /
    `RetryStrategyOptions` / `CircuitBreakerStrategyOptions` / ….
  - Initializers = the attribute values, so an absent key means the attribute default.
  - Property name = the attribute's `Name`, else the strategy kind. Two unnamed strategies of the same kind are a
    **generator error**, because their config keys would be ambiguous.
  - There is a generated `Validate()`.
  - Bind with the configuration-binding source generator (`EnableConfigurationBindingGenerator`) for AOT.
- **A snapshot class.** It has `readonly` fields and a `Version`. The static `s_current` is initialized from the
  attribute defaults, so the pipeline works before or without DI.
- **The execute method.** `ExecuteAsync` reads `s_current` once at entry. Circuit-breaker and limiter
  controllers stay `static readonly`, outside the snapshot.
- **Interface glue.**
  - `IReloadableResiliencePipeline<CatalogPipelineOptions>`: `static abstract DefaultSectionPath`,
    `TryApply(options, out error)`, and `AddServices(services, section)`.
  - `AddResiliencePipeline<TPipeline>() where TPipeline : IGeneratedResiliencePipeline` just calls
    `TPipeline.AddServices`. That is how the single-type-argument call reaches the typed options.
- **For `Reloadable = false`, constants are folded as in A.** There is no options class, `AddServices` only adds
  the registry entry, and configuring the options for it is a diagnostic.

**How DI connects to the static class:**

- `AddServices` → `AddOptions<TOptions>().BindConfiguration(section)`, plus a singleton `ReloadBinding`
  registered as an `IHostedService`.
- On start, the binding applies `factory.Create()` and fails fast if the options are invalid.
- It then subscribes to the change-token sources and calls `TPipeline.TryApply` on every change.
- Apps without a host call `serviceProvider.ActivateResiliencePipelines()`.
- Disposing the provider unsubscribes the binding.

## Files

- `Pipelines.cs`: A / B / C.
- `Options.cs`: the generated options class and the snapshot types.
- `Breaker.cs`: the breaker, with thresholds passed per call.
- `Registration.cs`: the DI extension, `ReloadBinding`, and `MonitorReloadBinding`.
- `Program.cs`: the tests and allocation checks, plus `--bench`.
- `Benchmarks.cs`: the BenchmarkDotNet class.
- `StaticCheck.cs`: the CS0718 probe.
