# Plan — MintPlayer.Resilience

Companion to [Resilience-prd.md](Resilience-prd.md).

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

## Milestone 1 — Runtime core ⏳

- `Outcome<T>`, `ResilienceContext` + pool, `ResiliencePropertyKey<T>`, `TimeProvider` plumbing.
- Strategy structs: Retry (all backoff types, jitter formulas matching Polly's decorrelated jitter),
  Timeout (pooled CTS), Fallback.
- The runtime builder in the shape S1 chose (struct-nested, plus the array fallback).

**Done when:** it compiles for net10/net11 with `IsAotCompatible` and no warnings.

## Milestone 2 — Circuit breaker ⏳

- Controller per S4: sliding health window, half-open probing, manual control, state provider.
- `BrokenCircuitException` / `IsolatedCircuitException`, cached per S5.

**Done when:** the state machine covers closed → open → half-open → closed/open, isolated, and
manual reset.

## Milestone 3 — Rate limiting, hedging & chaos ⏳

- Rate and concurrency limiter over `System.Threading.RateLimiting`.
- Hedging with pooled execution contexts and linked CTS. Only the intrinsic allocations remain.

- Chaos strategies: fault, outcome, latency and behavior injection, with injection rate and an
  `Enabled` generator.
- Beyond Polly:
  - retry budget (percentage of recent traffic, shared);
  - adaptive concurrency limiter (AIMD / gradient);
  - slow-call ratio in the circuit breaker (this part lands with M2).

**Done when:** all three are usable from the runtime builder.

## Milestone 4 — Source generator: declarative pipelines ⏳

- `MintPlayer.Resilience.SourceGenerator` on `MintPlayer.SourceGenerators.Tools`:
  - `[GenerateEquality]` models;
  - a fixed hint-name set, e.g. `ResiliencePipelines.g.cs`;
  - packed under `analyzers/dotnet/roslyn5.9/cs` in the core package (the Assertions
    `TargetsForTfmSpecificContentInPackage` pattern).
- Emits the flat method from S1 per `[ResiliencePipeline]` class. Covers:
  - `[RetryWhen]` / `[FallbackWith]` hooks;
  - `ExecuteAsync` / `TryExecuteAsync` / sync `Execute` overloads;
  - the pooling builder per S2;
  - reloadable pipelines per S6.
- MPR0004 (invalid combinations) and MPR0005 (not awaited).

**Done when:** the sample `CatalogPipeline` from PRD §2.1 generates, compiles and passes the analyzers
clean.

## Milestone 5 — Interceptors ⏳

- Closure lowering at generated-pipeline call sites, within the boundary S3 defined.
- The package `.props` sets `InterceptorsNamespaces`.
- MPR0002 for capturing lambdas on runtime pipelines.

**Done when:** a capturing call site in the sample compiles to the intercepted path (verified in the
generated source).

## Milestone 6 — DI, registry, reload, telemetry ⏳

- `AddResiliencePipeline<TGenerated>()` and `AddResiliencePipeline(key, …)`.
- Keyed provider/registry, with `IOptionsMonitor` reload.
- Meter / ActivitySource / LoggerMessage telemetry per S8. Meter and instrument names are documented.

**Done when:** registry resolution and a reload are wired in the sample app.

## Milestone 7 — HttpClient integration ⏳

- `MintPlayer.Resilience.Http`:
  - `ResilienceHandler<TPipeline> : DelegatingHandler`, usable with or without DI;
  - `IHttpClientBuilder.AddResilienceHandler<T>()` / `AddResilienceHandler(key)`;
  - `AddStandardResilienceHandler()` and `AddStandardHedgingHandler()` with Microsoft's defaults (PRD
    §2.5).
- Request cloning and content buffering per attempt.
- Per-request override and opt-out via `HttpRequestMessage.Options`.
- `Retry-After` honoured via `MintPlayer.Http`.

**Done when:** a typed client (DI) and a hand-composed `HttpClient` (no DI) both use the handler, and a
parity test pins the preset values against Microsoft's.

## Milestone 8 — Testing package & migration analyzer ⏳

- `MintPlayer.Resilience.Testing`: pipeline descriptor (`GetPipelineDescriptor()`), fake-time helpers,
  and helpers for asserting on chaos injection. The chaos strategies themselves live in the core (M3).
- MPR0001 Polly-migration code fix and MPR0003, informed by S7.

**Done when:** the code fix converts the three S7 snippets.

## Milestone 9 — Benchmarks, CI, docs ⏳

- `MintPlayer.Resilience.Benchmarks` against `Polly.Core [8.8.0]`, with a `Verification.Run()` fairness
  gate: the same scripted faults, and the same attempt counts and state transitions.
- `resilience-benchmark.yml`, nightly plus manual dispatch. It asserts allocation ceilings only (not
  wall-clock), like `assertions-benchmark.yml`.
- README with measured numbers only.
- Solution folder "Resilience" added to `MintPlayer.Dotnet.Tools.sln`.
- Versions set, and `eng/Assert-PackageVersions.ps1` passes.

**Done when:** the benchmarks run and meet PRD §4 criteria 1–5.

## Final sweep (tests, once) ⏳

- Unit and behavioural-parity suite (fake `TimeProvider`).
- Generator tests: snapshot plus incrementality harness.
- Analyzer and code-fix tests.
- Native AOT publish of the sample (PRD §4.6).

Then open the single PR.

## Spike results

_(filled in as M0 runs)_

## Outcome

_(filled in at completion: defects found, benchmarks, target met / not met)_
