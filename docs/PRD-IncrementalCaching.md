# PRD: make the source generators actually incremental

## Overview

Every generator in this repo is built on `MintPlayer.SourceGenerators.Tools`: models marked
`[AutoValueComparer]`, pipelines guarded by the generated `.WithComparer()`, and output registered through
`context.ProduceCode(...)` / `context.ReportDiagnostics(...)`. The design intent is a textbook incremental
pipeline: an edit the generator does not care about is absorbed by a comparer, and the output step is served
from cache.

**None of the three layers delivers that today**, and no test notices, because the only incrementality test
(`IncrementalityTests.AnUnrelatedEditIsServedFromCache`) asserts that *at least one* tracked step was cached —
which the syntax-provider steps satisfy no matter what happens downstream.

This came up when a session in `MintPlayer.AspNetCore.Tools` concluded that the house convention is to
"register producers directly, skipping `ProduceCode`, since it combines with `CompilationProvider` and breaks
caching". The convention part is wrong — all 11 shipped generators use `ProduceCode`, and no code, commit or
doc says otherwise — but the caching part is right, and it is only the first of three defects. The answer is
to fix the helper so every caller is correct, not to route around it.

Line numbers are against master `dfcf317`; paths are relative to `SourceGenerators/` unless stated otherwise.

## Problem Statement

### Defect 1 — `ProduceCode` pins every output step to the `Compilation`

`MintPlayer.SourceGenerators.Tools/Extensions/GeneratorExtensions.cs:19-22` (one provider) and `:38-46`
(several) combine each producer with `context.CompilationProvider`. A `Compilation` is a new object on every
edit, so the combined value is new every time and the `SourceOutput` step reports `Modified` on every
keystroke. The multi-provider path additionally funnels all producers into **one** output step, so a change to
one producer re-emits every file of that generator (ClassNames, Mapper).

The `Compilation` is never used: `Producer.Produce(SourceProductionContext, Compilation)` (`Producer.cs:45`)
accepts it and does not pass it on; the abstract `ProduceSource` (`:43`) has no such parameter. The cache-safe
registration is already in the file, commented out at `GeneratorExtensions.cs:18`.

### Defect 2 — `ReportDiagnostics` pays the same cost for a real but narrow reason

`GeneratorExtensions.cs:64-95` has the same shape. Here the compilation *is* used —
`IDiagnosticReporter.GetDiagnostics(Compilation)` turns stored `LocationKey`s back into in-tree `Location`s
(`Extensions/LocationExtensions.cs:27-39`) — but in the overwhelmingly common case the reporter has no
diagnostics at all, and the step still re-runs on every edit.

### Defect 3 — the generated value comparers are never used

This is the big one: fixing defect 1 alone would leave **9 of the 11** generators re-running on every edit.

- The generated `.WithComparer()` extensions resolve through `ComparerRegistry.For<T>()`
  (`ValueComparerGenerator/.../ValueComparerGenerator.Producer.cs:172-184`).
- `For<T>()` falls back to `EqualityComparer<T>.Default` when nothing is registered
  (`MintPlayer.SourceGenerators.Tools/ValueComparers/ValueComparer.Registry.cs:44-45`).
- The only registrations are `AnalyzerInfo`, `LangVersion`, `Settings`
  (`ValueComparer.ModuleInitializer.cs:12-14`) and `JObject` (`ClassNamesSourceGenerator.cs:18`).
