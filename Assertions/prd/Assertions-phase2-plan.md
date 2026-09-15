# Plan — MintPlayer.Assertions Phase 2

Companion to [Assertions-phase2-prd.md](./Assertions-phase2-prd.md). Repo policy applies: **one pull
request**, milestones are commit boundaries inside it, and the **test suite runs once, after the last
milestone** — intermediate milestones are verified by reading the code and type-checking.

Two standing constraints, from the PRD:

- **No performance loss.** Nothing may be added to the passing path. Milestone 0 exists to make that
  enforceable before any feature lands.
- **No backward compatibility required.** Versions are bumped, so renames and `Subject` shape changes
  are on the table where they produce the better API.

---

## Milestone 0 — Make the boundary enforceable ✅ (partly; see PRD §0)

**This goes first. Every later milestone is measured against what it establishes.** Today the only
benchmark covers `BeEquivalentTo`, and it is explicitly not gating
(`Assertions-plan.md:85`) — so a change that added an allocation to every passing assertion in the
library would be caught by nothing.

1. Promote the equivalency benchmark to a gate: a regression beyond noise fails the work. Keep the
   existing fairness check in `Benchmarks/Program.cs:15-52` (it asserts the registry is populated, so
   the benchmark cannot silently measure the reflection fallback).
2. Add a **per-assertion micro-benchmark** suite covering the *passing* path: one representative
   assertion each from string, numeric, collection, exception and date families.
3. Add an **allocation test** on the passing path — run N passing assertions, assert zero (or a
   pinned constant) allocations. A regression then fails a test with a name rather than moving a
   number in a report nobody reads.
4. Record the current numbers as the baseline in this file.

Exit: a passing-path regression is a build failure, not a judgement call.

## Milestone 1 — Correctness defects (PRD §2) ✅

Bugs before surface. Wrong answers matter more than missing methods.

1. **`AggregateException` extraction.** Adopt FA's shape: `Subject` becomes
   `IEnumerable<TException>`, `Which` stays as first-match sugar. Applies to the sync paths
   (`Specialized/ActionAssertions.cs:74`, `FuncAssertions.cs:73`) and their async counterparts.
2. **`ExecutionTime` no longer hangs.** Keep a cheap inline path for actions that complete promptly;
   escalate to a bounded/polled measurement only when there is a limit to exceed. Do **not**
   blanket-adopt FA's `Task.Run` + polling — it is more expensive for the common fast case. Also
   measure **once** into a value that chained assertions read, instead of re-running the action per
   assertion.
3. **`FailWith` stops allocating on the passing path.** Add lazy overloads
   (`Func<object>[]`-style) so the `object?[]` and its boxing are not paid by passing assertions.
   This is a performance *gain* and the item most likely to be invisible without Milestone 0.
4. **`CompleteWithinAsync` timer leak** — add a `CancellationTokenSource` so the `Task.Delay` does not
   survive the success path.
5. **Delete `Assertions/MintPlayer.Assertions.Analyzers`** (empty directory; analyzers live in
   `MintPlayer.Assertions.SourceGenerator/Diagnostics/`), and fix the stale claim at
   `MintPlayer.Assertions.SourceGenerator/README.md:7-9` that no analyzer test infrastructure exists.

## Milestone 2 — Renames (PRD §8) ✅

Do these early, in one commit, before new surface is written against the old names.

`BeCloseTo`/`NotBeCloseTo` → `BeApproximately`/`NotBeApproximately` **on numerics only** (temporal
types keep `BeCloseTo`, matching FA and resolving the internal collision); `WithInnerExactly<T>` →
`WithInnerExceptionExactly<T>`; `HaveSameCountAs`/`NotHaveSameCountAs` → `HaveSameCount`/
`NotHaveSameCount` plus the generic `IEnumerable<TExpectation>` form; reorder `WithArgs` parameters.

Then: update MPA0100's rename table for anything deliberately left divergent, and list those in the
README so a migrating user never meets an unexplained compile error.

## Milestone 3 — Free wins (PRD §3) ⚠️ mostly done — three items NOT built, see Status

