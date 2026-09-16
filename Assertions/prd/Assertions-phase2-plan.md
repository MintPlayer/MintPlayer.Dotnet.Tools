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

**Done: M0–M8.** Every milestone was verified by a full-solution Release build and the complete
assertions suite before pushing, after a single-project build once let a rename break a consumer in
another project.

The four items listed below as unbuilt were subsequently built; the section is kept because *how they
came to be marked done while absent* is the part worth remembering.

### After M4: the subject is iterated as a span

Not a milestone — a correction to work already done, prompted by actually measuring it.

MPA0005's original guidance ("index the interface instead") was wrong on time, and the 27 converted
sites were converted that way. A standalone benchmark showed the indexed loop ran **1.4× slower** than
the boxed `foreach` it replaced, while a span ran ~2× faster for a `List<T>` subject and ~7× faster
for an array — all three allocation-free. Full table in PRD §12.1.

`Items` and `Pairs` now return `ReadOnlySpan<T>`; `Spans.From` does the conversion. Null checks moved
from the materialised collection to `Subject`, because a span cannot represent null. MPA0005's message
now points at spans and says explicitly not to settle for indexing.

The **expectation** side stays a list (`Spans.ListFrom`) and that is deliberate: a ref struct cannot
reach `FailWith`'s `object?` parameters or be captured by a lambda, and those sequences are rendered
in messages. Trying it produced 52 errors of exactly those two kinds.

Worth carrying forward: both times a bulk regex was used on these files it introduced a semantic bug
the compiler could not see — once renaming a local that held a *value* rather than a collection, so a
dictionary failure stopped reporting what was actually found. The test suite caught both. Prefer
compiler- or analyzer-verified transformations over pattern rewrites here.

### Planned but NOT built — verified absent from the source *(since built)*

Checked by grep, not by memory. These were listed in the milestone bodies above as if done and were
not. All four have since been built — the table stays as the record of the discrepancy, not as
outstanding work:

| Item | Milestone | Note |
|---|---|---|
| `TaskCompletionSource` / `TaskCompletionSource<T>` assertions | M3 async | Whole family absent. No `Should()` overload for either type |
| `BeProperSubsetOf` / `BeProperSupersetOf` | M3 collections | `BeSubsetOf` and `BeSupersetOf` exist; the *proper* (strict) variants do not |
| Comparer-lambda overloads on `Equal` / `StartWith` / `EndWith` | M3 collections | The `Func<T, TExpectation, bool>` forms that let two differently-typed sequences be compared |
| String `config` overloads (`IgnoringCase`, whitespace, newline style) | M4 | Intended as a `StringComparison`/flags parameter rather than FA's per-call options object |

None was blocked; all four were ordinary additions. They were simply missed, and the milestones were
marked complete before this was checked. The lesson generalises: **a milestone is done when a grep
says the symbols exist, not when the work feels finished.**

Two of the four needed a design decision rather than just typing:

- The **comparer-lambda overloads** carry their own expectation type parameter
  (`Equal<TExpectation>(IEnumerable<TExpectation>, Func<T, TExpectation, bool>)`), which is the whole
  point — two differently shaped sequences compare without projecting one into the other first, which
  would allocate a whole sequence just to compare it. There is a test asserting the ordinal overloads
  still bind, because a second parameter of a different type is the only thing keeping them apart.
- The **string options** are a `[Flags]` enum, not FluentAssertions' per-call options object. FA's
  shape allocates a builder and runs a delegate on the passing path of every call that uses it; a
  flags enum is a compile-time constant at the call site. Only `IgnoringCase` is genuinely free (it
  maps onto `OrdinalIgnoreCase`); the rest rewrite both sides and allocate, which is inherent to the
  question rather than an implementation choice, and is documented as such.

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
  would pass unnoticed. This is the one M0 item that never landed, and it is the gap most worth
  closing next: M5 added real work to the walker (the trait filter runs per member, the inclusion
  matcher per node), and while every option is emptiness-checked before it is consulted, "checked and
  found empty" is still a branch that `PassingPathAllocationTests` cannot see.
- **The baseline table below is unfilled.** The benchmarks were never run to completion in this
  environment; the v1 figures are carried forward as the reference point, not re-measured.
