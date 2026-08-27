# PRD — MintPlayer.Assertions

A modern, open-source fluent assertion library for .NET, born from the FluentAssertions v8
license change (January 2025, Xceed partnership — v8+ requires a paid license for commercial
use). Goal: not a clone, but a next-generation library that fixes FluentAssertions' structural
weaknesses using the source-generator infrastructure this repository already owns.

**License: Apache-2.0, with an explicit pledge in the README that the license will never
change.** After the FA episode, this is table stakes in this space.

## 1. Why build this (and why here)

### The market gap

| Player | Niche | What they don't do |
|---|---|---|
| FluentAssertions v8+ (Xceed) | The incumbent, commercial | Free commercial use |
| AwesomeAssertions | Drop-in Apache-2.0 fork of FA v7 | Deliberately did NOT rethink the architecture — carries the reflection-heavy design forward |
| Shouldly | Terse `x.ShouldBe(y)` | No rich object-graph equivalency; call-site capture relies on PDB tricks |
| TUnit.Assertions | Async-first, AOT-ready, `[GenerateAssertion]` | `await` on every assertion is polarizing; equivalency options far shallower than FA |
| xUnit v3 `Assert.Equivalent` | Built-in, shallow | No options, weak diffs |

**No library today ships compile-time source-generated object-graph equivalency.** That is the
open niche: trim/AOT-safe, reflection-free, dramatically faster than FA's runtime walker, with
member mismatches surfacing at build time and refactor-safe member exclusion. This repo is
unusually well positioned to build it:

- `SourceGenerators/MintPlayer.SourceGenerators.Tools` — mature incremental-generator base
  (`IncrementalGenerator`, `Producer`, `IndentedTextWriter` emission) and shared MSBuild shape
  (`SourceGenerators/eng/sourcegenerator.targets`).
- `SourceGenerators/ValueComparerGenerator` + the `ValueComparer<T>`/`ComparerRegistry`
  runtime in the Tools package — a working pluggable structural-equality engine to grow the
  equivalency core from.
- Existing analyzer + code-fix precedent (`InterfaceImplementationAnalyzer`,
  `UnusedUsingsAnalyzer`) for shipping analyzers first-class.

### FA pain points this library fixes by design

1. **`BeEquivalentTo` performance** — FA's reflection walker, O(n²) unordered-collection
   matching, repeated perf regressions. Fix: generated per-type comparers.
2. **Trimming/AOT hostility** — FA uses unconstrained reflection and calling-assembly tricks;
   unusable under Native AOT (now real: xUnit v3 AOT mode, TUnit, MTP). Fix: zero reflection
   on the hot path; annotated reflection fallback only where a generator cannot run.
3. **Stringly-typed exclusions** — `Excluding(ctx => ctx.Path == "Items[0].Name")` is
   refactor-hostile. Fix: expression/selector-based exclusion that the generator resolves at
   compile time, including into nested collections.
4. **Silent-pass hazards** — forgotten `await` on async assertions, vacuous assertions. Fix:
   analyzers in the box (error severity for un-awaited assertion, warning for vacuous ones).
5. **Test-framework detection by reflection** — FA late-binds to xUnit/NUnit/MSTest to pick an
   exception type. Fix: throw our own `AssertionFailedException`; every framework renders it
   fine. Optional thin adapter packages only if a framework needs special treatment.
6. **Extensibility churn** (FA v7→v8 `AssertionChain` break stranded extension authors). Fix:
   a small, documented, stable extension surface from day one, plus `[GenerateAssertion]`-style
   source-generated custom assertions (proven by TUnit).

### Explicit non-goal: drop-in FA compatibility

AwesomeAssertions already owns "drop-in replacement" — competing there means being a worse
fork. We compete on architecture. Adoption lever instead: a **migration analyzer + code fix**
that rewrites the most common FluentAssertions/AwesomeAssertions call shapes to
MintPlayer.Assertions equivalents.

## 2. API shape