Pure additions, no mechanism, no allocation. Largest surface-per-cost in the project.

- `BeNull`/`NotBeNull` on every nullable value-type family (returning `AndWhichConstraint` so the
  unwrapped value chains).
- The sixteen temporal negatives: `NotBeBefore`/`NotBeOnOrBefore`/`NotBeAfter`/`NotBeOnOrAfter` ×
  DateTime, DateTimeOffset, DateOnly, TimeOnly.
- `BeOneOf` on strings, comparables, objects, TimeOnly — `foreach` with early exit, **not** LINQ
  `Contains`.
- `DateTimeOffset.BeExactly`/`NotBeExactly` (offset-sensitive equality; currently unexpressible).
- `HaveMillisecond`/`NotHaveMillisecond` on DateTime and DateTimeOffset.
- `NotBeTrue`/`NotBeFalse` on nullable bool; `Imply`.
- `BeRankedEquallyTo`/`NotBeRankedEquallyTo` on comparables (the `CompareTo() == 0` vs `Equals()`
  distinction that `ComparableAssertions.Be` currently conflates).
- `Match` predicate on numerics, enums, nullables — `Func<>` + `[CallerArgumentExpression]`, never
  `Expression<>`.
- Exceptions: non-generic `Throw()`/`ThrowAsync()`, `NotThrow<TException>`, sync `NotThrowAfter`,
  `WithoutMessage`, runtime-`Type` inner-exception overloads, `And` alias.
- Async: `ThrowWithinAsync`, `NotCompleteWithinAsync`, `NotThrowAfterAsync` on the generic variant,
  the `TaskCompletionSource` family, and **all `ValueTask` support**.
- Collections: `Contain`/`NotContain(IEnumerable<T>)`, `BeSupersetOf`, proper subset/superset,
  `HaveElementAt`/`Preceding`/`Succeeding`, key-selector overloads on `OnlyHaveUniqueItems` and
  `NotContainNulls`, comparer-lambda overloads on `Equal`/`StartWith`/`EndWith`,
  `ContainItemsAssignableTo`.
- Dictionaries: `Equal`/`NotEqual`, bulk `Contain`/`NotContain` overloads, the missing count family.

## Milestone 4 — Cheap-if-deliberate (PRD §4) ⚠️ mostly done — one item NOT built, see Status

Each has a naive implementation that breaches the boundary. Use the named mechanism.

- `HaveLineCount`/`NotHaveLineCount`/`ContainLine`/`NotContainLine` via
  `MemoryExtensions.EnumerateLines()` + `SequenceEqual` — never FA's allocating `SplitLines`.
- `OccurrenceConstraint` + `AtLeast`/`AtMost`/`Exactly`/`MoreThan`/`LessThan`, wired into
  `Contain`/`MatchRegex`/`ContainEquivalentOf`. Span `IndexOf` loop for literals; `Regex.Count` for
  regex — never `Matches(...).Count`.
- `MatchRegex(Regex)`/`NotMatchRegex(Regex)` overloads. This is a **fix**: today's static
  `Regex.IsMatch` hits the framework cache capped at 15 patterns and re-compiles past it.
- String `config` overloads as a `StringComparison`/flags parameter — not FA's per-call options
  object plus closure.
- Replace the greedy first-fit unordered-collection match with a proper bipartite match, removing the
  throwaway `List<Difference>` per probe (`EquivalencyValidator.cs:263-281`; a failing 20×20
  comparison allocates ~400 lists).


---

## Status — where this stands

**Done: M0, M1, M2. Mostly done: M3, M4.** 965 assertion tests pass, full Release build clean,
MPA0005 reports zero across the solution. Every milestone was verified by a full-solution Release
build and the complete assertions suite before pushing, after a single-project build once let a
rename break a consumer in another project.

**Remaining: the six unbuilt items below, then M5 (equivalency options — the largest), M6, M7, M8.**

### Planned but NOT built — verified absent from the source

Checked by grep, not by memory. These are listed in the milestone bodies above as if done; they are
not:

| Item | Milestone | Note |
|---|---|---|
| `TaskCompletionSource` / `TaskCompletionSource<T>` assertions | M3 async | Whole family absent. No `Should()` overload for either type |
| `BeProperSubsetOf` / `BeProperSupersetOf` | M3 collections | `BeSubsetOf` and `BeSupersetOf` exist; the *proper* (strict) variants do not |
| Comparer-lambda overloads on `Equal` / `StartWith` / `EndWith` | M3 collections | The `Func<T, TExpectation, bool>` forms that let two differently-typed sequences be compared |
| String `config` overloads (`IgnoringCase`, whitespace, newline style) | M4 | Intended as a `StringComparison`/flags parameter rather than FA's per-call options object |

None is blocked; all four are ordinary additions. They were simply missed, and the milestones were
marked complete before this was checked.

### What was done differently from the plan above

Recorded because the plan text still reads as originally written, and the reasoning matters more
than the instruction:

1. **`AggregateException` keeps a single `Subject`** (PRD §2.1), not FA's `IEnumerable<TException>`.
   The collection costs an allocation on the passing path of every exception assertion; §0 outranks
   FA-shape-matching.
2. **Bulk membership is `ContainAll` / `NotContainAny`**, not more `Contain` / `NotContain`
   overloads (PRD §8). FA's shape lets a `params` bulk overload silently hijack single-item calls.
   `StringAssertions` had already set the naming convention in this library.
3. **The generic `HaveSameCount<TExpectation>` overload was dropped** — the non-generic `IEnumerable`
   form already binds every typed collection.
4. **The bipartite matcher was a correctness fix, not a performance one** (PRD §4). Greedy first-fit
   could report a difference between equivalent collections depending on item order.
5. **MPA0005 was added** (PRD §12) — not foreseen when the plan was written. It also exposed that the
   library was not running its own analyzers at all.
6. **A data-driven allocation sweep was built and removed** in favour of the analyzer (PRD §0).
7. **`net8.0` and `net9.0` were dropped** from all six multi-targeting projects in the repo —
   Assertions, its tests, Http, and the three Verz projects. Both reach end of support shortly, and
   carrying them constrains which BCL APIs the library may use. ⚠️ This narrows the shipped surface
   of packages beyond Assertions; revisit if Http/Verz need wider reach.
8. **`ExecutionTime`'s deadline grace is 1 second, not 50ms.** 50ms left `BeLessThan(Zero)` against a
   20ms action too little room for a thread-pool hop, so a *completing* action was intermittently
   reported as never finishing. The grace exists to turn a hang into a failure, not to measure.

### Known gaps left open, deliberately

- **The equivalency benchmark is still not gated.** A pure time regression with no allocation change
  would pass unnoticed.
- **The baseline table below is unfilled.** The benchmarks were never run to completion in this
  environment; the v1 figures are carried forward as the reference point, not re-measured.
- **PRD §13's open questions are all still open** — test-framework exceptions, whether to build the
  Types/Assembly/selector family and whether to spike the generated variant, XML scope, permanently
  rejecting `IEquivalencyStep`, and the `monitor.Raise` shape. M6 and M7 depend on answers.

---

## Milestone 5 — Equivalency options (PRD §6.1) ⏳

The largest single gap: 12 options against FA's ~60, and the v1 PRD claims no deferred tier. Close it
to the compile-time-decidable boundary.

Requires extending `MemberAccessor` with flags the scanner already knows but does not emit
(`EquivalencyScanner.cs:111-126`) — accessibility, `[EditorBrowsable]`, explicit-interface, is-enum,
is-record:

- Field/property/internal/non-browsable/explicit-interface inclusion toggles.
- `ExcludingMembersNamed`, `Excluding<TMember>()`/`Excluding(Type)`.
- `WithMapping` (subject↔expectation name mapping).
- `Including` at arbitrary depth (today root-only, `EquivalencyValidator.cs:163`).
- `ExcludingMissingMembers`/`ThrowingOnMissingMembers`.
- `ComparingEnumsByName`/`ByValue`; `ComparingRecordsByValue`/`ByMembers`.
- `WithStrictOrderingFor`/`WithoutStrictOrderingFor` by path.
- String/null tuning; `Using<T>(IEqualityComparer<T>)`.

