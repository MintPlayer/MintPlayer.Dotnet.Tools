# PRD: Test Coverage, Phase 2 — Honest Denominator, Real Verification

## Overview

[`docs/PRD-TestCoverage.md`](PRD-TestCoverage.md) (PR #170, merged as `827a945`) fixed the coverage
plumbing and took the repo from a meaningless 46.9% to a measured, reproducible **64.0%**. That work
is essentially complete: 19 of its 21 defects are fixed, all 22 test projects exist, and the Roslyn
generator harness it specified is built and working.

This document is Phase 2. It is a smaller, sharper problem than Phase 1, because the plumbing is no
longer in question — a local run of the exact CI sequence reproduces the published figure to within
one line (see [Appendix A](#appendix-a-measurement-method)).

**Baseline, verified 2026-09-03 against master `c7b13b9`:**

| | value |
|---|---|
| Lines | **6,747 / 10,544 = 64.0%** |
| Branches | 2,847 / 6,001 = 47.4% |
| Files | 261 |
| Test projects | 22, all green |

Phase 2 has three goals, in priority order:

1. **Make the denominator honest.** ~2,360 LOC of shipped code is invisible to the metric — not
   counted as uncovered, simply absent. The 64.0% is flattering for exactly that reason.
2. **Close the two blocks that hold 89% of the uncovered lines** — `SourceGenerators` (2,282) and
   `Solve` (1,108).
3. **Fix what the suite fails to *verify*, not just what it fails to cover.** Three specific gaps
   are documented in [P3](#p3--the-suite-verifies-less-than-its-number-suggests); they are the
   reason this PRD is not purely a numbers exercise.

## Decisions taken

These were put to the repo owner before this document was written, and are settled:

| Decision | Choice | Consequence |
|---|---|---|
| **`Solve` scope** | **Test it.** Not excluded. | `Solve` stays in the denominator. Its 1,108 uncovered lines are work to be done, not lines to be hidden. Rejected: the one-line `Exclude` that would have moved 64.0% → 70.6% with no tests written. |
| **Denominator** | **Correctness first — accept the dip.** | Currently-invisible shipped code gets wired in even though the headline drops before it rises. Same reasoning Phase 1 applied to the old 46.9%. |
| **Phase 1 leftovers** | **All four in scope.** | `InjectPublicApiHashTask`, Layer 5 packaging smoke, diagnostic-path tests, FolderHasher golden hash. See [R5](#r5--phase-1-leftovers). |

One further question was raised — end-to-end tests via `MSBuildWorkspace`, with a generic base class
to reduce per-generator boilerplate. It is answered in [R4](#r4--generator-test-ergonomics-and-the-msbuildworkspace-question),
where the boilerplate half is adopted and the `MSBuildWorkspace` half is recommended against, with
reasons.

## Problem statement

### P1 — ~2,360 LOC of shipped code is not in the denominator at all

The metric describes a smaller repository than the one that ships. These projects have C# on disk and
produce **no coverage rows in any report**, because no test host loads them:

| Project | LOC | Ships? | Note |
|---|---:|---|---|
| `Assertions/MintPlayer.Assertions.SourceGenerator` | **1,256** | **yes — inside `MintPlayer.Assertions`** | see below |
| `Beid/MintPlayer.EidReader` | 358 | yes | live PC/SC card session |
| `Verz/MintPlayer.Verz` | 290 | no (`IsPackable=false`) | the `verz` CLI |
| `Beid/MintPlayer.EidReader.Native` | 174 | yes | raw `DllImport` to `winscard.dll` |
| `AdminHelper/MintPlayer.AdminHelper` | 64 | yes | UAC elevation relaunch |
| `GraphQL/MintPlayer.GraphQL.Tools` | 48 | yes (`MintPlayer.GraphQL`) | **pure string cleaning, no deps** |
| `Verz/Registries/...NugetOrg` | 39 | yes | live network |
| others (interfaces, markers, stubs) | ~130 | mixed | mostly not worth testing |

**`MintPlayer.Assertions.SourceGenerator` is the headline.** It is `IsPackable=false`, which reads as
"internal" — but `MintPlayer.Assertions.csproj:39-42` packs its DLL into `analyzers/dotnet/cs`, so it
ships to **every consumer of `MintPlayer.Assertions`**:

```xml
<None Include="..\MintPlayer.Assertions.SourceGenerator\bin\$(Configuration)\netstandard2.0\MintPlayer.Assertions.SourceGenerator.dll"
      Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
```

That payload is 4 analyzers and 4 code-fix providers — `AssertionScopeNotDisposedAnalyzer`,
`UnawaitedAssertionAnalyzer`, `VacuousShouldAnalyzer`, `FluentAssertionsMigrationAnalyzer` and their
fixes — with **zero tests**. A code-fix provider that corrupts a consumer's source has no test
standing between it and a release. `Assertions` reads 95.7% precisely because the generator producing
those assertions is not in the denominator: the generated *output* is exercised by 553 tests, the
generator itself not at all.

This is the largest untested shipped asset in the repository, and unlike the rest of P1 it is a
correctness risk rather than a metrics artifact.

### P2 — Two folders hold 89% of the uncovered lines

| Folder | Coverable | Covered | **Uncovered** | % |
|---|---:|---:|---:|---:|
| SourceGenerators | 4,537 | 2,255 | **2,282** | 49.7% |
| Solve | 1,410 | 302 | **1,108** | 21.4% |
| SlnLaunch | 498 | 370 | 128 | 74.3% |
| Assertions | 2,437 | 2,332 | 105 | 95.7% |
| Verz | 126 | 78 | 48 | 61.9% |
| FolderHasher | 214 | 170 | 44 | 79.4% |
| TokenReplacer | 216 | 178 | 38 | 82.4% |
| ObservableCollection | 297 | 263 | 34 | 88.6% |
| AsyncPipeline | 48 | 40 | 8 | 83.3% |
| Http / StringExtensions | 252 | 250 | 2 | ~99% |
| Beid, EnumerableExtensions, Math, MSBuildTasks, Pagination, SeasonChecker, StringBuilder | 509 | 509 | 0 | 100% |

Everything below `SlnLaunch` is rounding error. Chasing it cannot move the headline.

**Inside `SourceGenerators`**, the mass is concentrated far more tightly than the folder total
suggests — the Inject generator alone is 49% of the problem:

| Artifact | Coverable | Uncovered | % |
|---|---:|---:|---:|
| `InjectSourceGenerator.cs` + `.Producer.cs` + `.Rules.cs` | 833 | **602** | 28% |
| `ServiceRegistrationsGenerator.cs` + `.Producer.cs` | 501 | 263 | 47% |
| `MapperGenerator.cs` + `.Producer.cs` | 490 | 200 | 59% |
| `CliCommandSourceGenerator.cs` + `.Producer.cs` | 484 | 204 | 58% |
| `WithComparerRoslynTypeAnalyzer.cs` | 108 | 103 | **4.6%** |
| `ConfigSourceGenerator.Rules.cs` | 98 | 98 | **0% — misnamed, not dead. See note.** |
| `Tools/ValueComparers/*` (tuple comparers, cache) | ~110 | ~110 | **~0%** |

> **Note on `ConfigSourceGenerator.Rules.cs`.** There is no `ConfigSourceGenerator` class, which
> makes the file look like dead code. It is not. It declares `public static partial class
> DiagnosticRules` and its 14 descriptors are live — `InjectSourceGenerator.cs:455, 494, 522, 572,
> 611, 639, 723, 803` emit them. The `[Config]`/`[ConnectionString]`/`[Options]` feature was folded
> into the Inject generator and only the filename still refers to the old split. **Do not delete
> it**; rename it to `InjectSourceGenerator.Config.Rules.cs` so the next reader is not misled. It
> sits at 0% for the same reason `ExtractConfigField` and `ClassifyType` do — see
> [P4](#p4--the-uncovered-generator-mass-is-fixtures-not-harness).

**Inside `Solve`**, every command class is at literally zero:

| Group | Coverable | Covered | % |
|---|---:|---:|---:|
| `Solve/Commands` (all 7 classes) | 717 | **0** | **0.0%** |
| `Solve/Services` | 586 | 213 | 36.3% |
| `Solve/Program.cs` | 18 | 0 | 0.0% |
| `Solve/Models` | 89 | 89 | 100% |

`Solve.Tests` today exercises DTOs and `PrdGenerator`, nothing else. The four services the commands
depend on — `GitService` (94), `GitHubService` (154), `ClaudeService` (60), `ConsoleService` (59) —
are all at 0%, but **they are already interface-backed** (`IGitService`, `IGitHubService`,
`IClaudeService`, `IConsoleService`). The seams exist; nothing is using them.

### P3 — The suite verifies less than its number suggests

Three gaps that no percentage would reveal:

1. **Every `*.Rules.cs` file sits at exactly 0% while its generator is 30–60% covered.**
   `ConfigSourceGenerator.Rules` (98), `InjectSourceGenerator.Rules` (28), `MapperGenerator.Rules`
   (14), `ServiceRegistrationsGenerator.Rules` (14). Those files are the diagnostic descriptors and
   the code that emits them. All four at zero means **no test in the repository drives any generator
   down a diagnostic-reporting path.** The generator suite is happy-path only. A generator that
   reports the wrong diagnostic, or crashes instead of reporting one, would pass CI today.

2. **No packaging smoke test for the generators.** Phase 1's R3.4 Layer 5 was specified and not
   built. The generators' `build/*.props`/`.targets`, the `analyzers/dotnet/roslyn4.x/cs` pack
   layout and `GetDependencyTargetPaths*` are untested. This is the failure mode that ships a
   broken NuGet package with every unit test green — and it is not hypothetical, since
   `MintPlayer.Assertions` hand-rolls its analyzer packaging in `None Include` items (P1 above)
   with a `Configuration`-conditional second entry.

3. **`FolderHasher` has no known-answer test.** All 13 tests in
   `FolderHasher/MintPlayer.FolderHasher.Tests/FolderHasherTests.cs` are relative
   (`SameContent_ReturnsSameHash`, `DeterministicAcrossRuns`, `HashIsLowercaseHex`). No hex literal
   ≥20 chars exists anywhere under `FolderHasher/`. A silent change of hashing scheme passes every
   test — while every downstream cache keyed on that hash quietly invalidates.

### P4 — The uncovered generator mass is fixtures, not harness

A question worth settling before any work starts: *can lines inside a generator's lambdas ever be
covered?* **Yes.** Measured against the current report:

```
[REGULAR] COVERED 129/143  InjectSourceGenerator.Initialize   ← almost entirely lambda bodies
[REGULAR] zero      0/ 57  ExtractConfigField
[REGULAR] zero      0/ 58  ClassifyType
[REGULAR] zero      0/ 42  ExtractOptionsField
[REGULAR] zero      0/ 28  ExtractConnectionStringField
[REGULAR] zero      0/ 11  GetConstructorParameters
```

`Initialize` is 143 lines of which **129 are covered**, and the bulk of those lines *are* the
pipeline lambdas — the `static (node, ct) =>` predicate and the multi-line `static (context2, ct) =>`
transform. Elsewhere the report lists lambdas as their own methods at `line-rate="1"`
(`<RegisterCodeFixesAsync>b__0`, `<RemoveAllUnusedUsings>b__0`) and instruments `<>c__DisplayClass`
closures normally. Lambdas compile to ordinary methods in the same assembly; coverlet rewrites IL and
instruments them like anything else. The only thing that matters is that the executing assembly is
the instrumented copy, which the `Assembly.Load`-from-bin-root harness already guarantees.

`coverlet.runsettings` is actively protecting this: its comment records that excluding
`CompilerGeneratedAttribute` would drop 194 lines, and closures carry that attribute, so the usual
"exclude generated code" snippet would blind the report to exactly this code.

**Consequence for planning.** The 428 uncovered lines in `InjectSourceGenerator` are not a harness
limitation — they are ordinary private helpers that no fixture reaches. 185 of them
(`ExtractConfigField`, `ExtractOptionsField`, `ExtractConnectionStringField`, `ClassifyType`) plus
the 98 in `ConfigSourceGenerator.Rules.cs` are unreachable for one reason: **no test declares a
`[Config]`, `[Options]` or `[ConnectionString]` field.** That is roughly 283 lines behind three
input files. R3.4 is a fixture-writing exercise, not an infrastructure one.

**The one genuine lambda-shaped gap: incrementality.** `GeneratorHarness.cs:63` calls
`RunGeneratorsAndUpdateCompilation` exactly **once**, so incremental-pipeline lambdas that only fire
on a second run — the equality comparers and caching paths — never execute. Running the driver twice
is the only way to verify incrementality at all: a generator whose comparers are wrong still emits
correct output, it just recomputes everything on every keystroke. Folded into R4.1b.

> **Measured, and it revises the above — see [S2](#s2--what-does-a-second-driver-run-actually-cover-gates-m1r31-and-r41b-2h).**
> The prediction that a second run would light up `Tools/ValueComparers/*` is **false**. Four
> incrementality tests were written and measured against the full-suite baseline: they add
> **3 lines** of coverage, all in `LangVersion.Comparer.cs`. `ValueTupleValueComparer` (45),
> `NullableValueTupleValueComparer` (45) and `PerCompilationCache` (20) stayed at exactly 0.
>
> The reason is simpler than the pipeline theory: **no generator in this repo uses tuple-typed
> pipeline values or the per-compilation cache.** Those are unexercised paths in a general-purpose
> library, not paths hidden behind a single run.
>
> A second correction from the same measurement: `Tools/ValueComparers/*` is **164/339 = 48%**
> covered, not the ~4% reported during investigation, and `ValueComparer.Helpers.cs` is 79/82, not 0.
> The low figure came from reading one report rather than the union of all 24. R3.1's real pool is
> ~153 uncovered lines, much of it genuinely unused library surface — a fair share of which may
> deserve deletion or `[ExcludeFromCodeCoverage]` rather than tests.
>
> R4.1b was built anyway and is kept: the four tests assert real behaviour that nothing else checks.
> It is now a **verification** item, not a coverage one.

## Requirements

### R1 — Bring shipped code into the denominator

**R1.1 — `MintPlayer.Assertions.SourceGenerator` test project.** *(highest value in this PRD — **done**)*

> **Delivered.** 58 tests. The component went from **absent from the report** to
> **594/756 = 78.6%**, against a projection of 40–60%. All four analyzers, all four code fixes and
> both generators are covered, including every MPAG001 rejection path — which had never been
> executed by anything.
>
> Two things learned, both now in the package:
> - The generators derive from Tools' `IncrementalGenerator`, so `MintPlayer.SourceGenerators.Tools`
>   must be referenced by the test project or `GetTypes` silently drops exactly those two types
>   while the analyzers load fine and mask it. The harness's error message now says so.
> - `FluentAssertionsMigrationCodeFixProvider`'s class doc claimed it emits
>   `using MintPlayer.Assertions.Execution;`. It does not, and should not — `AssertionScope` is in
>   the root namespace despite living in an `Execution/` folder. The inline comment was right, the
>   doc comment was stale, and the doc comment is what a test author reads first. Corrected.
>
> Remaining 162 uncovered are concentrated in `AssertionMethodDeclaration` (35),
> `EquivalencyScanner` (33), `UnawaitedAssertionAnalyzer.CodeFix` (27) and `EquatableArray` (13) —
> mostly generated equality members and defensive branches.

Create `Assertions/MintPlayer.Assertions.SourceGenerator.Tests`, wired with the harness that already
works in `SourceGenerators/MintPlayer.SourceGenerators.Tests`:

- `ProjectReference` with `ReferenceOutputAssembly="false" SkipGetTargetFrameworkProperties="true"`
- a `CopyGeneratorRuntimeAssets` target copying the generator DLL **and PDB** into `$(OutputPath)`
  (the bin **root** — coverlet's collector scans the test-host directory and does not recurse)
- `Assembly.Load` by name into the **default** ALC, then `CSharpGeneratorDriver`

**Hard constraint:** `MintPlayer.Assertions.Tests` must keep its existing
`OutputItemType="Analyzer"` reference — its 553 tests consume the generated code and would break.
This is a *second, separate* reference from a *new* project. Do not convert the existing one.

Cover, at minimum: each of the 4 analyzers producing its diagnostic on a triggering input and
staying silent on a clean one; each of the 4 code-fix providers producing compilable output;
`GenerateAssertionGenerator` and `EquivalencyRegistrationGenerator` happy path plus one diagnostic
path each.

**R1.2 — `MintPlayer.GraphQL` test project.** ~~48 LOC of pure string cleaning with zero
dependencies, the cheapest 0→~100% in the repo.~~

> **Re-scoped — not done, and not cheap.** The investigation's description was wrong. The package's
> single public method, `GraphQlExtensions.Run`, takes an `Octokit.GraphQL.Connection` and makes
> network calls; the only pure logic is the private `[GeneratedRegex]` helpers behind it. Covering
> it needs either a connection seam or widening the regex helpers to `internal` — a production
> change made for testability, not a free win. Deferred deliberately rather than forced; the
> package is 48 lines and moves the headline by ~0.2pp either way.

**R1.3 — `MintPlayer.Verz` CLI test project.** 290 LOC, currently referenced by no test project.
`VerzCommand`, `ToolCatalog`, `VersionPackagePathResolver`, `VerzConfig`. Note it is
`IsPackable=false` today, so this is verification value rather than consumer-facing risk — ranked
accordingly. **Blocked on R5.5** (see below): `InitDotnetCommand.Execute` will rewrite `<Version>`
across the entire repository if run with the default root, so it must not be executed by a test
until that is fixed.

**R1.4 — Do not chase the hardware-bound projects.** `EidReader` (358) and `EidReader.Native` (174)
need a live PC/SC session and `winscard.dll`; `AdminHelper` (64) relaunches the process elevated
through UAC; `Verz.Registry.NugetOrg` (39) hits nuget.org. Extracting seams for these is real work
with little coverage return and is explicitly [out of scope](#out-of-scope). `EidReader.Core` (292
LOC, the pure parsing layer) is already tested at 100% and is the correct model should anyone
revisit this.

### R2 — `Solve`

The decision is to test it, and the seams already exist.

> **Delivered. `Solve`: 302/1410 = 21.4% → 1020/1422 = 71.7%.** All seven command classes went from
> literally zero to 96–100%, on 241 tests, with no production seam work — S3 confirmed the existing
> interfaces were enough. Five fakes in `Solve.Tests/_Fakes/` carry the suite.
>
> **Found a real defect on the way.** `PrCommand.ProcessPrTemplate` substituted placeholders in
> dictionary insertion order, so `{issue_number}` matched the inside of `{{issue_number}}` and
> emitted `{42}` rather than `42`. Every doubled-brace form the code advertises was broken — the
> Handlebars-style spelling a template author is most likely to reach for. Fixed by substituting
> longest-placeholder-first.
>
> **Not done: R2.2, the four concrete I/O services** (`GitHubService` 154, `GitService` 94,
> `ClaudeService` 60, `ConsoleService` 59 — 367 lines). They shell out to `git`, `gh` and `claude`,
> and a process-runner seam does not exist. Left measured and visibly red rather than excluded: the
> decision on this PRD was to test `Solve`, not to hide it.

**R2.1 — Command tests against fakes.** All seven `*Command` types, driven through their
`System.CommandLine` handlers with in-memory fakes for `IGitService`, `IGitHubService`,
`IClaudeService`, `IConsoleService`. `PrCommand` (246 coverable) first — it alone is 22% of the
`Solve` problem. Then `WorkCommand` (100), `StatusCommand` (83), `PrdCommand` (80), `SolveCommand`
(80), `FeedbackCommand` (68), `InitCommand` (60).

Assert on observable behaviour — what was written to `IConsoleService`, which service calls were
made in what order, the process exit code — not on internal structure.

**R2.2 — Service tests where a seam exists or is cheap.** `GitService` and `GitHubService` shell out
to `git` and `gh`; the argument-construction and output-parsing halves are pure and worth testing
directly, even where the `Process.Start` call itself is not. Extract a minimal process-runner seam
only where it pays for itself.

**R2.3 — `[ExcludeFromCodeCoverage]` on `Program.cs` entry points.** `Solve/Program.cs` (18) and
`SlnLaunch/Program.cs` (21) are host-builder wiring, untestable in practice, permanently red. This
is the one place this PRD removes lines from the denominator, and it is the conventional case.
~39 lines.

### R3 — Generators: lift what is already measured

Ranked by lines-at-stake per unit of effort.

| # | Target | At stake | Approach |
|---|---|---:|---|
| R3.1 | `Tools/ValueComparers/*` — `ValueTupleValueComparer` (45), `NullableValueTupleValueComparer` (45), `PerCompilationCache` (20), `TypeTreeDeclaration.Comparer` (32) | ~142 | **Plain unit tests. No harness at all** — `SourceGenerators.Tools.Tests` uses a normal `ProjectReference`. Lowest-effort lines in the repo. Pair with the R4.1 re-run capability, which exercises these comparers the way the pipeline actually uses them (see [P4](#p4--the-uncovered-generator-mass-is-fixtures-not-harness)). |
| R3.2 | `WithComparerRoslynTypeAnalyzer` (5/108, 4.6%) | ~103 | One triggering + one clean compilation via the harness's existing `RunAnalyzerAsync`, already proven against this exact type. Best ratio in the instrumented set. |
| R3.3 | Diagnostic paths across all four generators (`*.Rules.cs`) | ~154 + branches inside the generators | Malformed-input fixtures. **This is [P3.1](#p3--the-suite-verifies-less-than-its-number-suggests), not a numbers item** — the coverage is a by-product of fixing a real verification gap. |
| R3.4 | `InjectSourceGenerator` (28% of 833) | ~602 | Largest single pool. Fixtures per feature branch: base-constructor params, `[Options]`, `[ConnectionString]`, post-construct. |
| R3.5 | `ServiceRegistrationsGenerator` (46% of 501) | ~263 | Lifetime / scanning / assembly-attribute permutations. |
| R3.6 | `Mapper` + `CliGenerator` producers (both ~58%) | ~404 | **Do last.** Remaining regions are deep permutation branches needing bespoke fixtures each — the worst ratio on the list. |

**R3.7 — Rename `ConfigSourceGenerator.Rules.cs`, do not delete it.** An earlier draft of this PRD
called it a dead orphan and proposed deleting it for a free 98-line denominator reduction. That was
wrong, and the check that caught it is worth repeating before any similar deletion: the file declares
`public static partial class DiagnosticRules`, and `grep` for its descriptors finds live emitters in
`InjectSourceGenerator.cs`. Rename to `InjectSourceGenerator.Config.Rules.cs`; its coverage arrives
with the R3.4 fixtures.

### R4 — Generator test ergonomics, and the `MSBuildWorkspace` question

Two halves, answered differently.

**R4.1 — The generic harness: adopt, and ship it.** *(Done — `MintPlayer.SourceGenerators.Testing`.)*

Rather than a shared file, the harness is now a publishable package sitting beside
`MintPlayer.SourceGenerators.Tools`, so other repos can consume it. Public surface:

```csharp
GeneratorHarness.ForAssembly("Acme.Generators").AddReferences(typeof(Marker))
    .RunGenerator(name, sources)        // GeneratorResult
    .RunGeneratorTwice(name, a, b)      // IncrementalGeneratorResult — R4.1b
    .RunAnalyzerAsync(name, sources)    // filtered to the analyzer's own ids
    .ApplyCodeFixAsync(analyzer, fix, source)
    .DescriptorsOf(name) / .CodeFixProvidersFor(id)
```

It hides the four things that are easy to get wrong and fail *silently*: loading via
`Assembly.Load` into the default ALC (anything else runs un-instrumented code), the
case-insensitive `build_property.rootnamespace` lookup, tolerating a partial
`ReflectionTypeLoadException`, and `trackIncrementalGeneratorSteps`. It also ships a
`CopyComponentUnderTest` MSBuild target so consumers declare
`<ComponentUnderTest Include="...dll" />` instead of hand-rolling a copy that must land in the bin
*root* and must include the PDB — the target infers the PDB and **errors** when the DLL is missing,
because a green run reporting 0% is indistinguishable from an untested component.

Both in-repo test projects now use it; `MintPlayer.SourceGenerators.Tests` keeps its old call shape
through a ~140-line adapter that holds only what is repo-specific (which four assemblies to probe,
which libraries fixtures compile against), down from ~330 lines of duplicated mechanics.

Two things fell out of building it, both recorded because they are the kind of detail that costs an
afternoon:

- `AddReference<T>()` cannot take a **static** class, and the natural anchor for a library is very
  often its extension class. Hence the non-generic `AddReferences(params Type[])`.
- The package **orders fixable diagnostics by source position** before offering the first one.
  `GetAnalyzerDiagnosticsAsync` promises no order; the old harness happened to return first-in-file,
  and migrating exposed a test that silently depended on it. Ordering makes "the first fixable
  diagnostic" mean something stable.

Note it adds itself to the denominator: `MintPlayer.SourceGenerators.Testing` is shipped code
referenced by test projects, so it is instrumented like any other package. That is correct — it is
published to NuGet — and it is well covered by construction, since every generator test exercises it.

The original sketch, kept because the declarative-case idea is still worth doing on top:

```csharp
public sealed record GeneratorCase(
    string Name,
    string Source,
    string[]? ExpectedDiagnostics = null,
    string[]? ExpectedGeneratedHints = null,
    Type[]? References = null);

public abstract class GeneratorTestBase<TGenerator> where TGenerator : IIncrementalGenerator
{
    protected static GeneratorRunResult Run(GeneratorCase c) => /* existing GeneratorHarness */;
    [Theory, MemberData(nameof(Cases))] public void Snapshot(GeneratorCase c) { … }
    [Theory, MemberData(nameof(Cases))] public void Diagnostics(GeneratorCase c) { … }
}
```

Each generator then declares fixtures as data rather than as methods, which is what makes R3.3–R3.6
affordable at all. Build this **first**, before R3.4 — it is the difference between six bespoke test
files and six fixture lists. It also directly serves R1.1, since the Assertions generator gets the
same base class for free.

**R4.1b — Add a re-run capability to the harness.** Per [P4](#p4--the-uncovered-generator-mass-is-fixtures-not-harness),
`GeneratorHarness.Run` drives the generator exactly once, so no incremental-pipeline comparer or
cache path ever executes. Add a `RunIncremental(initialSource, modifiedSource)` that reuses the same
`GeneratorDriver` across two `RunGeneratorsAndUpdateCompilation` calls and exposes each step's
`GeneratorRunResult.TrackedSteps`, so a test can assert which outputs were `Cached` versus
`Modified`. This is the only way to cover `Tools/ValueComparers/*` and `PerCompilationCache` **and**
the only way to verify incrementality — a generator that silently regenerates everything on every
keystroke currently passes CI. Requires `CSharpGeneratorDriver.Create` with
`driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true)`.

**R4.2 — `MSBuildWorkspace`: recommended against.** Raised as a possibility; here is the case
against, so the decision is on the record rather than silently dropped.

The decisive fact was measured in Phase 1 and holds: **anything that runs a generator through the
real compiler contributes exactly zero coverage.** Roslyn loads analyzers via `AnalyzerFileReference`
from the generator's *own* `bin/`, which coverlet never instrumented. `MSBuildWorkspace` is that
path. It would add a heavy dependency (`Microsoft.Build.Locator`, SDK resolution, real project
loading — slow and environment-sensitive in CI) and return **no coverage whatsoever**.

Its genuine value is verifying the MSBuild/packaging contract that the in-process harness cannot see.
But **R5.2 covers that risk more directly and more cheaply**: `dotnet pack` → local feed →
`dotnet build` a fixture project → assert the generated output compiles and behaves. That exercises
the real `.props`/`.targets`, the real pack layout, and the real NuGet resolution — the actual
consumer path — rather than an in-process approximation of a build. The pattern is already proven in
this repo by `TokenReplacer/MintPlayer.TokenReplacer.Tests/Integration/PackAndConsumeTests.cs`.

Recommendation: **build R4.1, build R5.2, skip `MSBuildWorkspace`.** Phase 1 reached the same
conclusion and listed it out of scope. If it is adopted anyway, it should be scoped explicitly as a
*verification* item with a stated expectation of zero coverage contribution, so the next person
reading the number is not misled.

### R5 — Phase 1 leftovers

**R5.1 — Fix `InjectPublicApiHashTask` swallowing every failure.**
`Verz/MintPlayer.Verz.Targets/InjectPublicApiHashTask.cs:19,28,46` — every failure path is
`catch { LogWarning; return true; } // do not break pack`. A pack that fails to inject the API hash
reports success. This is the unfixed half of Phase 1's defect D4, and unlike D13/D21 it was **not**
listed as out of scope, so it slipped rather than being decided. Return `false` on genuine failure;
keep the warning-and-continue behaviour only for the cases that are legitimately non-fatal, and say
in a comment which those are and why. Add a test per branch.

**R5.2 — Layer 5 packaging smoke test for the generators.** See R4.2. Pack each generator package,
restore it from a local feed into a fixture project, build, and assert the generated code is present
and compiles. Covers `build/*.props`/`.targets`, the `analyzers/dotnet/roslyn4.x/cs` layout, and
`GetDependencyTargetPaths*`. Model: `TokenReplacer.Tests/Integration/PackAndConsumeTests.cs`.
Include `MintPlayer.Assertions`' hand-rolled analyzer packaging (P1) — its `Configuration`-conditional
`None Include` for `MintPlayer.SourceGenerators.Tools.dll` means a Debug pack silently ships a
different payload than a Release pack.

**R5.3 — Golden known-answer hash in `FolderHasher.Tests`.** Pin a fixed input tree to a literal
expected hash, so a change of scheme fails loudly. Second half of Phase 1's R4.3. See P3.3.

**R5.4 — Correct the Phase 1 PRD text on snapshots.** *(Done — the correction is inline in
`docs/PRD-TestCoverage.md`, in Appendix D's snapshot row.)* R3.4 Layer 4 specifies `Verify.Xunit` 31.12.5;
the repo uses a hand-rolled `_Infrastructure/Snapshot.cs` instead (fewer dependencies, functionally
equivalent). The substitution is fine; the document describing something that does not exist is not.
Amend `PRD-TestCoverage.md` in place with a note.

**R5.5 — File the two issues Phase 1 identified and never filed.** Both are named in its Out of Scope
section as deserving their own issue:
- `Verz`'s `InitDotnetCommand.Execute` **rewrites `<Version>` across the whole repository** when run
  with the default root. Blocks R1.3.
- RFC-5988 relative `Link` target resolution against the request URI (`MintPlayer.Http`).

### R6 — Guardrails

**R6.1 — Narrow `ExcludeByFile`.** `coverlet.runsettings:26` is `**/*.g.cs,**/*.Designer.cs`; Phase 1
specified `**/obj/**/*.g.cs`. The widening is deliberate and documented, but it silently hides any
tracked `*.g.cs` added in future. Either narrow it to `obj/`, or add a CI check that no tracked file
matches `*.g.cs`.

**R6.2 — Do not add a coverage threshold gate yet.** Phase 1 deferred this pending stable figures.
It should stay deferred until Phase 2's milestones land, because M1 and M2 deliberately move the
number in opposite directions and a gate would fire on the honest dip.

## Outcome

**Measured locally over the full CI sequence, 25 test runs, 0 failures.** The local merge method in
Appendix A was independently confirmed at `5596e2b`, where it read 78.6% against the coverage
service's own `coverage/project` of **78.6% (+14.6% vs base 64.0%)** — an exact match, with
`coverage/patch` at **87.1% of added lines** (210 of 241). Both check runs are `neutral`
(informational; the repository's coverage gate has Blocking off), which `gh pr checks` renders as
`skipping` — that is not a failure to upload.

Earlier readings in this document that lag the head — 75.3% at `c9007ed`, 78.6% at `5596e2b` —
predate later tranches of generator tests.

<a id="r36-outcome"></a>
### R3.6 outcome: the estimate was wrong, and the tests found a shipped bug

R3.6 was ranked last in this plan and described as "the worst ratio on the list — deep permutation
branches needing a bespoke fixture each". **That was wrong, and the error was in not looking before
estimating.** Reading the merged report line-by-line rather than by file total showed the uncovered
regions were not permutations at all but *whole features with no fixture*, several in single
contiguous blocks: the assembly-level `[GenerateMapper]` overload is 46 unbroken lines reached by one
fixture. 14 tests moved 307 lines — about 22 lines per test, an order better than the estimate implied.

| | Before | After |
|---|---|---|
| `SourceGenerators/Mapper` | 56.9% | **88.1%** |
| `SourceGenerators/Cli` | 56.6% | **78.1%** |
| Repo lines | 78.6% | **81.2%** |
| Repo branches | 58.9% | **61.6%** |

Two findings came out of it, and they are the argument for having done it at all:

**A shipped defect in `MintPlayer.CliGenerator`.** The producer emitted `option.IsRequired = true`
and `option.IsHidden = true`. Neither exists on `System.CommandLine`'s `Option<T>` — they are
2.0.0-beta names dropped before GA, where the properties became `Required` and `Hidden`. Any consumer
writing `[CliOption(Required = true)]` or `Hidden = true` got **CS1061 in generated code they cannot
edit**. The rest of the producer already targeted post-beta API (`DefaultValueFactory`,
`parseResult.GetRequiredValue`), so these two lines were simply missed in that migration. Nothing
caught it because nothing in the repository sets either facet — not `Verz`, not the
`CliCommandDebugging` playground, and no test — so the emitted code had never been compiled with a
required or hidden option in it. Fixed, and pinned by
`CliCommandFeatureTests.OptionMetadata_IsEmittedForEveryFacet`.

**A test that was not testing anything.** `ItBuildsACommandTree` declared a subcommand with
`[CliCommand("build")]` and no parent, then asserted only that `Errors` was empty and that something
had been generated. Both held — but a non-nested command without `[CliParentCommand]` is silently
dropped from the tree, so the generated output was the root and nothing else. The test named for
building a command tree never checked that the tree was built, and discarded its own subcommand on
every run since it was written. It now declares the parent and asserts the subcommand and its option
are present.

The silent dropping is pinned by `AnOrphanCommand_IsSilentlyDroppedFromTheTree` rather than changed.
Discarding a decorated command with no diagnostic is a poor failure mode — the consumer gets a CLI
missing a subcommand and nothing to search for — but that is a design decision, not a test fix.

| | Before (`c7b13b9`) | After (branch head) | |
|---|---|---|---|
| Lines | 6,747 / 10,544 = **64.0%** | 9,321 / 11,475 = **81.2%** | **+17.2pp** |
| Branches | 2,847 / 6,001 = 47.4% | 4,099 / 6,652 = 61.6% | +14.2pp |
| Files measured | 261 | 286 | |

The denominator **grew by 893 lines** while the percentage rose, which was the point of the
correctness-first decision: the old 64.0% was flattering because `MintPlayer.Assertions.SourceGenerator`
was hidden from it entirely.

| Folder | Before | After |
|---|---|---|
| Assertions *(now includes the generator)* | 2332/2437 = 95.7% | 2926/3193 = **91.6%** |
| Solve | 302/1410 = 21.4% | 1008/1392 = **72.4%** |
| SourceGenerators | 2255/4537 = 49.7% | 3523/4745 = **74.2%** |
| SlnLaunch | 372/498 = 74.7% | 370/477 = 77.6% |
| FolderHasher | 170/214 = 79.4% | 173/217 = 79.7% |
| Verz | 78/126 = 61.9% | 81/129 = 62.8% |

Defects found and fixed while writing the tests — note that **three of the four were found by the
tests rather than by reading the code**, and the last one was found by CI on the golden test's first
run:

1. **`PrCommand` template placeholders** — every doubled-brace form (`{{issue_number}}`,
   `{{issue_title}}`, `{{pr_type}}`, `{{changes}}`, `{{labels}}`, `{{author}}`) rendered as
   `{42}` rather than `42`, because substitution ran in dictionary insertion order and the single-brace
   form matched the inside of the double. Fixed by substituting longest-placeholder-first.
2. **`InjectPublicApiHashTask`** — returned `true` from every failure path, so a pack that could not
   record its API hash reported success (R5.1).
3. **`FluentAssertionsMigrationCodeFixProvider`'s class doc** contradicted its own implementation
   about `using MintPlayer.Assertions.Execution;`. The code was right; the doc was what misled.
4. **`FolderHasher` produced a different hash on Windows and Linux** for an identical tree, because
   the relative path was hashed with the OS directory separator (`sub\b.txt` vs `sub/b.txt`); the
   adjacent `.ToLower()` was culture-sensitive as well. Since the hash is a cache key, the failure
   was silent — a Windows developer and a Linux runner could never share an entry, and every lookup
   missed. Normalised to `/` and `ToLowerInvariant`; Windows now converges on the value Linux was
   already producing, so Linux-side caches survive and only Windows-computed ones invalidate once.
   **Caught by R5.3's golden test on its first CI run** — the thing all 13 pre-existing relative
   tests were structurally incapable of seeing.
5. **`ServiceRegistrationsGenerator` factory handling** — a `[RegisterFactory]` whose signature the
   DI overload cannot accept was emitted as a bare method group and failed with CS1503 in the
   *consumer's* build; and a factory returning the implementation was silently ignored when
   registering an interface, so the container constructed the service itself and the factory never
   ran. Both found by asserting the generated code compiles.
6. **`DescriptionSourceGenerator`** emitted `partial class` for a documented `record`, producing
   CS0261 in the consumer. A record class reports `TypeKind.Class`; `IsRecord` was never consulted.
7. **Generator packages shipped with no analyzer in them from a clean `dotnet pack`** (R5.2). The
   analyzer payloads were collected by static `ItemGroup` globs over sibling `bin` folders, which
   MSBuild expands at *evaluation* time — before those projects are built. The packages looked
   correct only because a prior full build had populated those folders. Now collected in
   `TargetsForTfmSpecificContentInPackage` targets with `<Error>` guards, per concern, in the
   `eng/` file that owns it.

### Defects introduced by this branch and caught before merge

Recorded because the ratio matters when judging the work:

- The `FolderHasher` fix was **incomplete** — the separator was normalised for hashing but the file
  ordering still sorted raw OS paths culture-sensitively, and feed order is part of the hash. The
  golden fixture had no filename straddling the separator in sort order, so it passed.
- The `<Error>` guards in `sourcegenerator.targets` **could never fire**: a literal `Include`
  creates the item regardless of file existence, so `'@(x)' == ''` is always false.
- The factory fix (5 above) initially handled only the parameterless case and used a conversion
  check too permissive for a method group.
- `MintPlayer.ValueComparerGenerator.Attributes.dll` was removed from the payload as "stale
  residue". It is required at generator load time; removing it disables the package.
- Windows-only path separators in a test theory; an over-broad `catch` in the probe; `.Single()`
  contradicting a documented contract; a silently ignored parameter.

The first four were found by a `/code-review` pass over the branch and by the repo owner, not by the
author. `SourceGenerators/CLAUDE.md` records the pattern behind them.

Not delivered, and why:

| Item | Status |
|---|---|
| **R2.2** — `Solve`'s four concrete I/O services (367 lines) | Need a process-runner seam that does not exist. Left measured and red rather than excluded. |
| **R1.2** — `MintPlayer.GraphQL` | Re-scoped: not the cheap win it was described as. See R1.2. |
| **R1.3** — `MintPlayer.Verz` CLI | Blocked on [#173](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/173), now filed. `InitDotnetCommand.Execute` cannot be called from a test in this repository until its default root is safe — a test with the wrong working directory would rewrite every `.csproj` as a side effect of `dotnet test`. Also see [#175](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/175): nothing in the repo consumes Verz, and whether it is finished or archived is undecided. Testing it is premature until that is settled. |
| **R5.2 / S4** — packaging smoke test | **Done.** 13 tests. Found a configuration-dependent analyzer payload in three places; see [S4](#s4--pack-and-consume-for-a-generator-not-just-an-msbuild-task-gates-m5r52-3h). |
| **R3.5** — `ServiceRegistrationsGenerator` | **Done.** 38 tests over the attribute shapes; found the two factory defects above. |
| **R3.6** — `Mapper` / `Cli` producers | **Done, and the estimate was wrong.** Mapper 56.9% → **88.1%**, Cli 56.6% → **78.1%**; 14 tests, +307 lines overall. See [R3.6 outcome](#r36-outcome). |
| **R5.4** — correct the Phase 1 PRD on snapshots | **Done.** `docs/PRD-TestCoverage.md` now carries the correction inline. |
| **R5.5** — the two unfiled issues | **Done.** [#173](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/173) (`verz init-dotnet` rewrites `<Version>` repo-wide by default) and [#174](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/174) (`GetPaginationLinks` drops relative `Link` targets). Both verified against the code before filing. |
| **`InjectSourceGenerator` remainder** | ~382 lines still uncovered after the `[Config]`/`[Options]`/`[ConnectionString]` fixtures took it 30% → 54.8%. |

### Also delivered, beyond the original plan

- **`MintPlayer.SourceGenerators.Testing`** — the harness became a publishable package beside
  `Tools`, consumed by both in-repo test projects. Replaced ~330 lines of duplicated mechanics with
  ~140 lines of configuration.
- **`SourceGenerators/CLAUDE.md`** — the `eng/` layout, the packaging traps, the coverage facts, and
  the specific wrong turns taken here.
- **The `eng/` packaging refactor** — each analyzer payload now lives in the targets file that owns
  its concern, collected after Build and guarded.

## Milestones

Projections are estimates from measured coverable-line counts and the coverage rates the existing
harness achieves on comparable code (50–70%). They are directional, not commitments — the coverable
count for code not yet in the denominator is derived from the repo's observed LOC→coverable ratio of
roughly 0.55.

| Milestone | Content | Δ lines | Projected total |
|---|---|---|---|
| **Baseline** | master `c7b13b9` | — | **6,747 / 10,544 = 64.0%** |
| **M0 — Harness** | R4.1 generic base class, R4.1b re-run; R3.7 rename | +0 | 6,747 / 10,544 = 64.0% |
| **M1 — Cheap lines** | R3.1 comparers *(re-scoped by S2)*, R3.2 analyzer, R2.3 `[ExcludeFromCodeCoverage]` (−39) | +130 / −39 | ~6,880 / 10,505 = **65.5%** |
| **M2 — `Solve`** | R2.1 commands, R2.2 services | +756 | ~7,636 / 10,505 = **72.7%** |
| **M3 — Honest denominator** | R1.1 Assertions generator (+~690 coverable), R1.2 GraphQL (+~26) | +445 / +716 | ~8,081 / 11,221 = **72.0%** ← *the deliberate dip* |
| **M4 — Generator lift** | R3.3 diagnostics, R3.4 Inject (+ the 98 Config rules), R3.5 ServiceRegistrations | +900 | ~8,981 / 11,221 = **80.0%** |
| **M5 — Verification** | R5.1 InjectPublicApiHashTask, R5.2 packaging smoke, R5.3 golden hash, R1.3 Verz CLI | +180 / +160 | ~9,161 / 11,381 = **80.5%** |
| **M6 — Long tail** *(optional)* | R3.6 Mapper/Cli producers, SlnLaunch 128, ObservableCollection 34, TokenReplacer 38 | +400 | ~9,561 / 11,381 = **84.0%** |

**M3 is where the number goes down.** That is the point of the correctness-first decision, and it
should be stated in the PR description rather than explained away afterwards.

Ordering rationale: M0 first because it makes M4 affordable. M1 before M2 because it is nearly free
and builds confidence in the harness. M2 before M3 because it banks a large, unambiguous rise
immediately before the deliberate dip. M5 carries the items that are about correctness rather than
percentage, and must not be dropped if the number is judged good enough at M4 — R5.1 in particular
is a live bug.

## Spikes

Timeboxed investigations to run **before** committing to the milestone they gate. Each has a stated
question, a box, and a decision rule — if the box expires without a clear answer, take the fallback
and move on rather than extending it.

### S1 — Does the Assertions generator load cleanly under the harness? *(gates M3/R1.1, 2h)*

**Question.** `MintPlayer.Assertions.SourceGenerator` is `netstandard2.0` and depends on
`MintPlayer.SourceGenerators.Tools`, which polyfills BCL types. The existing harness already handles
that collision for four generators via `ReferenceOutputAssembly="false"` + bin-root copy — but the
Assertions generator also carries `EquatableArray` and an `EquivalencyScanner` that walk referenced
assemblies, which may need reference-set seeding the current `BuildCompilation` does not do.

**Do.** Add the project reference and copy target to a throwaway test project; `Assembly.Load` it;
run one analyzer and one generator against a two-line fixture. Confirm the package appears in a
cobertura report.

**Decision rule.** If it loads and attributes coverage → proceed with R1.1 as written. If the
reference set needs seeding → note which assemblies and proceed (cost: a few lines). If it cannot
load in-process at all → fall back to a dedicated test project targeting `netstandard2.0`-compatible
surface only, and re-scope R1.1's estimate down.

**Why it is a spike, not a task.** R1.1 is the single largest item in this PRD and M3's projected
+690 coverable lines rests entirely on the harness transplanting cleanly. Finding out on day four is
much worse than finding out in two hours.

**RESULT — run 2026-09-03. Clean pass. Proceed with R1.1 as written.**

`Assertions/MintPlayer.Assertions.SourceGenerator.Tests` builds, loads the generator by name, and
runs both analyzers and generators. `MintPlayer.Assertions.SourceGenerator` **now appears as a
package in the cobertura report**, which it never has before. No reference-set seeding beyond
`MintPlayer.Assertions` itself was needed; `EquivalencyScanner` and `EquatableArray` loaded without
special handling. `Microsoft.CodeAnalysis.CSharp.Workspaces` is required — reflecting over the
assembly loads the four `CodeFixProvider` types, and `GetTypes` throws without it.

**Newly measurable surface: 756 coverable lines** (estimate was ~690 — M3's projection stands, and
is slightly conservative). Three smoke tests already cover 50 of them (6.6%).

One useful accident: all four `*.Rule.cs` files went to **100%** off a single analyzer
instantiation, because `DiagnosticRules` is one static partial class whose initializer constructs
every descriptor. The same shape should hold for the generators' `*.Rules.cs` files in R3.3 — the
descriptors themselves are nearly free; what R3.3 actually buys is the *emission* paths.

### S2 — What does a second driver run actually cover? *(gates M1/R3.1 and R4.1b, 2h)*

**Question.** [P4](#p4--the-uncovered-generator-mass-is-fixtures-not-harness) predicts that running
the driver twice will execute the comparer and cache paths in `Tools/ValueComparers/*`. That is
inference from the 0% figures, not a measurement.

**Do.** Take one generator, run it twice through a shared driver with a modified compilation, and
diff the resulting cobertura against a single-run baseline.

**Decision rule.** If the comparers light up → R4.1b is confirmed and R3.1's ~142 lines are largely
free. If they do not → they need direct unit tests instead, R4.1b's value drops to incrementality
*verification* only (still worth building, but it stops being a coverage item), and M1's estimate
comes down.

**RESULT — run 2026-09-03. The comparers did not light up. Fallback taken.**

Four tests in `Generators/IncrementalityTests.cs`, measured against the full-suite baseline:
**+3 lines**, all in `LangVersion.Comparer.cs`. `ValueTupleValueComparer`,
`NullableValueTupleValueComparer` and `PerCompilationCache` stayed at 0 — no generator here uses
tuple-typed pipeline values or that cache. Also corrected: `Tools/ValueComparers/*` was already
48% (164/339), not ~4%.

Consequences, applied to this document: R3.1 becomes direct unit tests over a smaller, honestly
sized pool; R4.1b is retained as verification; M1's estimate drops from +245 to +130. The four
tests are kept — they assert incremental behaviour nothing else covers, and
`AnUnrelatedEditIsServedFromCache` would fail today if a comparer regressed.

### S3 — One `Solve` command end to end, before writing seven *(gates M2, 3h)*

**Question.** `Solve`'s commands are `System.CommandLine` handlers. How much ceremony does it take to
invoke one in-process with fakes, and does the DI wiring in `Program.cs` need to be reachable from a
test?

**Do.** Write the full test file for `InitCommand` (60 coverable, the smallest) against fakes for all
four service interfaces. Measure the resulting coverage of that one file.

**Decision rule.** If `InitCommand` reaches >70% for a reasonable test file → extrapolate to the
remaining six and M2's +756 stands. If invocation needs significant production-code change → stop and
re-scope: the middle path from the original decision (test commands, `[ExcludeFromCodeCoverage]` on
the I/O shells) becomes the recommendation, and that is a decision to bring back rather than take
unilaterally.

### S4 — Pack-and-consume for a generator, not just an MSBuild task *(gates M5/R5.2, 3h)*

**Question.** `TokenReplacer.Tests/Integration/PackAndConsumeTests.cs` proves the pattern for an
MSBuild targets package. A *generator* package is harder: the fixture build must resolve
`analyzers/dotnet/roslyn4.x/cs`, and the test must assert that generated code appeared — inside a
child `dotnet build`, offline, from a local feed.

**Do.** Get exactly one generator package (`MintPlayer.SourceGenerators`) packed, restored into a
fixture from a temp feed, built, and asserted on.

**Decision rule.** If it works → replicate for the rest, and R4.2's argument against
`MSBuildWorkspace` is settled by demonstration. If the child build is too slow or too flaky for CI →
reconsider, and `MSBuildWorkspace` re-enters the conversation as the cheaper way to get *some*
packaging signal. **This spike is the one that could overturn an R4.2 recommendation**, which is why
it is scheduled before M5 rather than during it.

**RESULT — run 2026-09-03. Works, ~18s in-suite. R4.2 stands; `MSBuildWorkspace` not needed.**

13 tests in `SourceGenerators/MintPlayer.SourceGenerators.Tests/Packaging/`. A shared
`IClassFixture` packs six real projects into a temp feed once; a consumer restores from it and
builds code that cannot compile without the generated constructor, so build success is the
assertion.

**It found the defect it was built to look for, then a second one during the fix.** The analyzer
payload was conditioned on `Configuration == 'Release'` in three places — `sourcegenerator.targets`
(Tools.dll, twice), an entire `ItemGroup` in `MintPlayer.SourceGenerators.csproj` (the Attributes
DLLs and `DependencyInjection.Abstractions`), and `MintPlayer.Assertions.csproj` (Tools.dll again).
A plain `dotnet pack` defaults to Debug and therefore produced a package whose generator cannot
resolve its own base class: restores cleanly, reports nothing, never runs. CI packs Release, so
nothing broken shipped.

The second condition was caught only because the guard test **compares the Debug and Release
payloads to each other** instead of asserting a fixed list — a `Should().Contain(...)` would have
gone green after the first fix.

Also learned: `MintPlayer.SourceGenerators.Tools` is a *transitive* package dependency via
`MintPlayer.ValueComparers.NewtonsoftJson`, despite the generator referencing it privately. Omitting
it from the feed fails with NU1102 pointing at nothing useful. No in-process test can see that.

## Acceptance criteria

1. All 22+ test projects green; no test skipped to make a number.
2. Merged local figure reproduces the published figure to within 2 lines
   ([Appendix A](#appendix-a-measurement-method)).
3. `MintPlayer.Assertions.SourceGenerator` appears in the coverage report at all — currently it does
   not — with every analyzer and code-fix provider covered by at least one triggering and one clean
   case.
4. No `*.Rules.cs` file remains at 0%.
4b. At least one test asserts incremental behaviour (a second driver run producing `Cached` steps),
   per R4.1b.
5. `dotnet pack` of every generator package is exercised by a pack-and-consume test.
6. `InjectPublicApiHashTask` returns `false` on genuine failure, with a test per branch.
7. `FolderHasher` has at least one literal expected hash.
8. Every `[ExcludeFromCodeCoverage]` added carries a comment saying why the code cannot be tested.

## Out of scope

Genuinely not being done — not a parking lot:

- **`MSBuildWorkspace` end-to-end tests** — see R4.2. Zero coverage contribution; R5.2 covers the
  same risk better. Revisit only if R5.2 proves insufficient.
- **Hardware- and environment-bound projects**: `EidReader` (358), `EidReader.Native` (174, P/Invoke
  to `winscard.dll`), `AdminHelper` (64, UAC), `Verz.Registry.NugetOrg` (39, live network). Seam
  extraction for these is a separate piece of work.
- **`CodeMigrations.Runner` / `CodeMigrations.Tools`** — a 3-line `Program.cs` shipping as the
  `migrate-workspace` tool and a 5-line `MigrationConfig`. Effectively empty packages on nuget.org.
  Worth a decision about whether they should ship at all; not a coverage question.
- **A coverage threshold / merge gate** — R6.2, deferred again deliberately.
- **Multi-TFM test projects beyond `Assertions.Tests`** — carried over from Phase 1.
- **Phase 1 defects D13 and D21** — remain deliberately deferred and characterized by test.

## Appendix A — Measurement method

Reproduces the published figure to within one line.

```
dotnet restore
dotnet build -c Release --no-restore
dotnet test --no-restore --no-build -c Release \
  --settings coverlet.runsettings \
  --collect:"XPlat Code Coverage" --results-directory coverage
```

Then merge the 24 per-project `coverage/**/coverage.cobertura.xml` reports: resolve each `filename`
against every `<source>` prefix **in its own report**, suffix-match against `git ls-files`
(case-insensitively, on Windows), drop unresolvable paths, and max-merge hit counts per file per
line.

**The trap:** the `<sources>` root differs per report. Most emit repo-root-relative filenames
(`Assertions/MintPlayer.Assertions/Formatter.cs`), but the Assertions test host emits
project-relative ones (`Formatting/Formatter.cs`) with a matching `<source>` element. Naive dedupe on
the `filename` attribute treats those as different files, silently discards the real Assertions
coverage, and lands at **48.1%** instead of 64.0%.

Verified 2026-09-03 against master `c7b13b9`: merged **6,747/10,544 = 64.0%**, 2,847/6,001 branches,
261 files, versus the server's 6,748/10,544. The 1-line delta is
`Solve/obj/Release/net10.0/.../Inject.g.cs`, a generated file under `obj/` that the `git ls-files`
rule drops and the server counts — so the published number is very slightly inflated.

Instrumentation is otherwise clean: that one file is the *only* thing the git-tracking filter
removes. No `.Sample`, `.Demo`, `DemoWebApp`, `AvaloniaTest` or `TestProjects/*Debugging` file
appears in any report.

## Appendix B — Evidence

Baseline from [coverage.mintplayer.com, commit `c7b13b9`](https://coverage.mintplayer.com/po/commit/Commits%2F216014918%2Fc7b13b9e5a6fe5c69b619d21f66718146920f9d1),
cross-checked against a local run of the sequence above. Per-folder, per-file and top-40-uncovered
tables were produced by a merge script following Appendix A; LOC figures for code absent from the
reports are non-blank, non-comment-only C# lines excluding `bin`/`obj`/`sandbox`/fixtures.