- **Two delivered PRDs still described the old `roslyn4.0` folder layout.** `docs/PRD-INTF001-…`
  now carries a footnote pointing at `SourceGenerators/eng/roslyn.props` instead; the body text is
  left as written, because a delivered PRD is a record of what was done and silently rewriting it
  would destroy that. `docs/PRD-TestCoverage-Phase2.md` says `roslyn4.x`, which is generic enough to
  still be true.
- **PRD §13's open questions are all answered** — see the PRD, where each answer carries its
  reasoning. In short: a registration seam rather than test-framework detection; the
  Types/Assembly/selector family built on runtime reflection because a generated variant is not
  possible even in principle; XML in scope; `IEquivalencyStep` rejected permanently and documented as
  a boundary; `monitor.Should().Raise(...)` accepted as an alias of `monitor.Raise(...)`.
- **The assertions suite no longer runs in parallel.** Phase 2 added process-wide configuration
  (`Formatter.Options`, the global formatter registry, `AssertionConfiguration.ExceptionFactory`), and
  a handful of tests necessarily mutate it and put it back. Under xUnit's default parallelism those
  ran beside every other test, so an unrelated assertion failed with whatever renderer or exception
  type the mutating test had installed at that instant — which is not a flake to retry, it is two
  tests sharing one process-wide setting. The alternative was making the configuration async-local,
  which would defeat its purpose (a failing assertion usually has no scope). The suite runs in a few
  seconds, so serialising it costs almost nothing.

---

## Milestone 5 — Equivalency options (PRD §6.1) ✅

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

### What M5 actually did

All of the above, plus three things the plan did not foresee:

1. **The reflection fallback had to change too.** The plan only mentions the scanner, but a trait the
   generator emits and the reflection provider does not is worse than no trait at all: the same option
   would mean two different things depending on whether a type happened to be scanned. The provider
   now collects public *and* internal members and explicit interface properties, and deliberately
   **not** `private`/`protected` — which reflection could reach and generated code cannot. That
   asymmetry is the reason `IncludingInternalMembers` is one option rather than FA's
   `IncludingInternalFields` + `IncludingInternalProperties`.
2. **The generic-dictionary fix was a second silent pass, not a nicety.** A type implementing only
   `IReadOnlyDictionary<K,V>` — frozen, immutable, hand-rolled — fell through to the *collection*
   path, so a wrong value under a matching key was reported as "no equivalent item was found". The
   non-generic `IDictionary` path was **kept** rather than folded into the new one, and that is
   load-bearing: `IDictionary.Contains` goes through the dictionary's own key comparer, so a
   `Dictionary<string, T>(OrdinalIgnoreCase)` still matches keys the way it does everywhere else.
3. **The hash-based multiset shortcut had to be guarded.** Any option that changes what "equal" means
   for a value — string options, enum-by-name, null-as-empty — also changes what belongs in the same
   hash bucket. Those comparisons fall back to a pairwise match.

Three existing tests asserted the old silent-pass behaviour *by name*
(`WithMaxDepth_TreatsDeeperNodesAsEqual`), and one asserted that the generator emits no internal
members. Both were the old contract, and both were updated rather than worked around.

## Milestone 6 — Failure-path features (PRD §5) ✅

Free by construction: reached only after an assertion has already failed.

- A pluggable formatter seam (global + scope-scoped). **Reject** FA's `[ValueFormatter]` assembly
  scan — it is exactly what the AOT posture rules out.
- Configurable `MaxDepth`/`MaxLines`/`UseLineBreaks`, with an actionable depth-exceeded message
  naming the knob.
- Multi-line indented graph rendering (today a single flat line, so large diffs are unreadable).
- `AssertionScope` inspection: `Discard()`, `AddPreFormattedFailure`, reportables — the thing that
  currently blocks a `BeEquivalentTo`-style full-diff block.
- Native test-framework exceptions **only if open question 1 is answered yes**.

### What M6 actually did

All of it. `IValueFormatter` with scope-first-then-global resolution; `FormattingOptions` as a record
so a scope can change one field with `with`; `Formatter.Options` globally and
`AssertionScope.WithFormatting(...)` per block; `MaxDepth` / `MaxStringLength` /
`MaxEnumerableItems` / `MaxLines` / `UseLineBreaks`; `Discard()`, `AddPreFormattedFailure`, eager and
lazy reportables. The depth elision now reads
`Name {… depth 3 reached; raise FormattingOptions.MaxDepth}` instead of `Name {…}`, because the depth
limit is the most common reason a message does not show the member that actually differs.