Also revisit §6.3: the depth cut currently treats deeper nodes as **silently equal**
(`EquivalencyValidator.cs:116`) — a silent pass is the worst failure mode an assertion library has.
Add a `ThrowException` cycle mode and generic `IDictionary<K,V>` handling.

Then document §6.2 — runtime types not visible to the compilation, anonymous types, open generics,
plug-in steps — in the README as the **deliberate ceiling** of a generator-first design, so it stops
reading as unfinished work.

## Milestone 6 — Failure-path features (PRD §5) ⏳

Free by construction: reached only after an assertion has already failed.

- A pluggable formatter seam (global + scope-scoped). **Reject** FA's `[ValueFormatter]` assembly
  scan — it is exactly what the AOT posture rules out.
- Configurable `MaxDepth`/`MaxLines`/`UseLineBreaks`, with an actionable depth-exceeded message
  naming the knob.
- Multi-line indented graph rendering (today a single flat line, so large diffs are unreadable).
- `AssertionScope` inspection: `Discard()`, `AddPreFormattedFailure`, reportables — the thing that
  currently blocks a `BeEquivalentTo`-style full-diff block.
- Native test-framework exceptions **only if open question 1 is answered yes**.

## Milestone 7 — Scope-gated families (PRD §7, §9) ⏳

Only after open questions 2, 3 and 5 are answered. Nothing here may touch `Assertion`,
`AssertionScope`, `Formatter` or the equivalency path.

- `Stream` / `BufferedStream` assertions — reflection-free, purely additive, cheapest item here.
- Types/MemberInfo/Assembly/selector family — hot-path reflection by nature, admissible only because
  it lives on assertion types nobody else touches. Annotate trimming/AOT honestly, following the
  `[RequiresDynamicCode]` precedent set by `EventMonitor`. Spike the source-generated variant first.
- XML, if in scope.
- Event gaps that do **not** need `Reflection.Emit`: options overload, `MonitoredEvents`,
  `GetRecordingFor`, multi-predicate `WithArgs`, interface-declared events, weak subject reference,
  no-events guard. `Reflection.Emit` itself is rejected — it costs AOT.

## Milestone 8 — Verify ⏳

First and only full test run (repo policy). Then:

1. Full solution build, 0 errors.
2. Full `dotnet test` sweep.
3. Milestone 0's gates: equivalency benchmark within noise of baseline; per-assertion
   micro-benchmarks within noise; allocation test passing.
4. `IsAotCompatible=true` with **zero** trim warnings, and every new `[RequiresDynamicCode]` /
   `[RequiresUnreferencedCode]` annotation checked for the *exact* diagnostic id — the v1 plan records
   two suppressions citing the wrong id, which meant the AOT-clean claim was not actually being
   enforced (`Assertions-plan.md:115-116`).
5. Pack and confirm the analyzer payload still lands in every `analyzers/dotnet/roslyn<N.N>/cs`
   folder alongside its dependencies — see `PackagingTests`, and note that the packaging suite reads
   a real freshly-packed nupkg only because `PackedFeed` now evicts its constant version from the
   global packages folder.
6. README updated: the §6.2 ceiling, the deliberate divergences from §8, and the new options surface.

---

## Baseline

⚠️ Not re-measured. The v1 equivalency figures are carried forward as the reference point; the
per-assertion rows were never run to completion in this environment. The deterministic guard is
`PassingPathAllocationTests`, not these numbers.

| benchmark | time | allocated |
|---|---|---|
| `BeEquivalentTo` (5-type / 4-level / 20-item graph) | 13.08 µs | 20.34 KB |
| *(FluentAssertions 7.2.2, same graph)* | *201.08 µs* | *409.14 KB* |
| per-assertion passing path — string | tbd | tbd |
| per-assertion passing path — numeric | tbd | tbd |
| per-assertion passing path — collection | tbd | tbd |
| per-assertion passing path — exception | tbd | tbd |
| per-assertion passing path — date | tbd | tbd |