Fluent `Should()` style — familiar to the largest audience, synchronous by default (no
mandatory `await` like TUnit), terse where possible.

```csharp
using MintPlayer.Assertions;

order.Total.Should().Be(120m, because: "the order was paid");
name.Should().NotBeNullOrWhiteSpace();

items.Should().HaveCount(3)
     .And.ContainSingle(i => i.IsPrimary)
     .Which.Name.Should().Be("main");

// Object-graph equivalency — source-generated, reflection-free
actualDto.Should().BeEquivalentTo(expectedDto, opt => opt
    .Excluding(x => x.Id)
    .ExcludingNested((Item i) => i.CreatedOn)     // reaches into collections, refactor-safe
    .Using<DateTime>((a, e) => a.Should().BeCloseTo(e, TimeSpan.FromSeconds(1)))
    .WithStrictOrdering());

// Exceptions
var act = () => service.Process(null!);
act.Should().Throw<ArgumentNullException>().WithParameterName("order");
await asyncAct.Should().ThrowAsync<TimeoutException>().WithMessage("*timed out*");

// Soft assertions
using (new AssertionScope("the response"))
{
    response.Status.Should().Be(200);
    response.Body.Should().NotBeEmpty();
} // one combined failure, all collected differences
```

Failure messages use `[CallerArgumentExpression]` (zero-cost, no PDB tricks):

```
Expected order.Total to be 120m because the order was paid, but found 90m.
```

Equivalency failures render a per-member difference report with a unified-diff-style rendering
for strings and collections (truncated, configurable).

### Equivalency: how the generated path works

`BeEquivalentTo` is generic; the source generator specializes it **without interceptors**
(decision: interceptors are the newest, most version-sensitive part of the toolchain and are
not needed — a registry gives the same reflection-free result with none of the fragility):

- The generator scans the test assembly's compilation for `BeEquivalentTo` call sites (and
  for types marked `[AssertEquivalency]` for the explicit opt-in path) and emits, per subject
  type, a **member-accessor map** (typed getter delegates, member names, no reflection) plus
  a structural comparer — member-by-member, recursive, collection-aware, cycle-safe.
- A generated `[ModuleInitializer]` registers these into `EquivalencyRegistry` at load time;
  `BeEquivalentTo` consults the registry first. Options (`Excluding`, `Using`,
  `WithStrictOrdering`, …) are applied at runtime over the generated accessors — still zero
  reflection.