Two things are defensive rather than featureful, and both are the same principle: **the caller is
already looking at a failure, and that is the worst possible moment to lose the explanation.** A
formatter that throws is skipped and the next one tried, falling through to the built-in rendering; a
reportable that throws renders as `<threw …>` beside the failure instead of replacing it. The same
reasoning governs `AssertionConfiguration.BuildException`.

Open question 1 was answered "a registration seam, never detection" — see the PRD. Reportables travel
from a nested scope to its parent, because the outermost scope is where the message is built.

## Milestone 7 — Scope-gated families (PRD §7, §9) ✅

Nothing here touches `Assertion`, `AssertionScope`, `Formatter` or the equivalency path.

- `Stream` / `BufferedStream` assertions — reflection-free, purely additive, cheapest item here.
- Types/MemberInfo/Assembly/selector family — hot-path reflection by nature, admissible only because
  it lives on assertion types nobody else touches. Annotate trimming/AOT honestly, following the
  `[RequiresDynamicCode]` precedent set by `EventMonitor`. Spike the source-generated variant first.
- XML, if in scope.
- Event gaps that do **not** need `Reflection.Emit`: options overload, `MonitoredEvents`,
  `GetRecordingFor`, multi-predicate `WithArgs`, interface-declared events, weak subject reference,
  no-events guard. `Reflection.Emit` itself is rejected — it costs AOT.

### What M7 actually did

All four, including XML (open question 3: in scope). The spike in bullet two resolved negatively and
that is worth keeping: a source-generated Types/Assembly family is not possible even in principle,
because a generator needs a compile-time target and this family's whole point is a `Type` chosen at
run time, usually from an assembly scan.

Additions the plan did not list, each because writing the family exposed the need:

- **`TypeSelectorAssertions.NotBeEmpty()`.** Every rule over an empty set holds vacuously, so a
  selector whose filter stopped matching passes the whole suite while checking nothing. The
  equivalency engine's vacuity guard, one level up.
- **Every selector assertion names *all* the offenders**, not the first. That is the difference
  between a rule fixed in one pass and a rule fixed one recompile at a time, and it costs nothing —
  the list is built only once the assertion has already failed.
- **`HaveMethod` requires the parameter types.** `GetMethod(name)` throws `AmbiguousMatchException`
  for an overloaded method, which turns an assertion into a crash.
- **Stream assertions check `CanSeek` before reading `Length`/`Position`**, which throw
  `NotSupportedException` on a network stream or a pipe. An assertion that lets that escape reports a
  crash where the honest answer is a failure with a reason.
- **`XElement.HaveAttributeWithValue` is spelled out**, not a second `HaveAttribute` overload.
  `HaveAttribute(name, value)` and `HaveAttribute(name, because)` have identical parameter types; the
  compiler picked one and the test got the other. **This is the third time in Phase 2 that a trailing
  `because` parameter silently absorbed a meaningful argument** — after `Contain`/`ContainAll` and the
  `WithArgs` reordering. A distinct name is the only spelling that cannot be misread.

Two suppressions were written with the wrong diagnostic id (`IL2070` where the build wanted `IL2090`
and `IL2075`) and the build caught it — which is exactly the failure M8 item 4 exists to prevent, and
evidence that checking ids against a build rather than guessing them is not a formality.

## Milestone 8 — Verify ✅

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

### M8 results

| Gate | Result |
|---|---|
| Full solution Release build | ✅ 0 errors |
| Full `dotnet test` sweep | ✅ every project green |
| Assertions suite, `net10.0` and `net11.0` | ✅ 1075 tests each |
| Trim warnings from `MintPlayer.Assertions` | ✅ zero; the solution's other IL warnings are pre-existing and in other projects |
| New `[RequiresUnreferencedCode]` / suppression ids | ✅ checked against the build, not guessed — two were wrong (item 4's exact failure) and the build caught them |
| `PassingPathAllocationTests` | ✅ passing; the analyzer (MPA0005) reports zero |
| Packaging | ✅ `PackagingTests` green in the sweep |
| Equivalency benchmark | ⚠️ still not gated — see the deliberate gaps above |

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