- The generator emits `[ValueComparer(typeof(XValueComparer))]` on every `[AutoValueComparer]` model, and the
  hand-written models carry the same attribute (`LocationKey.cs:7`, `SymbolExtensions.cs:197,213`, the
  ValueComparerGenerator's own models) — **but nothing ever reads that attribute.**

So every model class is compared by reference. The syntax transforms allocate new models on each run, the
`Collect` step is therefore `Modified`, the `Select` that builds the `Producer` re-runs, and the output is
regenerated. The nested `ValueComparer<T>.IsEquals` helper goes through the same registry, so nested models,
`LocationKey` and `PathSpec` compare by reference too.

### Defect 4 — per-generator inputs that no comparer can fix

Once defects 1–3 are fixed, these still break caching (from a static audit, to be confirmed by Spike 1):

| Generator | Problem | Location |
|---|---|---|
| ServiceRegistrations | `CompilationProvider.Select`s return reference types with no comparer (`AssemblyRegistrationConfig`, `ServiceRegistration[]`); class provider's `SelectMany(...).Collect()` has no comparer; per-class transform returns `ServiceRegistration[]` | `ServiceRegistrationsGenerator.cs:43-172, 178-179, 182-282, 288-315` |
| GenericMethod | model holds `SyntaxTokenList MethodModifiers/ClassModifiers` (compared by syntax identity); no comparer before `Collect` | `Models/MethodDeclaration.cs:14-15`, `GenericMethodSourceGenerator.cs:71` |
| Inject | no `.WithComparer()` before `Collect` | `InjectSourceGenerator.cs:155` |
| ValueComparer | anonymous-type model; downstream `Select`s return deferred LINQ `IEnumerable`s | `ValueComparerGenerator.cs:37-130, 140-197` |
| CliCommand | `ImmutableArray<CliCommandTree>` compared by reference unless an array comparer applies | `CliCommandSourceGenerator.cs:26-27` |
| GenerateAssertion | source-path model carries `Location`, so the file is regenerated whenever the method moves | `AssertionMethodDeclaration.cs:76, 91` (Assertions) |
| (all) | `IncrementalGenerator` derives `LangVersion` by walking `CompilationProvider.SyntaxTrees` on every edit | `MintPlayer.SourceGenerators.Tools/IncrementalGenerator.cs:22-65` |

Also: no generator uses the `ICompilationCache` third `Initialize` parameter (`IncrementalGenerator.cs:13-15`,
itself keyed on the `Compilation`, so new every edit). It stays — it is public API — but nothing may flow it
into a producer.

## Goals

1. After an edit a generator does not care about (a method body changes), **every `SourceOutput` step of every
   shipped generator reports `Cached` or `Unchanged`.** Measured, not argued.
2. After a relevant edit, output changes and — for generators with more than one producer — only the producers
   whose inputs changed re-run.
3. `context.ProduceCode(...)` and `context.ReportDiagnostics(...)` remain the one way to register output. No
   caller changes are required to get the benefit; existing call sites compile unchanged.
4. `[ValueComparer(typeof(...))]` means what it says: any type carrying it is compared with that comparer by
   `ComparerRegistry` and by `ValueComparer<T>.IsEquals`, with no manual registration.
5. A regression test that fails on today's code for each of the above, so this cannot silently regress again.

## Non-goals

- Changing generated output. Every generated file must be byte-identical before and after (the existing
  snapshot/feature tests enforce this).
- Removing public API. `Producer.Produce(SourceProductionContext, Compilation)` and the `ICompilationCache`
  parameter stay, the former marked `[Obsolete]`.

## Design

### D1 — `ProduceCode`: one output step per provider, no compilation

```csharp
public static void ProduceCode(this IncrementalGeneratorInitializationContext context,
    params IncrementalValueProvider<Producer>[] providers)
{
    foreach (var provider in providers)
        context.RegisterSourceOutput(provider, static (spc, p) => p?.Produce(spc));
}

public static void ProduceCode(this IncrementalGeneratorInitializationContext context,
    IncrementalValuesProvider<Producer> providers)
    => context.RegisterSourceOutput(providers, static (spc, p) => p?.Produce(spc));
```

`Producer` gains `Produce(SourceProductionContext)`; the two-argument overload forwards to it and is marked
`[Obsolete]`. Each provider gets its own output node, so producers cache independently. Hint-name uniqueness is
unchanged (each producer already has its own `Filename`).

`Producer` itself needs no `Equals`: when the upstream `Select` is cached, the driver hands back the same
instance, which the default comparer accepts.

### D2 — `ReportDiagnostics`: pay for the compilation only when there is something to report

Shaped by Spike 2. The in-tree `Location` must be kept (pragma and `.editorconfig` severity are applied per
syntax tree), so the compilation cannot be dropped outright; the goal is that the zero-diagnostics case does no
per-edit work. See *Spike results → S2*.

### D3 — `ComparerRegistry` honours `[ValueComparer]`

On a miss, `TryGet<T>` inspects `typeof(T)` for `ValueComparerAttribute`, instantiates the comparer (a public
static `Instance` if present, else a non-public-capable parameterless constructor), registers it, and caches
negative lookups so the reflection happens once per type per process. `For<T>()`, the generated
`.WithComparer()` and nested `IsEquals` all go through `TryGet`, so this single change activates every
generated and hand-written comparer in this repo — and in any third-party generator using the package.

Explicit `Register` / `TryRegister` keep precedence (an explicit registration is never overridden by the
attribute).

### D4 — per-generator fixes

Each row of the defect-4 table gets the minimal fix that makes its inputs value-comparable: add the missing
`.WithComparer()`, replace syntax/`Location` members with strings or `LocationKey`, materialise deferred
`IEnumerable`s into arrays with a structural comparer, give `CompilationProvider`-derived configuration a
comparer. `IncrementalGenerator` reads the language version from `context.ParseOptionsProvider` instead of
walking the syntax trees.

### D5 — tests

- `IncrementalGeneratorResult` (`MintPlayer.SourceGenerators.Testing/Results.cs`) gains `OutputReasons` (from
  `Second.TrackedOutputSteps`) and `OutputsFullyCached`, so tests can assert on the step that matters.
- An `[Theory]` over every generator reachable from `MintPlayer.SourceGenerators.Tests` (ClassNames,
  ServiceRegistrations, Description, Inject, GenericMethod, Mapper, CliCommand, ValueComparer, JoinMethod),
  each with a fixture that triggers it and an unrelated method-body edit: all output reasons must be `Cached` or
  `Unchanged`. Equivalent tests for GenerateAssertion and EquivalencyRegistration in the Assertions generator
  test project.
- A multi-producer test (ClassNames or Mapper): a relevant edit to one producer's input leaves the other
  producer's output `Cached`.
- `ComparerRegistry` unit tests in `MintPlayer.SourceGenerators.Tools.Tests`: attribute resolution, `Instance`
  vs constructor, explicit registration wins, negative cache, nested `IsEquals` uses the attribute comparer.
- The existing `AnUnrelatedEditIsServedFromCache` is tightened to assert on output steps.

## Spikes

Run before implementation; results are recorded below and adjust the milestones.

- **S1 — baseline measurement.** Add `OutputReasons` to the test harness and run the D5 theory against
  unmodified code. Records the actual per-generator output reasons today, and confirms (or corrects) the static
  audit behind defects 3 and 4. Then apply D1 alone and re-measure, then D1+D3, to attribute each generator's
  remaining misses to a specific cause.
- **S2 — diagnostics without per-edit cost.** Establish empirically whether a generator can report a diagnostic
  at `Location.Create(path, span, lineSpan)` (no compilation), and whether pragma / severity configuration still
  apply to it; and whether a `Combine` of an *empty* `IncrementalValuesProvider` with `CompilationProvider`
  produces no re-run output step. Picks D2's shape.
- **S3 — consumers outside this repo.** Search sibling repositories for `IDiagnosticReporter`,
  `ReportDiagnostics(`, two-argument `Produce(` and `ProduceCode(`, to size the compatibility constraint on D1/D2.

## Milestones

1. **M1 — `ProduceCode` (D1).** `GeneratorExtensions.cs`, `Producer.cs`.
2. **M2 — `ReportDiagnostics` (D2).** `GeneratorExtensions.cs`, `IDiagnosticReporter.cs`, reporters as needed.
3. **M3 — `ComparerRegistry` honours `[ValueComparer]` (D3).** `ValueComparer.Registry.cs`.
4. **M4 — per-generator fixes (D4)**, including `IncrementalGenerator`'s language-version provider.
5. **M5 — tests (D5).**
6. **M6 — ship.** Version bumps (Tools minor: new API + obsolete; each generator package that bundles Tools
   gets a patch), `MintPlayer.SourceGenerators.Tools/README.md` note on `ProduceCode` / `[ValueComparer]`.

Per the repo's working rules: one pull request for everything; test suites run once, after all milestones.

## Consumers in other repositories

`MintPlayer.AspNetCore.Tools` consumes the Tools package. Its in-progress generator should register through
`context.ProduceCode(...)` like every generator here, once the version from M6 is published. That is the only
ordering constraint: this PR lands and publishes first.

## Spike results

*(filled in as the spikes run)*
