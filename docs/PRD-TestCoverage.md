# PRD: Raising Test Coverage Across MintPlayer.Dotnet.Tools

## Overview

Coverage reporting to [coverage.mintplayer.com](https://coverage.mintplayer.com) landed in commit
`f69b852`. This document is the plan for making the resulting number both *correct* and *good*.

**`C:\Repos\MintPlayer.Spark` is the reference implementation.** It is a .NET repo with a
working coverage setup *and* — critically — a working source-generator test project whose
coverage actually attributes (`tests/MintPlayer.Spark.SourceGenerators.Tests`, 18 generator test
files + 6 analyzer test files + snapshots). Where this document's spike findings and Spark's
proven pattern disagree, **Spark wins**; the disagreements are called out explicitly in R3 and
[Appendix D](#appendix-d-deviations-from-spark).

Those are two separate problems, and the first one dominates. A measured local run of the exact CI
sequence produces a merged repo figure of **46.9%** — but **48% of that denominator is Roslyn
source-generator code that no test touches and that is only in the report by accident**. Fixing the
plumbing moves the honest figure to **91.2% over 3,852 coverable lines**, while simultaneously
*shrinking* the measured surface to only what is actually tested. Growing that surface back out —
with real tests over the ~7,500 lines of generator code and the ~5,000 lines of untested shipped
libraries — is the second, larger problem.

## Problem Statement

Three distinct problems, in priority order.

### P1 — The reported number does not mean what it says

Measured, not inferred (full method in [Appendix A](#appendix-a-measurement-method)):

| | files | coverable | covered | rate |
|---|---|---|---|---|
| Today, as CI runs it | 179 | 8,834 | 4,144 | **46.9%** |
| After plumbing fixes only | 72 | 3,852 | 3,513 | **91.2%** |

The gap is almost entirely one root cause. `SlnLaunch/MintPlayer.SlnLaunch/MintPlayer.SlnLaunch.csproj`
lines 36 and 38 reference two source generators with `OutputItemType="Analyzer"` **and**
`ReferenceOutputAssembly="true"`. `MintPlayer.SlnLaunch.deps.json` confirms the consequence:

```
MintPlayer.SlnLaunch/10.0.1 -> [..., MintPlayer.CliGenerator, MintPlayer.SourceGenerators, ...]
MintPlayer.SourceGenerators/10.20.1 -> [MintPlayer.SourceGenerators.Attributes,
                                        MintPlayer.ValueComparerGenerator.Attributes,
                                        MintPlayer.ValueComparers.NewtonsoftJson]
```

So the shipped `slnlaunch` dotnet tool carries four Roslyn generator assemblies as **runtime
dependencies**, their PDBs land next to `MintPlayer.SlnLaunch.Tests.dll`, and coverlet dutifully
instruments ~4,236 lines of code that no test can reach. This is a packaging bug first and a
coverage bug second.

### P2 — Source generators are structurally invisible to coverage

The single most important mechanical fact about this repo, measured both directions:

| Reference style | Coverage attribution |
|---|---|
| `OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"` — how `MintPlayer.Assertions.Tests` references its generator today, with **553 passing tests** exercising it on every build | **Zero.** The Cobertura report contains exactly one package, `MintPlayer.Assertions`. `MintPlayer.Assertions.SourceGenerator` does not appear at all. |
| Generator DLL **copied into the test bin root**, `Assembly.Load`ed, and run via `CSharpGeneratorDriver` in-process — the Spark pattern | **Real.** A 4-test spike produced `MintPlayer.SourceGenerators` 15.7%, `MintPlayer.SourceGenerators.Tools` 28.8%, with per-class hits (`ServiceRegistrationsGenerator` 370/760 lines). Spark's production suite does the same at scale. |

Coverlet rewrites IL on disk in the test project's output directory. Roslyn loads analyzers through
`AnalyzerFileReference` from the *generator's own* `bin/`, which coverlet never touched. Therefore
**any** approach that runs a generator through the real compiler — the `TestProjects/*Debugging`
projects, `dotnet build` of a fixture, or `MSBuildWorkspace` — contributes nothing to coverage.
Coverage appears only when the generator assembly sits **in the test host's own output directory**,
where coverlet's collector scans, and is loaded from there.

This is why the ~7,500 lines under `SourceGenerators/` (plus 1,441 in
`Assertions/MintPlayer.Assertions.SourceGenerator`) sit at 0% despite being the most heavily
*exercised* code in the repository.

### P3 — ~5,000 lines of shipped library code has no test project at all

`ObservableCollection`, `Http`, `Pagination`, `StringExtensions`, `Math`, `EnumerableExtensions`,
`SeasonChecker`, `AsyncPipeline`, `EidReader.Core`, `StringBuilder.Extensions`, `Verz.Sdks.Dotnet`
and `Solve` ship to NuGet with zero automated tests. They contribute neither numerator nor
denominator today — they are simply absent from the report.

Reading them surfaced **seven live bugs before a single test was written**, and writing the
tests has since surfaced two more (§[Defects](#defects-found-during-investigation)).

## Goals

1. Make the reported coverage figure honest: the denominator contains shipped product code that
   tests could reach, and nothing else.
2. Stop shipping Roslyn generator assemblies as runtime dependencies of the `slnlaunch` tool.
3. Establish a source-generator test harness whose coverage actually attributes, and get real tests
   on all 11 generators, 8 analyzers and 5 code-fix providers.
4. Get a test project onto every shipped library that can be tested in CI on `ubuntu-latest`.
5. Fix every defect found along the way, and bump the affected packages so the fixes ship.
6. Leave the coverage number on an upward ratchet rather than a cliff.

## Non-Goals

1. **Any coverage target percentage.** The number is a signal, not a goal. A ratchet that only ever
   goes up is the mechanism; a threshold invites tests written to move a number.
2. **Testing the P/Invoke boundary.** `Beid/MintPlayer.EidReader.Native` is 15 `[DllImport("winscard.dll")]`
   declarations on a static class. The project *is* the unmockable boundary; ~40 of its 173 lines are
   reachable and testing them proves nothing.
3. **Testing `AdminHelper`.** `EnsureRunningAsAdmin()` on Linux calls `geteuid()`, then
   `Process.Start("sudo", ...)` re-spawning the test host, then `Environment.Exit(0)`. Its three
   `IsOSPlatform(Windows)` branches are structurally unreachable in CI.
4. **Seam-extraction refactors for `Verz`, `GraphQL.Tools`, `RemoveObjBin`, `EidReader`.** These need
   genuine dependency-inversion work on live-HTTP, `Assembly.Load`, `Console.ReadLine` and
   `Directory.Delete(recursive: true)` code paths before a test is even meaningful. Explicitly not
   in this unit of work — see [Out of Scope](#out-of-scope) for the full list and reasoning.
5. **Avalonia / desktop UI testing.** Would require `Avalonia.Headless` (not referenced) and a UI
   thread.
6. **`MSBuildWorkspace`.** Evaluated and rejected on evidence — see
   [Appendix B](#appendix-b-why-not-msbuildworkspace).

## Defects found during investigation

All of these are in shipped packages. Each is fixed in the milestone that adds its test, with a
patch version bump so the fix actually reaches NuGet.

| # | Location | Defect |
|---|---|---|
| D1 | `StringExtensions/.../Casing.cs` | `NthIndexOf` **infinite-loops** for `occurance > 1` — `IndexOf(c, index)` re-finds the same position without advancing — and throws on the initial `index = -1`. `Kebab2Camel` returns PascalCase, contradicting its name. |
| D2 | `Pagination/MintPlayer.Pagination` | Three separate crashes on a default-constructed request. `TotalPages` divides by zero when `PerPage == 0` — on a property getter, so it fires during serialization. An empty `GetEffectiveSortColumns()` was handed straight to `OrderBySortColumns`, which throws on an empty array, so `Paginate` on an unsorted request **always** threw. And `Page` is 1-based, so `Page = 0` produced `Skip(-PerPage)` and an `ArgumentOutOfRangeException` (found while writing the tests). |
| D3 | `Pagination/MintPlayer.Mapping` | `AddMapper<TMapper>()` calls `GetGenericTypeDefinition()` on *every* interface the mapper implements, so it throws if `TMapper` also implements e.g. `IDisposable`. |
| D4 | `Verz/MintPlayer.Verz.Targets` | `GeneratePublicApiHashTask` calls `Assembly.Load(AssemblyPath)` — `Load` takes an assembly *name*, not a path, so the happy path throws today. `InjectPublicApiHashTask` returns `true` from every failure path (`catch { LogWarning; return true; }`), so failures are invisible. |
| D5 | `EnumerableExtensions` | `Pairwise` uses `Count()` + `ElementAt`, enumerating the source multiple times — wrong for a one-shot sequence. |
| D6 | `Http/MintPlayer.Http` | `FromStreamAsync` sets `Position = 0`, throwing on a non-seekable stream. |
| D7 | `SourceGenerators/MintPlayer.SourceGenerators.Tools/Producer.cs:48-51` | `Producer.Produce` wraps `ProduceSource` in a bare `catch (System.Exception) { }`. **A generator crash produces no file and no diagnostic** — a silent miscompile of the consumer, and the reason a naively-written generator test can pass green while generating nothing. |
| D8 | `SourceGenerators/ValueComparers/.../JObjectValueComparer.cs` | Overrides `AreEqual` but not `AddHash`, so `GetHashCode` fell through to `JObject`'s own non-structural hash. Two objects the comparer called **equal returned different hashes**, breaking the `IEqualityComparer` contract and silently losing entries in any dictionary or set keyed on it — including the incremental-generator caches the comparer exists to serve. Found by a test, not by reading. |
| D9 | `AsyncPipeline/MintPlayer.AsyncPipeline/Pipeline.cs` | `GetAwaiter` used `upstream.ContinueWith(_ => output.Writer.Complete())`. `ContinueWith` yields a new task that succeeds regardless of whether the antecedent faulted, so **awaiting a pipeline silently swallowed every exception thrown by an action** — a failed pipeline was indistinguishable from a successful one. Separately, `Complete()` throws on an already-completed writer, so awaiting the same pipeline twice raised `ChannelClosedException` from inside the continuation. Both found by tests. |
| D10 | `Http/MintPlayer.Http/HttpResult.cs` | `public static implicit operator HttpResult<T?>(HttpResult<string?> result) => result;` — the body's conversion resolves to the operator itself, so for any `T` other than `string` it recursed until the process died of an **uncatchable `StackOverflowException`**. Reachable from `SendAsync<T>` whenever a server answered a typed request with `text/plain`. Removed; `SendAsync` now converts explicitly and throws a catchable `NotSupportedException`. |
| D11 | `Http/MintPlayer.Http/HttpResponseMessageExtensions.cs` | `ReadJsonAsync` assigned `PropertyNameCaseInsensitive` directly on the **caller's** `JsonSerializerOptions`. Those become read-only after first use, so the second call with the same instance threw `InvalidOperationException` — and the first silently mutated the caller's options. Now copies. |
| D12 | `ObservableCollection/…Extensions/ObservableCollectionExtensions.cs` | All eight `AddDistinctRange` overloads returned the filtered query **lazily**. `AddRange` enumerated it to do the inserting; the caller's enumeration then re-ran `!collection.Contains(item)`, by which point every item was in the collection — so the documented "items that were actually added" return value was **always empty**. Fixed by materializing before the add. |
| D13 | `ObservableCollection/…Extensions/ObservableCollectionExtensions.cs` | `RemoveRange(start, count)` resolves the slice **by index** and then hands it to the value-based `RemoveRange(IEnumerable<T>)`, which removes the FIRST match of each item. With duplicates it therefore deletes the wrong positions — and `RemoveExceedingAt` routes every `maxItemCount` trim through it. **Not fixed here**, see below. |
| D14 | `Beid/MintPlayer.EidReader.Core/Extensions/ByteArrayExtensions.cs` | The TLV length-continuation check read `(lenByte & 0x08) == 0x80`. `& 0x08` yields only 0 or 8, so it could never equal `0x80`: the do-while always ran exactly once and the multi-byte length form was **unreachable dead code**, despite the `len << 7` shift showing that is what was intended. A length byte of 0x80 or above was read as `lenByte & 0x7F` with the rest of the length treated as payload. Every documented BEID identity field is under 128 bytes, so real cards parsed identically — it was latent, not live. Fixed to `& 0x80`. |
| D15 | `SourceGenerators/…Tools/ValueComparers/ValueComparer.cs` | The `ImmutableArray<T>` branch of `IsEquals<TProp>` (and of the static `AddHash<TProp>`) passed **`TProp` — the array type — where the ELEMENT type was expected**. So `ImmutableArrayEquals` cast an `ImmutableArray<int>` to `ImmutableArray<ImmutableArray<int>>` and threw `InvalidCastException`: **every `ImmutableArray`-valued property comparison failed at runtime**, in the code path that exists to make incremental-generator caching work. Fixed by closing the generic over `GetGenericArguments()[0]`, with a cached `MethodInfo` per element type. |
| D16 | `StringBuilder/…/StringBuilderExtensions.cs` | `AppendIndented` advanced past the last line unconditionally: `valueSpan.Slice(index + nl.Length)` with `index == -1`. On **Windows** `Environment.NewLine` is two characters, so that is `Slice(1)` on an already-consumed span → `ArgumentOutOfRangeException` for an empty string, or for any input ending in a newline. On **Linux** NewLine is one character, so it was `Slice(0)` and the bug never showed. A platform-dependent crash on the simplest possible input. |
| D17 | `StringBuilder/…/StringExtensions.cs` | `Dedent` on text with no indentation threw `"Line … contains too few spaces at the start (should start with 0 spaces)"`. `DedentLine` only compares `trimmedSpaces >= spaces` **after** consuming a character, so with `spaces == 0` it consumed the first real character and fell into the throw. Guarded. |

## Requirements

### R1 — Coverage plumbing (P1)

**R1.1** Add a **minimal** `coverlet.runsettings` at the repo root, passed via `dotnet test --settings`.
Spark has no runsettings at all, so this is a deliberate deviation
([Appendix D](#appendix-d-deviations-from-spark)) justified by measurement, and it is kept as small as
the measurements support. Full rationale per setting in
[Appendix C](#appendix-c-coverlet-settings-rationale). The two load-bearing decisions:

- `ExcludeByFile` drops `**/obj/**/*.g.cs`. Measured: removes exactly the 6 files the coverage
  service was already discarding as unresolvable (738 coverable lines), so the raw report and the
  service finally agree.
- **No assembly `Exclude` list.** The spike proposed excluding `[MintPlayer.SourceGenerators]*`,
  `[MintPlayer.CliGenerator]*`, `[MintPlayer.Assertions.SourceGenerator]*` and friends as
  belt-and-braces for P1. **That must not be done**: it would suppress exactly the assemblies M10
  exists to start measuring, and the suppression would be invisible — the generator suite would pass
  while reporting 0%. R1.3 fixes P1 at the root; a filter that also blocks the fix is not a backstop.
- **`ExcludeByAttribute` is deliberately not set.** The usual internet snippet is actively harmful
  here. Measured on `MintPlayer.Assertions` (baseline 3202/3426): `Obsolete` deletes **125 lines** of
  real tested code, because Roslyn decorates `readonly ref struct` with a synthetic `[Obsolete]`;
  `CompilerGeneratedAttribute` deletes **194 lines** — every async state machine, i.e. the bodies of
  async methods; `GeneratedCodeAttribute` deletes nothing at all.

**R1.2** Change the Test step in both workflows to
`dotnet test --no-restore --no-build --configuration Release --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory coverage`.

Today `--no-restore` does *not* imply `--no-build`, so CI builds the whole solution twice — Release,
then Debug — measures coverage on the **Debug** build, and then packs the **Release** build. Verified:
69 `bin/Debug` directories appear alongside the 69 `bin/Release` ones, and the Test step's compiler
lines read `/define:TRACE;DEBUG;...`. Fixing it cuts the step from **88s to 32s** with all 679 tests
still passing.

This carries a **one-time ~2pp ratchet dip** (46.9% → 44.8% on the old denominator) because Release
strips debug-only sequence points. It must land in the same commit as R1.3 so there is one
re-baseline, not two.

**R1.3** In `MintPlayer.SlnLaunch.csproj` lines 36/38, set `ReferenceOutputAssembly="false"` on both
`OutputItemType="Analyzer"` references, and align `Verz/MintPlayer.Verz.csproj:28` the same way (it
already uses `false` on line 31 — it is internally inconsistent). Root-cause fix for P1, and the
**only** fix — deliberately not backed by a coverlet `Exclude` filter, for the reason in R1.1.

**R1.4** Add `Solve/Solve.csproj` to `MintPlayer.Dotnet.Tools.sln`. It is the only tracked project
outside the solution, which means CI never builds, tests **or packs** it — `MintPlayer.Solve` is a
`PackAsTool` package that has silently never shipped from this workflow.

**R1.5** Align both upload steps with Spark, which is the house pattern:

- **Drop `flags:` entirely.** Spark passes none. Two different flag names (`unit` on master, `pr` on
  PRs) fragment the per-flag trend line for no gain.
- **`fail-ci-if-error: false` on master too.** Spark uses `false` everywhere. `true` was over-strict:
  it makes a coverage-service outage — or a missing `COVERAGE_TOKEN` — fail the publish workflow
  *before* `dotnet pack`, blocking a NuGet release over a reporting problem.
- Add Spark's `if: always() && hashFiles('coverage/**/coverage.cobertura.xml') != ''` guard, so a run
  that produced no report is a no-op rather than an upload of nothing.
- Keep `base-sha` on the PR upload but **not** `partial:` — Spark sets `partial: true` because it runs
  `nx affected`; this repo runs the whole suite, so the totals really are whole-workspace.

**R1.6** Align test tooling with Spark's verified versions across all test projects:
`coverlet.collector` 8.0.1 → **10.0.1**, `Microsoft.NET.Test.Sdk` 18.3.0 → **18.9.0**,
`xunit.runner.visualstudio` 3.1.5 → **4.0.0** (Spark pairs 4.0.0 with `xunit` 2.9.3, so the combination
is proven). A two-major-version jump in the collector is itself capable of changing what gets
instrumented, so it belongs in the same re-baseline commit as R1.2 rather than landing later and
muddying the cause of a shift.

### R2 — Path-resolution robustness

The coverage service resolves report paths against `git ls-files` by longest-suffix match and
**silently drops** anything ambiguous or unresolvable. Measured today: 0 ambiguous drops. But that is
luck, not design.

Coverlet sets `<source>` to the *longest common directory prefix of all instrumented documents*. So
a report covering one product project gets **bare project-relative paths** (`AndConstraint.cs`),
while a report spanning two or more gets repo-relative paths. The repo has 21 duplicated basenames,
9 of them `.cs`: `Program.cs` (×22), `StringExtensions.cs` (×5), `ClassDeclaration.cs` (×3),
`Person.cs` (×3), `ConsoleService.cs`, `IConsoleService.cs`, and more.

Simulated casualties if a test project covered exactly one product project:
`MintPlayer.SlnLaunch` alone loses **3 of 23 files** (`Program.cs`, `Services/ConsoleService.cs`,
`Services/IConsoleService.cs` — the subdirectory does *not* disambiguate).

**R2.1** Every new test project must reference `MintPlayer.Assertions` (as 4 of the 5 existing ones
already do). This is not only the house assertion library — it forces ≥2 top-level folders into every
report, which pushes coverlet onto the repo-root-relative path shape where every file hits the
service's `exact` branch. It converts a latent silent-data-loss class into a non-issue.

**R2.2** After the first CI upload on this branch, confirm the service reports **0 unmatched paths**.

### R3 — Source-generator test harness (P2)

**R3.1** One new shared harness project, `SourceGenerators/MintPlayer.SourceGenerators.Tests`
(net10.0, xunit, coverlet), built on **Spark's proven pattern** — port
`tests/MintPlayer.Spark.SourceGenerators.Tests` rather than inventing one:

- Reference each generator project with `ReferenceOutputAssembly="false" SkipGetTargetFrameworkProperties="true"`.
  A **plain `ProjectReference` does not work**: the generators are netstandard2.0 and
  `MintPlayer.SourceGenerators.Tools` carries polyfilled BCL types, so letting their compile assets
  into a net10.0 project puts a second `ModuleInitializerAttribute` in scope and every use becomes
  ambiguous with `System.Runtime`'s (**CS0433**). This corrects the spike's advice, which had not hit
  the collision.
- Add a `CopyGeneratorRuntimeAssets` target (`AfterTargets="Build"`) copying each generator DLL **and
  its PDB**, plus `MintPlayer.SourceGenerators.Tools.dll`, into the **test bin root** — not a
  subfolder. Coverlet's collector scans the test host directory; a subfolder is not scanned.
- Load generators and analyzers by name at test time: `Assembly.Load(new AssemblyName(...))` into the
  **default** ALC, then find the type by name and `Activator.CreateInstance`. This is what makes the
  IL coverlet rewrote the code that actually runs.
- Reference attribute/abstraction projects (`*.Attributes`) **normally** — the test input sources need
  them to compile.
- Where a generator's runtime dependency comes from a NuGet package rather than a project, use
  `ExcludeAssets="all" GeneratePathProperty="true"` to get its path without its compile assets, and
  copy from `$(Pkg...)`. Spark's comment records the failure mode a hardcoded version caused: it
  worked on any machine with the old package cached and failed only on CI.

**R3.2** The harness must supply a stub `AnalyzerConfigOptionsProvider` carrying
`build_property.rootnamespace` (Spark's `StubAnalyzerConfigOptionsProvider`). Two verified traps,
both of which Spark's implementation already handles — copy it rather than re-deriving it:

- `AnalyzerConfigOptions` keys are **lowercase**, and Roslyn's real global-options dictionary is
  **case-insensitive**. A case-sensitive harness dictionary silently yields a null `RootNamespace`.
- `MapperGenerator.cs:240,245,249` and `ClassNamesSourceGenerator.cs:84` pass `RootNamespace!`. Under
  a driver with no options provider, `RegistrationsProducer` emits a bare `namespace ` → `CS1001`.

Combined with D7's swallowed exception, a naively-written generator test **generates nothing, reports
nothing, and passes green**. Therefore every generator test asserts `Assert.Empty(result.Errors)` and
a generated-file count `> 0`.

**R3.3** These generators need `Microsoft.Extensions.DependencyInjection.Abstractions` in the
reference set, or `ServiceRegistrationsGenerator` returns `default` and emits nothing.

**R3.4** Layer the strategy — each layer catches a class of bug the others cannot:

| Layer | Mechanism | Catches |
|---|---|---|
| 1 — bulk | `CSharpGeneratorDriver`, assert generated text + diagnostics + `Assert.Empty(result.Errors)` | wrong output, missing output, wrong/missing diagnostic, **uncompilable output** |
| 2 — thin, high value | `Compilation.Emit` → `Assembly.Load` → invoke generated code | semantics text assertions cannot reach: right-looking output with the wrong DI lifetime, a mapper that does not round-trip, an unresolvable service graph |
| 3 — analyzers | `compilation.WithAnalyzers(...)` + `GetAnalyzerDiagnosticsAsync`, filtered to the analyzer's own `SupportedDiagnostics` ids — Spark's `RunAnalyzerAsync`, same harness, same `Assembly.Load` | wrong/missing/misplaced diagnostics, with coverage attribution and no extra dependencies |
| 3b — code fixes | `AdhocWorkspace` + `CodeFixProvider.RegisterCodeFixesAsync`, apply the operations, compare resulting document text. Needs only `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.3.0 | that a fix produces the intended code and still compiles |
| 4 — snapshots | `Verify.Xunit` over the same harness, with Spark's `VerifyDefaults` pinning snapshots to `VerifyResults/{Class}/{Method}.verified.txt` | formatting/shape regressions in the four large producers (Mapper 25KB, Inject 20KB, Registrations 14KB, Cli 17KB) |
| 5 — packaging smoke | `dotnet pack` → local feed → `dotnet build` a package-consuming fixture | the **only** layer covering `build/*.props`/`.targets`, the `analyzers/dotnet/roslyn4.x/cs` pack layout, and `GetDependencyTargetPaths*` |

**R3.5** Do **not** use `Microsoft.CodeAnalysis.*.Testing`. The spike verified it works (1.1.4 against
Roslyn 5.3.0, with `DefaultVerifier` — the `*.Testing.XUnit` variants are dead at 1.1.2) and its
`{|MP001:...|}` markup is genuinely nicer. It is rejected because Spark does not use it, it pulls in
`NuGet.Common/Packaging/Protocol/Resolver`, `Microsoft.VisualStudio.Composition` and `DiffPlex`,
it resolves targeting packs over the network at *test* time, and it runs ~3–5× slower per test. One
harness for generators, analyzers and fixes beats two.

**R3.6** For Layer 2, load emitted fixture assemblies with plain `Assembly.Load(byte[])` into the
default context and never unload. A collectible `AssemblyLoadContext` drags in the `NoInlining` + explicit-GC dance
recorded in the project memory and turns flaky; the leak across a few hundred small fixture
assemblies in a short-lived test process is bounded and irrelevant.

**R3.7** Fix D7 (remove the blanket `catch`, or convert it to a reported diagnostic) as a
prerequisite — Layer 1 cannot fail loudly while exceptions are swallowed.

**R3.8** **The generator PDBs must be copied in every configuration, not just Debug.** Spark's target
guards the PDB copy with `Condition="'$(Configuration)' == 'Debug'"` and its Nx test target runs
Debug, so the two agree. R1.2 switches this repo's test run to **Release** — with Spark's condition
copied verbatim, no PDBs would land in the test output and generator coverage would silently be zero
again. Copy them unconditionally; the test project is `IsPackable=false` and never published, so the
concern the condition guards against does not apply.

### R4 — Library test projects (P3)

New xUnit test projects, ranked by product lines currently at 0% divided by friction. Each fixes any
defect it surfaces and bumps that package's patch version.

| Rank | Target | Lines | Notes |
|---|---|---|---|
| 1 | `Solve` | 2,520 | Needs R1.4 first. All 7 commands already take only interfaces — write 5 fakes. `IssueReference.Parse`/`ExtractFromBranchName`, `GitHubIssue.GetIssueType/GetPriority/GetSlug`, `WorkStatus.CompletionPercentage` and `PrdGenerator`'s 5-regex parsing are pure and public *today*. Leave the 4 `Process`-spawning adapters uncovered. |
| 2 | `ObservableCollection.Extensions` | 370 | Pure in-memory. `maxItemCount` trimming × `ECollectionSide` × comparer overloads is a dense off-by-one surface. |
| 3 | `ObservableCollection` | 448 | Range ops / `Enabled` / `ItemPropertyChanged` / dispose need nothing. `IsCollectionView` and `RunOnMainThread` are `protected virtual` — real seams. `SynchronizationContext.Current` is captured in a field initializer and is null under xUnit; `Assertions`' `AsyncDeadlockTests` shows how to install one. |
| 4 | `Http` | 294 | 18 pure builders, an RFC5988 `Link` parser, and `SendAsync<T>` behind a `DelegatingHandler` stub. Never touches a real URL. Fixes D6. |
| 5 | `EidReader.Core` | 303 | Pure TLV parsing, zero P/Invoke — but `internal static`. One `InternalsVisibleTo` line unlocks it. |
| 6 | `Pagination` | 213 | `List<T>.AsQueryable()` is the entire harness. Fixes D2. |
| 7 | `StringExtensions` | 215 | `[Theory]` fodder. Fixes D1. |
| 8 | `SourceGenerators.Tools` (pure half) | ~600 of 1,621 | String/enumerable/type/writer extensions + ~10 value comparers, all `public static`, no Roslyn. `ValueComparer.Registry.cs` has a process-wide static `ComparerRegistry` seeded by `[ModuleInitializer]` with no reset API — serialize those tests into one xUnit `[Collection]`. |
| 9 | `SeasonChecker` | 143 | One DI line. No `DateTime.Now` anywhere — fully deterministic. |
| 10 | `AsyncPipeline` | 128 | Deterministic at `consumerCount=1`. |
| 11 | `Math` | 100 | 15 pure `static double f(double)`. Best coverage-per-minute in the repo; none guard 0/±1/NaN/∞. |
| 12 | `EnumerableExtensions` | 60 | Fixes D5. `RandomElement` uses static `Random.Shared` — assert membership, not value. |
| 13 | `StringBuilder.Extensions` | 190 | Process-wide non-thread-safe `Dictionary<StringBuilder, State> states` — serialize into one `[Collection]`. `AppendIndented` uses `Environment.NewLine`, so compose expectations, never literal. |
| 14 | `Verz.Sdks.Dotnet` | ~50 of 138 | Temp-csproj fixtures for the 3 `XDocument` methods; skip the 2 `Assembly.LoadFrom` ones. |
| 15 | MSBuild tasks | 300 | One shared ~30-line fake `IBuildEngine` unlocks `FolderHasher.Targets`, `Verz.Targets` and `MSBuild.Tasks`. `Microsoft.Build.Utilities.Core` is just a NuGet package — no MSBuild install needed. Fixes D4. |
| 16 | `ValueComparers.NewtonsoftJson` | 18 | 18 lines to 100% in ~10 assertions. |

**R4.1** `MintPlayer.Assertions.Tests` targets `net10.0` only while `MintPlayer.Assertions` ships
`net8.0;net9.0;net10.0`. Adding `net8.0;net9.0` to the test project's `TargetFrameworks` runs all 553
tests three times. **Gate this on R2.2** — it makes the test project multi-TFM, and coverlet then
emits one report per TFM carrying the same `filename` with different line sets. Max-merge takes the
union, which is defensible but grows the denominator silently.

**R4.2** Close the named gaps in `MintPlayer.Assertions`: `ReferenceTypeAssertions` (13 members),
`ActionAssertions` (9), `FuncAssertions` (8), `WildcardPattern` (3), `Formatter` (2),
`StringDifference` (2) have **zero mentions in any test file**. Also lift the three invariant checks
from `MintPlayer.Assertions.Benchmarks`' existing `Verification.Run()` into the test project — they
already assert the generated-accessor invariant and run in seconds.

**R4.3** Convert `ObservableCollection/MintPlayer.ObservableCollection.Test` from a console `Exe` to
xUnit. Its 4 demo methods are pre-written test scenarios. Add a known-answer golden hash to
`FolderHasher.Tests` — every assertion there is currently *relative*, so a change of hashing scheme
passes silently.

### R5 — Repo hygiene

**R5.1** Delete `SourceGenerators/TestProjects/MapAsDictionaryDebugging/` (completely empty) and
`CodeMigrator/` (no `.cs`, no `.csproj`, not in the sln, `git ls-files` returns nothing).

**R5.2** Rename `AdminHelper/AdminTest` — it is a console `Exe` with a plain project GUID, not a test
project, and its name implies otherwise. It calls `EnsureRunningAsAdmin()`, so if it were ever made
discoverable it would `sudo`-respawn and then `Environment.Exit(0)` the test host.

**R5.3** `Verz/MintPlayer.Verz/sandbox/` is untracked and contains a **real nested git repository**.
Add it to `.gitignore` so no recursive glob or `git add` ever walks into it.

## Implementation Plan

Milestones are ordered so that measurement is trustworthy before any test is written to move it. One
commit per milestone.

| M | Scope | Requirements |
|---|---|---|
| **M0** | Branch, coverage badge, this PRD | — |
| **M1** | **Plumbing.** `coverlet.runsettings`; both workflow Test steps; `ReferenceOutputAssembly="false"`; `Solve` into the sln; align uploads with Spark; align test-tooling package versions with Spark. Land together — one re-baseline. | R1.1–R1.6 |
| **M2** | Hygiene. Delete dead dirs, rename `AdminTest`, gitignore the sandbox. | R5 |
| **M3** | Pure easy wins: `Math`, `EnumerableExtensions` (D5), `StringExtensions` (D1), `SeasonChecker`, `ValueComparers.NewtonsoftJson`, `AsyncPipeline`. | R4 §11,12,7,9,16,10 |
| **M4** | `Pagination` (D2) + `Mapping` gaps (D3). | R4 §6 |
| **M5** | `Http` (D6), incl. the `DelegatingHandler` stub and RFC5988 `Link` parser. | R4 §4 |
| **M6** | `ObservableCollection` + `.Extensions`; convert the console harness to xUnit. | R4 §2,3, R4.3 |
| **M7** | `EidReader.Core` via `InternalsVisibleTo` + TLV fixtures. | R4 §5 |
| **M8** | `SourceGenerators.Tools` pure half; `StringBuilder.Extensions`; `Verz.Sdks.Dotnet`. | R4 §8,13,14 |
| **M9** | MSBuild tasks: shared fake `IBuildEngine`, all three task projects (D4). | R4 §15 |
| **M10** | **Generator harness** — R3.1–R3.3, D7 fix, Layer 1 + Layer 2 across all 11 generators. The single biggest lever on the headline number. | R3 |
| **M11** | Analyzers (Layer 3, `WithAnalyzers`) + code fixes (Layer 3b, `AdhocWorkspace`) + snapshots (Layer 4, `Verify.Xunit`). 8 analyzers, 5 fixes, 4 large producers. | R3.4 L3/3b/4 |
| **M12** | `Solve` tests. | R4 §1 |
| **M13** | `Assertions` gap-closing (R4.2); `net8.0;net9.0` test legs **if** R2.2 confirmed clean. | R4.1, R4.2 |

Version bumps ride along in the milestone that changes each package's code.

## Out of Scope

Genuinely not being done in this unit of work — not deferred to avoid a large diff.

- **Seam extraction for `Verz` (290 lines), `GraphQL.Tools` (64), `RemoveObjBin` (20), `EidReader`
  (368).** Each needs real dependency inversion before a test means anything: live
  `api.nuget.org` + `Assembly.LoadFrom` + `Activator.CreateInstance`; a concrete non-mockable
  `Octokit.GraphQL.Connection` requiring a token; `Directory.Delete(root, recursive: true)` against
  `GetCurrentDirectory()` with no injectable root; and `SCardConnect`/`SCardTransmit` needing an
  `IWinSCard` interface. Note `Verz`'s `InitDotnetCommand.Execute` does
  `Directory.GetFiles(root, "*.csproj", AllDirectories)` + `File.WriteAllText` inside a bare
  `catch { }` — it would **rewrite `<Version>` across the whole repo** if run with the default root.
  That is a defect worth its own issue, but fixing it is a behavioural change to a tool, not a
  coverage task.
- **`EidReader.Native`, `AdminHelper`, Avalonia projects, `Beid/DemoWebApp`** — see Non-Goals 2–5.
  `DemoWebApp` additionally hardcodes Belgian root-CA thumbprints absent from the Linux trust store,
  uses `DateTime.Now`, and indexes `chain.ChainElements[1]/[2]` unguarded.
- **`CodeMigrations.Runner`** — a publishable global tool whose `Program.cs` is 3 lines and two
  `Console.WriteLine`s. Whether it should exist at all is a separate question.
- **D13 (`RemoveRange(start, count)` removing by value).** Pinned by a characterization
  test rather than fixed. The correct fix is index-based removal, which the base
  `System.Collections.ObjectModel.ObservableCollection<T>` does not offer for a range —
  so it means either N single `RemoveAt` calls, turning one batched notification into N,
  or new index-range support on `MintPlayer.ObservableCollection` itself. Both change
  observable notification behaviour for every UI consumer, which is a design decision
  for the owner rather than something a coverage pass should settle.
- **`Verz.Sdks.Dotnet` misreading .NET Framework monikers.** `IsNetTfm` accepts anything
  matching `net<digit>` and `ParseNetMajor` reads every leading digit, so `net472` parses
  as major version **472** and outranks `net10.0`. Pinned by a characterization test.
  Whether .NET Framework targets should be supported at all is a product decision.
- **A coverage threshold or merge gate.** Deliberately: get the number honest and rising first. A
  `coverage.yml` with `blocking: false` may be added once the figures settle.
- **Multi-TFM test projects beyond R4.1**, and `MSBuildWorkspace` (Appendix B).

## Acceptance Criteria

1. The coverage service reports **0 unmatched paths** for the upload from this branch.
2. No Roslyn generator assembly appears in the report except through
   `MintPlayer.SourceGenerators.Tests`, where it appears with a **non-zero** rate.
3. `MintPlayer.SlnLaunch.deps.json` no longer lists `MintPlayer.SourceGenerators` or
   `MintPlayer.CliGenerator` as runtime dependencies.
4. The Test step runs `--no-build --configuration Release`; no `bin/Debug` directory is produced in CI.
5. `Solve` is built, tested and packed by CI.
6. Every defect D1–D7 has a regression test that fails without the fix.
7. Every shipped library named in R4 has a test project, and every package whose code changed has a
   bumped `<Version>`.
8. The full suite passes on `ubuntu-latest`.

## Risks

| Risk | Mitigation |
|---|---|
| The Debug→Release switch drops the rate ~2pp and reads as a regression. | Land M1 as one commit with a clear message; it is a re-baseline, not a regression. |
| `ReferenceOutputAssembly="false"` breaks the `slnlaunch` build. | Unverified by the spike — the runtime types live in the separate `*.Attributes` projects, which are referenced normally, so it should be fine, and Spark uses exactly this style. **Build it in M1 before moving on.** If it genuinely cannot be false, the fallback is `IsPackable`-scoped rather than a coverage filter — a filter would also block M10. |
| `TokenReplacer.Tests` shells out to nested `dotnet build`/`pack` (5-min timeout) and hits `api.nuget.org` inside the coverage-collecting host. Most likely source of CI flake. | Watch the first runs. Consider marking the pack-and-consume E2E as a separate traited suite. |
| `FolderHasher.Tests`' `IsIgnored_CaseInsensitive_OnWindows` has **no OS guard** and has only ever run on Windows. | May fail on ext4. Verify on the first CI run of this branch. |
| `SlnLaunch`'s 74.3% is the *Linux* path — `ProcessOrchestratorTests` branches on `OperatingSystem.IsWindows()`, so the Windows tree-kill path is permanently uncovered in CI. | Understood and accepted. Do not chase it. |
| Snapshot tests (Layer 4) lock in a wrong-but-accepted output. | Always pair with `Assert.Empty(result.Errors)`; a snapshot proves the output did not change, never that it is correct. |

## Appendices

### Appendix A: measurement method

Every figure in this document came from running the real CI sequence locally
(`dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test ... --collect:"XPlat Code Coverage"`),
inspecting all 5 Cobertura reports, and re-implementing the coverage service's `PathNormalizer` plus
its max-merge and `Summarize(files.Where(f => f.Matched))` in Python to simulate exactly what the
server keeps and drops. 679 tests, 0 failures.

Verified package versions for the generator work: SDK 10.0.400; `Microsoft.CodeAnalysis.CSharp` 5.3.0
(the generator csprojs `Update` this over the 4.14.0 pinned in `SourceGenerators/eng/sourcegenerator.targets`
— 5.3.0 is the real target); `Basic.Reference.Assemblies.Net100` 1.8.11; `Microsoft.NET.Test.Sdk`
18.3.0; `xunit` 2.9.3; `xunit.runner.visualstudio` 3.1.5; `coverlet.collector` 8.0.1;
`Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` / `.CodeFix.Testing` **1.1.4**.

Note on `Microsoft.CodeAnalysis.*.Testing`, recorded because it was verified and then **rejected**
(R3.5): the framework-specific variants (`*.Testing.XUnit`, `.MSTest`, `.NUnit`) are **dead at 1.1.2**;
on 1.1.4 you use the framework-agnostic `CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>`
directly, and `ReferenceAssemblies.Net.Net100` does exist. It resolves targeting packs through NuGet
at *test* time, so a cold runner may hit the network on first run. Not used here.

Spark's verified versions, adopted by R1.6: `coverlet.collector` 10.0.1, `Microsoft.NET.Test.Sdk`
18.9.0, `xunit.runner.visualstudio` 4.0.0, `xunit` 2.9.3, `Microsoft.CodeAnalysis.CSharp` 5.3.0,
`Verify.Xunit` 31.12.5.

### Appendix B: why not `MSBuildWorkspace`

Raised as a candidate for generator e2e testing. A probe was built and run against
`SourceGenerators/TestProjects/MintPlayer.SourceGenerators.Debug`. **It does work:**

```
opened in 4211 ms; analyzerRefs=14
source-generated docs: 5
  SymbolDescriptions.g.cs  ClassNames.g.cs  ServiceMethods.g.cs  Inject.g.cs  ClassNameList.g.cs
compilation trees=18, elapsed=7693 ms, errors=0
```

Rejected anyway:

- **Zero coverage attribution.** The generator loads via `AnalyzerFileReference` from its own `bin/`
  — the exact case measured in P2. On a repo where coverage is a gating signal, this layer is
  invisible.
- **4.2s to open one project, 7.7s total** — 4–8× the entire raw-driver suite, per project.
- Requires `Microsoft.Build.Locator` 1.9.1 with a hard rule that `RegisterDefaults()` must run before
  the JIT resolves any MSBuild type in the calling method — trivially violated by xunit fixture
  ordering. One plausible csproj mistake (`ExcludeAssets="runtime"`) produced
  `FileNotFoundException: Microsoft.CodeAnalysis.Workspaces, Version=5.3.0.0`.
- Emits 5 `Found project reference without a matching metadata reference` warnings precisely because
  the generator projects set `IncludeBuildOutput=false`, so the `WorkspaceFailed` handler cannot be a
  blanket assert — losing the diagnostic value that motivated it.
- Its one unique capability — "does the real MSBuild wiring work" — is covered better and more
  cheaply by Layer 5 (`dotnet pack` → `dotnet build` a fixture). The `*Debugging` projects already do
  80% of it for free on every CI build.

### Appendix C: coverlet settings rationale

Each measured on `MintPlayer.Assertions.Tests`, baseline **3202/3426 covered**.

| Setting | Decision | Measurement |
|---|---|---|
| `ExcludeByFile` = `**/obj/**/*.g.cs,**/*.Designer.cs` | **set** | Removes exactly the 6 files the service already dropped (738 coverable lines). Tuned run: 0 unmatched paths across all 5 reports. |
| `ExcludeByAttribute` | **not set** | `GeneratedCodeAttribute`: 0 effect. `Obsolete`: −125 lines (synthetic `[Obsolete]` on `readonly ref struct` — `SpanAssertions<T>`, `ReadOnlySpanAssertions<T>`). `CompilerGeneratedAttribute`: −194 lines (every async state machine). |
| `SkipAutoProps` | **false** | 3202/3426 → 3109/3331; rate 93.46% → 93.33%. Near-zero gain, and it removes 3 files from the service's file tree entirely. |
| `UseSourceLink` | **false**, explicitly | SourceLink is live (`obj/*.sourcelink.json` exists). `true` rewrites `filename` to `raw.githubusercontent.com` URLs; the tail would still suffix-match, but any duplicated basename goes ambiguous. |
| `DeterministicReport` | **false**, pinned with a comment | Not needed today — but the very next workflow step sets `ContinuousIntegrationBuild=true`. If that ever migrates to a `Directory.Build.props`, paths become `/_/Assertions/...` and this becomes mandatory. Pinned so the coupling is visible. |
| `Include` | **not set** | Instrumentation is already scoped by "has a PDB in the test output". An allowlist is just a second place to forget. |
| Demo/sample/benchmark exclusions | **not needed** | `*.Demo`, `*.Debugging`, `AvaloniaTest`, `DemoWebApp`, `HttpDemo`, `Benchmarks` never appear in any report — nothing references them from a test project. The only accidental inclusions were the generators (P1). |

### Appendix D: deviations from Spark

`C:\Repos\MintPlayer.Spark` is the reference. Every difference is deliberate and listed here.

| Area | Spark | Here | Why |
|---|---|---|---|
| Test invocation | `dotnet test --no-build --no-restore` per project via Nx (→ **Debug**) | one solution-wide `dotnet test --no-build --configuration Release` | This repo has no Nx and builds Release for `dotnet pack` in the same job. Testing Debug would mean building the solution twice, which is the bug R1.2 fixes. |
| Generator PDB copy | `Condition="'$(Configuration)' == 'Debug'"` | **unconditional** | Direct consequence of the row above. Spark's condition is correct *for Spark*; copied verbatim here it would silently zero out generator coverage. See R3.8. |
| Report location | `tests/*/coverage/**` (per project, because Nx runs each separately) | `coverage/**` (one `--results-directory` for the whole run) | One `dotnet test` invocation, so one results directory. |
| `.runsettings` | none | minimal (`ExcludeByFile`, `UseSourceLink=false`, `DeterministicReport=false`) | Measured benefit; see Appendix C. Kept as small as the measurements support. |
| `partial:` on PR upload | `true` (+ `base-sha: $NX_BASE`) | omitted (`base-sha` kept) | Spark uploads an `nx affected` subset; this repo runs the whole suite, so declaring it partial would misreport a genuine whole-workspace total. |
| Assertion library | `FluentAssertions` 7.2.2 | `MintPlayer.Assertions` | This repo *is* the FluentAssertions replacement — it should use its own library. Also satisfies R2.1 (forces the repo-root-relative path shape). |
| Analyzer/code-fix testing | `WithAnalyzers` in the shared harness; no code-fix tests | same harness for analyzers, plus `AdhocWorkspace` for the 5 code-fix providers Spark doesn't have | Same pattern extended, rather than adding the `Microsoft.CodeAnalysis.*.Testing` stack. |
| Snapshots | `Verify.Xunit` 31.12.5 + `VerifyDefaults` | same | Adopted as-is, including the `VerifyResults/{Class}/{Method}` path convention. |

## Version

Created 2026-08-27. Branch `feature/improve-test-coverage`.