- If no generated map exists for a type (non-C#, generators disabled, exotic shape), a
  `[RequiresUnreferencedCode]`-annotated reflection fallback keeps behavior identical; the
  library works without the generator, it is *faster and AOT-safe* with it.

### Assertion categories (v1 surface)

- Scalars: `Be/NotBe`, `BeNull/NotBeNull`, `BeTrue/BeFalse`, `BeSameAs`
- Numerics via generic math (`INumber<T>`, one implementation, no overload explosion):
  `BeGreaterThan`, `BeInRange`, `BeCloseTo`, `BePositive/Negative`
- Strings: `StartWith`, `EndWith`, `Contain`, `Match` (wildcard + regex), `BeEmpty`,
  `NotBeNullOrWhiteSpace`, with diff rendering on failure
- Collections (incl. `Span<T>`/`ReadOnlySpan<T>`): `HaveCount`, `Contain`, `ContainSingle`,
  `OnlyContain`, `Equal` (ordered), `BeEquivalentTo` (unordered structural),
  `BeInAscendingOrder(selector)`, `AllSatisfy`, `SatisfyRespectively`, `BeSubsetOf`
- Dictionaries: `ContainKey`, `ContainValue`, `Contain(pair)`
- Dates/times: `BeCloseTo`, `BeAfter/Before`, `HaveYear` etc.; `DateOnly`/`TimeOnly` included
- Exceptions: `Throw<T>`/`NotThrow` (+ `WithMessage` wildcard, `WithInnerException<T>`,
  `WithParameterName`), `ThrowAsync<T>`/`NotThrowAsync`
- Tasks: `CompleteWithinAsync`
- Object graphs: `BeEquivalentTo` with the options above
- `AssertionScope` soft assertions with nesting and context naming
- Extensibility: `AndConstraint<T>`/`AndWhichConstraint<T, TWhich>`, a stable `Assertion`
  builder (`ForCondition/BecauseOf/FailWith`), and `[GenerateAssertion]` for user one-liners

- Event monitoring: `using var monitor = subject.Monitor();` then
  `monitor.Should().Raise(nameof(X.Changed)).WithSender(subject).WithArgs<TArgs>(e => ...)`,
  including INotifyPropertyChanged helpers (`RaisePropertyChangeFor(x => x.Name)`)
- Execution time: `act.Should().ExecutionTime().BeLessThan(TimeSpan.FromMilliseconds(500))`
- JSON (System.Text.Json): `JsonElement`/`JsonNode` assertions — `BeJsonEquivalentTo`,
  `HaveProperty`, value/type assertions on properties
- Guids, enums (`HaveFlag`), `Nullable<T>` (`HaveValue`), comparables, streams
- Types/reflection assertions (`BeAssignableTo<T>`, `BeDecoratedWith<TAttribute>`)

Everything above ships in v1. There is no deferred tier.

## 3. Packages & repo layout

Following repo conventions (folder per family, `MintPlayer.*` ids, xUnit tests, per-project
metadata block, Apache-2.0, snupkg):

```
Assertions/
  prd/Assertions-prd.md                       (this file)
  prd/Assertions-plan.md
  MintPlayer.Assertions/                      net8.0;net9.0;net10.0 — core runtime library
  MintPlayer.Assertions.SourceGenerator/      netstandard2.0, imports SourceGenerators/eng/sourcegenerator.targets
                                              equivalency generator + [GenerateAssertion] + interceptors
  MintPlayer.Assertions.Analyzers/            netstandard2.0 — un-awaited/vacuous-assertion analyzers,
                                              FA→MintPlayer migration code fix
  MintPlayer.Assertions.Tests/                xUnit, net10.0 — also the first consumer (self-hosted:
                                              the library's own tests use the library)
```

The core package references the generator + analyzers as analyzer assets so consumers get the
whole experience from a single `PackageReference`. Multi-targeting `net8.0+` matches
`MintPlayer.Http` precedent and keeps generic math / `[CallerArgumentExpression]` / C# 12+
available everywhere.

Dogfooding: after v1 stabilizes, migrate this repo's own test projects (`SlnLaunch.Tests`,
`TokenReplacer.Tests`, `FolderHasher.Tests`, `Mapping.Tests`) — in the same PR, per repo
policy — as the acceptance test of real-world ergonomics.

## 4. Success criteria

1. **AOT/trim-clean**: the core package compiles with `IsAotCompatible=true`, zero trim
   warnings; an AOT test app runs equivalency assertions under Native AOT.
2. **Faster than FA**: BenchmarkDotNet suite shows generated `BeEquivalentTo` ≥5× faster than
   FluentAssertions 7/AwesomeAssertions on a representative DTO graph (goal, validated in
   Spike 1; revise if the spike says otherwise).
3. **Message quality**: failure messages include the caller expression, the `because` reason,
   and a member-level diff — at least at parity with FA, better for strings/collections.
4. **Safety**: forgetting `await` on an async assertion is a build **error** (analyzer);
   common vacuous assertions warn.
5. **Migration**: the code fix converts ≥ the top-20 FA call shapes automatically.
6. **This repo's own tests** run on it (dogfooding complete).

## 5. Risks & open questions

- **Scope**: v1 deliberately ships the full §2 category list in a single pull request
  (repo policy: one PR, tests batched at the end).
- **Generator applicability**: consumers on VB.NET or with generators disabled get the
  reflection fallback — correctness identical, performance/AOT benefits lost. Acceptable.
- **Naming**: resolved — the package id is `MintPlayer.Assertions`.
