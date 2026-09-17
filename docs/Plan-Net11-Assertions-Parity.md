# Plan — .NET 11, and FluentAssertions parity without losing the performance

Companion to [PRD-Net11-Assertions-Parity.md](./PRD-Net11-Assertions-Parity.md). Branch
`net11-assertions-parity`. Repo policy: **one pull request**; milestones are commit boundaries inside
it.

> **Before writing code, read [PRD §9 "Traps already paid for"](./PRD-Net11-Assertions-Parity.md#9-traps-already-paid-for).**
> Twelve failure modes that already happened on this codebase, several of which shipped green. The
> checklist near the end of this plan encodes them; §9 explains why each one exists.

Two standing rules:

- **Nothing may be added to the passing path.** M2 exists to make that enforceable before any feature
  lands, and every later milestone is measured against what it establishes.
- **Measured, not argued.** A hot-path change is done when the numbers say so. "It should be fine"
  is how the eager candidate matrix shipped at 75× its intended allocation.

On testing: the repo batches test runs to the end. The exception here is **any milestone that touches
the equivalency walker** — M2, M3, M6 — where the allocation gate is the point of the change and
running it is how the change is verified at all.

---

## STATUS — as of 2026-09-17, branch `net11-assertions-parity`, 9 commits

**Done: M0, M1, M2 (all four layers), M3, M4, M5a, M6, S1, S3, S4. Partial: M5b, M5c. Outstanding: S2 (resolved in favour of keeping the counters).**

991 assertion tests pass on net10.0 and net11.0; 24 test projects across the solution, 0 failures,
Release. The walk allocates **6,808 B/op**, down from 20,340 when this branch started.
891 assertion tests pass on net10.0 and net11.0; full solution builds clean.

### The hard boundary: improved, not merely held

Measured net11-vs-net11 on an idle machine, `Fairness checks passed`:

| | Mean | Allocated | vs FluentAssertions |
|---|---:|---:|---|
| README claimed (net10) | 13.08 µs | 20.34 KB | 15.4× / 20.1× |
| **Now (net11, after M5a)** | **9.58 µs** | **6.59 KB** | **17.5× / 60×** |

Allocation reproduced to the decimal across every run (6.59 KB after M5a; 14.83 before the path work) while
FluentAssertions' did not (404.26 then 397.04 KB) — the byte-exact gate works because nothing on this
library's passing path allocates conditionally, which is a property the code earned rather than a
property of benchmarking. README updated, quoting the less flattering of the two runs whole.

### What each milestone actually did

**M1 ✅** — 71 projects re-targeted, 33 versions → `11.0.0-rc.1`, C# 14 → 15, Dockerfile, CI SDK pins,
`Vidyano.props`' shipped `net9.0`, and `Verz/Program.cs`'s hardcoded `lib/net10.0` replaced with a
probe. One real .NET 11 breaking change: `Microsoft.Extensions.DependencyInjection.Abstractions`
moved into the shared framework, and the generator leaked its v10 copy to consumers as a compile
reference via `TargetPathWithTargetPlatformMoniker` — 120 errors from one line.

**S3 ✅** — `analyzers/dotnet/roslyn4.0|4.9/cs` → a single honest `roslyn5.0/cs`, Roslyn floor lowered
5.3.0 → 5.0.0 (the whole solution compiles against it, and 5.3 excluded .NET 10.0.112 for nothing).
The dead `<Choose>` on `$(RoslynVersion)` removed from `eng/` **and** the shipped
`sourcegenerator_tools.props`; all five shipped `build/*.props` moved together.

**M2 ✅** — `AllocationProbe` + `EquivalencyWalkerGateTests` + `PassingPathAllocationTests` (12 tests)
+ **MPA0005**, and `OutputItemType="Analyzer"` so the library runs its own rules at all. Operation
counters are `[ThreadStatic]` — plain statics were counted across xUnit's parallel test classes and
read 259 nodes where the graph has 133.

**M3 ✅** — passing path **112 → 0 B/op**: arity-specific `FailWith` overloads (no call site changed),
`AndConstraint` → `readonly struct`, `Items`/`Pairs` → `ReadOnlySpan<T>` (which also stopped copying
subjects that were already arrays), boxed enumerators 28 → 6 sites, and twelve unconditional
failure-detail lists analysed individually — nine made lazy, two restructured because lazy would have
been *wrong*, one left alone with a comment.

### Gate thresholds, and why they are where they are

Tightened once every site under them read zero, because a bound nothing currently exceeds is not a
bound — §9.15 slipped a 20% regression past a 100 KB alarm.

| Gate | Was | Now | Measured | Why that number |
|---|---:|---:|---:|---|
| PassingPathAllocationTests.AllowanceBytesPerOp | 16 B | **0 B** | 0 B everywhere | The actual rule. 16 was scaffolding from while sites were still being fixed, and would have hidden a future box. |
| TheDefaultWalkStaysUnderItsMeasuredByteCost | 14,100 B | **6,900 B** | 6,808 B | Under 1.4% headroom — less than one allocation per node on this graph, so it cannot hide a per-node cost. |
| TheWalkStaysFarUnderAReflectionWalkersCost | 100 KB, then 32 KB | **16 KB** | 6,808 B | Kept deliberately loose and kept separate: it answers "is this still the same algorithm", which the tight bound cannot. 2.4× the measurement, 25× under a reflection walk. |
| UnorderedMatchingOfAnAlreadyOrderedCollection… | 3× strict | **1.2×** | 1.11× | 3× would accept a matcher three times more expensive than the algorithm it mirrors. |

**There is no wall-clock gate, and that is not an omission.** Duration is gated by the exact
operation counts — 133 nodes, 112 member lookups, 20 match probes, 0 probes under strict ordering —
which are asserted for equality, not bounded. An exact count cannot be tightened further, and it
survives a loaded CI runner where a millisecond threshold is either flaky or too loose to catch
anything (PRD §2: the same benchmark measured FluentAssertions at 150 µs and 216 µs on the same
machine).

### Outstanding, and where each is written down in code

Every item below has a ⚠️ comment at the code it concerns, so none depends on this document:

| Item | Location of the note |
|---|---|
| M4 ✅ fixed — why the two phases must stay two phases | `EquivalencyValidator.CompareCollections` and `MaximumMatcher` |
| Why the walk's position is a stack and not a string (49% of a comparison) | `PathStack`, and the push/try/finally in `CompareNode` |
| No initial capacity on the path stack — it measured worse | `PathStack`, on the segment list |
| Why traits filter in the PROVIDER and never in `CompareMembers` | `MemberSelection`, and `Context.MemberSelection` |
| The vacuity check must count the type's OWN members, not the selected ones | `EquivalencyValidator`, above the vacuity return |
| `FormattingOptions` is process-wide; `With(...)` is the thread-local form | `FormattingOptions`, class remark |
| Formatters are registered, never discovered by scanning | `Formatter.customFormatters` |
| `FailWith`: arguments are formatted, and an index is not substituted twice | `GenericCollectionAssertions.HaveNeighbour` |
| S1 ✅ — why traits are two tables and not a mask | `MemberTraits`, `EquivalencyRegistry.extended`, and the emitter's `WriteRegistration` |
| No capturing lambda in a hot method, however cold its branch | `EquivalencyRegistry.TryGetAccessors`, above the `GetOrAdd` |
| The generator and the reflection fallback are one rule in two projects | `EquivalencyScanner.TryClassify` and `ReflectionMemberProvider`, both pointing at `MemberTraitTests` |
| `CompareMultisets`' O(n)→O(n²) cliff if any option changes value equality | `EquivalencyValidator.CompareMultisets` |
| `FindByName` is O(members²) per node | `EquivalencyValidator.FindByName` |
| `GetNestedExclusions` allocates a HashSet per node when configured | `EquivalencyValidator.GetNestedExclusions` |
| `EquivalencyOptions` builds 7 collections eagerly per call | `EquivalencyOptions`, on the field block |
| MPA0004 at `Info` hides the silent 15× reflection fallback | `ErasedEquivalencyAnalyzer.Rule.cs` |
| `JsonEquivalency` is O(properties²), and order-insensitive arrays would repeat the matcher trap | `JsonEquivalency.CompareObjects` |
| The 4 remaining MPA0005 sites are the expectation side, deliberately | `GenericDictionaryAssertions`, class-level remark |
| `InspectItems` allocates an `AssertionScope` per item | `GenericCollectionAssertions.InspectItems` |
| Per-assertion gate covers one assertion per family, not all | `PassingPathAllocationTests`, KNOWN GAP block |

### Still to do

- **M4 ✅** — maximum matching landed. The gate that made it safe to attempt
  (`AnAlignedCollectionCostsOneProbePerItem`, pinned at exactly 20 probes) was written first, on
  purpose, and the probe count is unchanged by the fix.
- **S1 ✅** — generator-emitted member flags landed: `MemberTraits` on every accessor, mirrored in
  the reflection fallback, with `IncludingInternalMembers` wired end to end. Members excluded by
  default live in a SECOND table rather than being masked out of the first, so the default walk is
  handed the same array as before. Cost with the option unset: **+16 B per comparison** (two object
  size roundings), no per-node cost, node/lookup counts unchanged. It cost 2,768 B/op before the
  closure in `EquivalencyRegistry.TryGetAccessors` was removed — PRD §9.15.
- **S2** — resolved in favour of keeping the counters; they cost nothing measurable and caught a real
  bug within minutes. Open question for review: they do add a static-bool read per node to shipped
  code. If that is unacceptable, compile them out behind a symbol.
- **M2 layer 4 ✅** — nightly BenchmarkDotNet wired; see its own section. It asserts bytes and the fairness check, never a time.
- **M5** — the feature gap: 20 free-on-failure, ~40 own-type, 63 hot-path.
- **M6** — final verify. Note the README table has already been updated, ahead of M6, because the
  measurement conditions were right and waiting would have meant re-running it.

---

## M0 — Baseline ✅ done

Benchmark run on an idle machine before any change: **13.13 µs / 20.34 KB** against FluentAssertions'
**204.52 µs / 406.95 KB**. Reproduces README:236 to within 0.4% on time and **exactly** on allocation.
Recorded in PRD §2. This is the number everything is held against.

---

## M1 — .NET 11 migration and versioning (R1, R2, R3)

Deliberately first: the rest lands on a .NET 11 baseline, and without the version sweep nothing ships.

1. **Target frameworks.**
   - 67 `net10.0` projects → `net10.0;net11.0` if packable, `net11.0` if not.
   - The 6 multi-targeting projects → `net10.0;net11.0` (drops net8.0/net9.0).
   - 10 `netstandard2.0` projects → unchanged. 6 are Roslyn components loaded into the compiler host,
     2 are MSBuild task assemblies that must load under Visual Studio's .NET Framework MSBuild, and
     2 (`NestFiles`, `Vidyano.Sdk`) contain **zero `.cs` files** and are pure MSBuild payload — their
     TFM is vestigial. Do not "explain" all ten as generators.
   - 5 generator projects have **no TFM in the csproj** — they inherit it from
     `SourceGenerators/eng/sourcegenerator.targets:8`. A csproj-only grep reports these as missing.
   - `MintPlayer.SourceGenerators.Testing` moves from `net8.0` to `net10.0;net11.0`, and its csproj
     comment — which currently argues *for* the net8.0 pin — is rewritten to match (PRD §5).
   - ⚠️ D1/D2: `PackAsTool` projects and the single-TFM-path `*.Targets` packers cannot be
     multi-targeted without breaking their pack expressions.
2. **Versions.** Sweep the ~23 framework-tracking `10.x` packages to `11.0.0-rc.1`, **including
   `MintPlayer.Assertions`** (1.1.0 → 11.0.0-rc.1). Leave the SourceGenerators `10.20.x`–`10.22.x`
   line and the 5 independent lines alone.
3. **Verify the sweep against nuget.org.** For every packable project, confirm the new version is not
   already published. This is a required step, not a courtesy: all 45 packages are currently in the
   `--skip-duplicate` trap.
4. **The four out-of-csproj references** (PRD §3.5): Dockerfile base images, `Vidyano.props`'
   shipped `net9.0`/C#13, and `Verz/Program.cs:83`.
5. **CI**: bump the SDK pin in all three workflows. Consider dropping `--skip-duplicate` on the
   nuget.org push, or adding a pre-push guard that fails when a packed version already exists.
6. **LangVersion**: `14` → `15` where pinned; normalise the two `latest` pins; leave netstandard2.0
   pins in place.

**Done when** the solution builds on .NET 11, every test project runs, and a dry-run pack produces
nupkgs whose versions do not exist on nuget.org.

---

## M2 — Build the gate (R5)

**This goes before any feature.** Four layers; the first two are the merge gate.

**Layer 1 — `PassingPathAllocationTests`.** Per assertion family, measure a bare `Should()` as the
baseline and assert the full assertion adds ≤ a small allowance. Relative rather than absolute, so it
states the rule ("a passing assertion allocates nothing") instead of pinning a number that drifts.
Harness: ~2,000 warm-up iterations (tiered compilation promotes after ~30 calls and promoted code
allocates differently), `GC.Collect/WaitForPendingFinalizers/Collect`, ~20,000 measured iterations, a
`volatile` sink so nothing is optimised away, `[MethodImpl(NoInlining)]`, and **two samples with
`Math.Min`** to absorb a background GC. Cover numeric, string, boolean, date, collection, count,
inside-an-`AssertionScope`, and explicitly a multi-argument failure template.

**Layer 2 — `EquivalencyAllocationTests`.** Same harness on the benchmark-shaped graph. Absolute
ceilings, plus the assertion that matters: **unordered matching of an already-ordered collection stays
within a small multiple of the same comparison under `WithStrictOrdering()`.** Strict ordering is O(n)
by construction, so any matcher that goes quadratic diverges from it visibly and deterministically.

**Layer 3 — the boxed-enumerator analyzer**, `Warning`, scoped to the library's own assembly. Also fix
`MintPlayer.Assertions.csproj:39` to add `OutputItemType="Analyzer"` — without it the library does not
run its own rules. Consider raising MPA0004 from `Info` to `Warning` at the same time. The diagnostic
must prescribe the **correct** fix: iterate a `ReadOnlySpan<T>`, **not** an indexed loop over the
interface — indexing removes the allocation and measures *slower* than the boxed foreach it replaces,
because every indexer call and `Count` read is an un-inlinable interface dispatch.

**Layer 4 — BenchmarkDotNet, nightly and non-gating.** Scheduled, not per-PR. Its job is to keep the
README's µs numbers honest and to catch drift the other layers cannot see.

**Plus S2** (operation counts) if the spike passes — the only layer that catches a time-only
regression deterministically.

**Prove the gate works** by introducing a regression deliberately and watching it fail. A gate nobody
has seen fail is a gate nobody knows works.

---

## M3 — Fix what the gate reports (PRD §3.3)

The six pre-existing violations, in the order the gate will surface them:

1. Arity-specific `FailWith` overloads (`FailWith<T0>`, `<T0,T1>`, `<T0,T1,T2>`) alongside the
   `params` one, so the array and the boxing disappear from the passing path.
2. `AndConstraint`/`AndWhichConstraint` → `readonly struct`. ~360 return sites. `AndWhichConstraint`
   duplicates `And` rather than inheriting, because structs do not inherit.
3. `Items`/`Pairs` stop copying an already-materialised subject.
4. The 30 boxing `foreach` sites → spans.
5. `EquivalencyOptions`' seven eager collections → lazily created.
6. A `Count == 0` guard on `IsExcluded`'s wildcard loop.

Ports from `dotnet-11` are candidates for 1, 2 and 4 — **re-measured here**, not assumed.

---

## M4 — Correctness: maximum matching (S4) ✅ done

Greedy first-fit is replaced by a maximum bipartite matching, in two phases inside
`EquivalencyValidator.CompareCollections`:

1. **Greedy pre-pass, unchanged.** Still the only phase an aligned collection ever runs: ~n
   comparisons, one `bool[]`, nothing else. It bails out the moment it strands an expectation.
2. **`MaximumMatcher`** — Kuhn's augmenting paths over a **lazily filled, memoised** candidate grid,
   reached only from that bail-out. An edge test is a full recursive subtree comparison, so the memo
   is what bounds the work at n×m instead of leaving it open-ended across augmenting attempts.

Phase 1's assignments are deliberately **not** carried into phase 2. Reconstructing them would cost
an `int[]` per collection node on the passing path to serve a branch passing tests never take; paying
n×m once, on a path that is about to produce a failure message, is the cheaper trade.

**Reproducing the bug needed a non-transitive equivalence, which is why the new tests use
`object[]`.** Comparison is driven by the expectation's members, so when every item has the same type
the relation is transitive, the candidate graph is a disjoint union of complete bipartite blocks, and
greedy is accidentally optimal — a typed array cannot exhibit the bug at all. Mixed expectation types
are what make one expectation strictly pickier than another about the same subject item.
`UnorderedCollectionMatchingTests` carries that reasoning, because "tidying" those arrays to a typed
one would leave every test green and testing nothing.

Eight tests: the two-item strand, the same case with the order reversed, a three-deep displacement
chain, a genuine no-match (the fix must not become "find an excuse to pass"), leftover items, an
empty subject, strict ordering still bypassing the matcher, and the value-like multiset shortcut
still being taken.

### What running the gate turned up on the way

`PassingPathAllocationTests` was **already failing on the branch** before M4 was touched — the fix's
own safety net caught something unrelated the moment it was run again:

- **`x is null` on an unconstrained generic emits `box !T`.** `NotContainNulls` over an `int[8]` cost
  **192 B/op** — 24 B per item — to discover eight times that an `int` is not null. It reads as a
  plain null check; there is no grep for it.
- **The obvious guard has the same bug.** Writing `if (default(T) is null)` inline moved it to
  24 B/op rather than 0: the guard boxes too. It is now a `static readonly bool` per closed generic
  type (`ItemsCanBeNull`, `KeysCanBeNull`), so the box happens once at type initialisation.
- **`var pairs = Pairs;` opening six dictionary methods that never read it.** `ContainKey`,
  `ContainKeys`, `NotContainKey`, `NotContainKeys`, `Contain`, `NotContain` all answer through
  `TryGetValueForKey`, which goes at the dictionary's own `TryGetValue` — the unused local copied the
  whole subject on every call. 56 B/op for a two-entry dictionary, proportional to size for a real
  one. It reads as harmless setup and was the most expensive line in each method.

Two new gates pin these: `LookingUpAValueTypeKeyAllocatesNothing` and
`TheNullScanOverAReferenceCollectionAllocatesNothing`. **Trap for later: an allocation gate only
covers the instantiations it names.** The generic-boxing family is invisible unless a test exercises
a *value-type* instantiation specifically — `NotContainNulls` over `string[]` allocates nothing and
always did.

---

## M5a — free-on-failure ✅ done

Everything here is reached only after an assertion has already failed, so it is free by construction
— and that was verified rather than assumed: the walk measures the same before and after.

**Formatter extensibility, explicitly registered.** `Formatter.Register<T>(Func<T,string>)`,
`Unregister<T>()`, `ClearCustomFormatters()`. Matching is exact type first, then up the base chain;
interfaces are deliberately not matched, because a value implementing two registered interfaces has
no defensible winner. **Nothing is discovered by scanning assemblies** — that is the pattern that
breaks trimming and AOT, which is the property this library exists for. A formatter that throws
renders as `<custom formatter for X threw Y>` rather than replacing the failure it was rendering.

**`FormattingOptions`** — `MaxDepth`, `MaxCollectionItems`, `MaxStringLength`, `UseLineBreaks`,
`MaxLines`, all previously constants. Every elision marker now **names the knob that caused it**
(`{… depth 3 reached; raise FormattingOptions.MaxDepth to see more}`), because a truncation the
reader cannot act on sends them to a debugger for information the message could have carried.

⚠️ The properties are process-wide, so `FormattingOptions.With(...)` applies a **thread-local**
override for a `using` block, and that is what tests and concurrent code should use. This is PRD §9.9
answered before the mistake instead of after it: process-wide state plus parallel test classes is
exactly what once made a walker node count read 259 where the graph had 133. The suite's own option
tests all go through `With`.

**`AssertionScope` inspection** — `Failures` (a snapshot, not the live list), `Discard()` (returns
what it discarded, so an ignored result is not indistinguishable from a swallowed failure),
`AddPreFormattedFailure`, and reportables. `AddReportable` takes a **`Func<string>`**: reportables
exist to be read when something fails, and a passing scope must not build text nobody reads. A
nested scope hands its reportables up with its failures, since the outermost scope is the one that
throws.

**`WithDiagnostics()`** on equivalency — appends `Walk: N node(s), M member lookup(s), P collection
match probe(s).` to the failure message, positive and negative form both. It answers the two
questions a difference list cannot: *did it even look at that member*, and *why is this slow*. Off by
default and free when off; it is the first production caller of the counters S2 built for the gate.

⚠️ Two allocation traps were paid here, both caught by the gate rather than by review:

- The diagnostics branch lives in its own method, because a capturing lambda anywhere in
  `Validate` would allocate its display class at method entry on every comparison (§9.15).
- The summary is returned in a **value tuple** beside `ValidationResult`, not stored on it. A
  nullable field on that record measured 13,992 → 14,000 B/op, because it is allocated once per
  comparison and would charge every passing suite 8 bytes for a diagnostic almost nobody enables.

### The walk itself got 51% cheaper on the way past

Not planned as part of M5a; found because the 8-byte question above made the number worth looking at.
The walker threaded its position as a **string**, rebuilt at every step — `$"{path}.{member}"` once
per member per node. That was **6,848 bytes, 49% of the whole comparison**, and a passing comparison
never reads a path.

`PathStack` replaces it: a push/pop stack of segments on the context, rendered only on demand.
**13,992 → 6,808 B/op**, node/lookup/probe counts unchanged, 958 tests untouched. PRD §9.17 and
§9.18 have the details, including the design that does not compile (CS9050) and the capacity hint
that measured worse.

Gates re-tightened to the new floor — a bound left at the old figure quietly re-authorises the
regression that was just removed:

| Gate | Was | Now |
|---|---:|---:|
| Tight walk bound | 14,100 B | **6,900 B** (measured 6,808) |
| Loose shape alarm | 32 KB | **16 KB** |
| Unordered ÷ strict | 1.15× | **1.2×** (measured 1.11×) |

---

## M5 — Features, cheapest class first (R4)

**M5a — free-on-failure (20 items).** ✅ Done; see the M5a section above. Formatter extensibility with explicit registration (no assembly
scan), configurable `MaxDepth`/`MaxLines`/`UseLineBreaks` with a depth-exceeded message that names the
knob, `AssertionScope` inspection (`Discard`, `AddPreFormattedFailure`, reportables), equivalency
diagnostics. Reached only after an assertion has failed, so free by construction.

**M5b — own-type, reflection-free only (~95 items minus the ~100 cut in PRD §5).** Streams, XML
(LINQ-to-XML and the DOM), `TaskCompletionSource`, ValueTask, `ExecutionTimeOf`, `OccurrenceConstraint`,
`StringCollectionAssertions`, the per-primitive `Not*`/`BeNull` gaps, `BeSupersetOf` /
`BeProperSubsetOf` / `BeProperSupersetOf`, `HaveElementAt`/`Preceding`/`Succeeding`,
`ContainInConsecutiveOrder`, `ThenBeInAscendingOrder`, comparer-lambda overloads. Cost falls on the
caller only, and **none of these needs reflection** — that is now the entry condition for this
milestone, not a nice-to-have.

The Types/MemberInfo/Assembly/selector family is **out of scope** (PRD §5). Anything that reaches for
`Type.GetProperty`, `GetMethod`, `GetInterfaces`, `GetTypes` or `GetCustomAttribute` to answer an
assertion belongs with it and does not land here.

**M5c — hot-path (63 items), unblocked by S1 ✅.** Chiefly the ~46 missing equivalency options. The ~30
compile-time-decidable ones ride on generator-emitted flags; the ~14 needing runtime reflection go
behind an explicit opt-in so they never cost a test that does not use them.

⚠️ **The naming trap.** Every assertion ends `(…, string? because = null, params object?[] becauseArgs)`
and that tail swallows anything compatible with it. A new parameter that could be a string does not
belong beside `because` — it belongs in a differently named method. This bit three times in the
previous attempt, each time invisible to the compiler and to review.

---

## M5c — hot-path options (first tranche) ✅ partial

The first equivalency options answered from generator-emitted member flags rather than reflection,
which is what S1 was built for:

| Option | Rides on |
|---|---|
| `ExcludingFields()`, `ExcludingProperties()` | the `Property` / `Field` traits |
| `IncludingNonBrowsableMembers()` | the `NonBrowsable` trait |
| `IncludingInternalMembers()` (M5a/S1) | the `NonPublic` trait |

**`MemberSelection` keeps the two directions apart.** `Wanted` ADDS members that are off by default;
`ExcludedKinds` REMOVES members that are on by default. Folding them into one mask would make every
call site guess which direction a bit means. It is the cache key for both providers, so a filtered
member list is built **once per (type, selection)** and the walk itself does no per-member testing —
`MemberSelection.Default` short-circuits to the exact array the provider held before selections
existed. The byte gate is unchanged at 6,808.

### It opened a hole in the vacuity check, and the test caught it

Excluding both kinds leaves nothing to compare, which should be rejected as a vacuous comparison.
It was not: the check said "two memberless values really are equivalent, so the subject must have
members for this to count as vacuous", and with both tables filtered empty, a node whose members had
all been REMOVED BY OPTIONS looked identical to a genuinely memberless value. The assertion went
green.

Fixed by testing the type's OWN member count, not the selected one — an unfiltered lookup that is
cached and only runs when nothing was compared, so a normal comparison pays nothing. Worth recording
as a shape: **a filter added upstream of a "did we assert anything?" check can make "nothing to do"
indistinguishable from "nothing was asked for".**

### Still outstanding in M5c

The remaining ~43 equivalency options, including the ~14 that genuinely need runtime reflection and
must therefore go behind an explicit opt-in so they never cost a test that does not use them.

---

## M2 layer 4 — the nightly benchmark ✅ done

`.github/workflows/assertions-benchmark.yml`. Nightly at 03:00 UTC plus `workflow_dispatch`.

**It is deliberately NOT a gate, and it asserts no wall-clock figure at all.** The tight gates live
in the test suite and assert bytes and operation counts, which are the same on a developer's machine
and on a loaded runner. Time is not: this benchmark measured FluentAssertions at 150 and 216 us on
the same idle machine, a 44% spread on identical code. A millisecond threshold on a shared GitHub
runner would be flaky or useless, and adding one would teach people to ignore the job.

What it does instead: publishes the BenchmarkDotNet table to the job summary, and fails on two
things that DO reproduce —

1. **The allocation figure**, against a 7,168 B ceiling (measured 6,808). Overridable per run via the
   dispatch input.
2. **A missing result row, or a failed fairness check.** Both are failures rather than passes. A run
   whose fairness check did not pass may have measured the reflection fallback and would otherwise
   report a flattering number for the wrong code.

Nightly rather than per-PR because a full BenchmarkDotNet run costs minutes of queue time for a
signal the unit tests already give in seconds, and because a shared runner is the worst available
place to measure anything.

⚠️ **It will not run until this branch is merged.** GitHub only schedules workflows from the
default branch, so the first real execution is the night after the merge — which also means the
first run is the one that proves the parsing works. `workflow_dispatch` is there so that can be
checked on purpose rather than discovered.

---

## M5b — own-type, reflection-free (collections tranche) ✅ partial

Landed, all reflection-free and all own-type cost — they run only for the caller who invoked them:

| Added | Notes |
|---|---|
| `BeSupersetOf`, `NotBeSupersetOf` | mirrors of the existing subset pair |
| `BeProperSubsetOf`, `BeProperSupersetOf` | ⚠️ "proper" compares DISTINCT VALUES, not item counts — `[1,1]` is a proper subset of `[1,2]`. Comparing counts is the obvious implementation and is wrong for any collection with duplicates; there is a test for exactly that case. |
| `HaveElementAt` | returns the element via `.Which`. An out-of-range index is a FAILURE, not an `ArgumentOutOfRangeException`: the test is asserting something false about the collection, and an exception would report that as an error instead. |
| `HaveElementPreceding`, `HaveElementSucceeding` | first occurrence of the anchor |
| `ContainInConsecutiveOrder`, `NotContainInConsecutiveOrder` | the strict sibling of `ContainInOrder`, which allows gaps. The difference is asserted rather than described, because it is the only reason both exist. |
| `BeOrderedBy`, `BeOrderedByDescending` + `ThenBeInAscendingOrder` / `ThenBeInDescendingOrder` | multi-key ordering, each level with its own direction |

**The naming trap, in a form the plan did not anticipate.** `ThenBe…` needs the ordering assertion to
return something other than `AndConstraint`, and changing `BeInAscendingOrder`'s return type is a
breaking change to a shipped surface. A new overload cannot help either: overloads differing only by
return type are not selectable, and one differing by an added parameter risks being swallowed by the
`because`/`becauseArgs` tail. Hence a differently named entry point, `BeOrderedBy`, with
`BeInAscendingOrder(selector)` left exactly as it was.

`Then…` re-checks the whole collection with a composite comparer rather than only the tied runs.
Same answer — a sequence is ordered by (a, b) exactly when every adjacent pair is — and the composite
cannot get the tie boundaries wrong.

**One message bug, caught by a test rather than by review:** `FailWith` renders arguments through the
Formatter, so passing the word "preceding" as an argument quoted it; and a template mentioning `{1}`
twice does not substitute it twice. Both are now commented at the method, because the failure looked
like a wrong assertion rather than a wrong message.

### Still outstanding in M5b

Streams, XML (LINQ-to-XML and the DOM), `TaskCompletionSource`, ValueTask, `ExecutionTimeOf`,
`OccurrenceConstraint`, `StringCollectionAssertions`, the per-primitive `Not*`/`BeNull` gaps, and the
comparer-lambda overloads. All still in scope and all still reflection-free; none started.

---

## M6 — Verify ✅ run, with results

Run against the branch, not recalled. Each line is what was actually observed.

| # | Check | Result |
|---|---|---|
| 1 | Full solution **Release** build | 0 errors. Zero trim warnings attributable to `MintPlayer.Assertions`. |
| 2 | Full `dotnet test` sweep, Release | **24 test projects, 0 failures.** `MintPlayer.Assertions.Tests` 981 × 2 TFMs; `MintPlayer.SourceGenerators.Tests` 263 (the packaging tests among them). |
| 3 | The gate passes **and has been seen to fail** | Reintroduced the §9.15 closure on purpose. `TheDefaultWalkStaysUnderItsMeasuredByteCost` failed at **9,560 B/op against 6,808** — and the 16 KB alarm correctly stayed quiet, which is the two-gate design doing its job. Reverted; green again. |
| 4 | Benchmark re-run on .NET 11, idle machine | **9.58 us / 6.59 KB** against FluentAssertions' 167.82 us / 397.04 KB. README updated. |
| 5 | Pack, and the analyzer payload lands | `MintPlayer.Assertions.11.0.0-rc.1.nupkg` carries `lib/net10.0`, `lib/net11.0` and both analyzer assemblies. `PackagingTests` — which packs and inspects for real — passes, including the Roslyn-folder layout from S3 and Debug/Release parity. Consumed and exercised from **both** a net10.0 and a net11.0 test project. |
| 6 | Bumped versions absent from nuget.org | `mintplayer.assertions`, `mintplayer.sourcegenerators`, `mintplayer.verz` — no `11.0.0-rc.1` published. |
| 7 | README and PRD/plan current | Done, including the deliberate boundaries. |

⚠️ **On check 5, one thing that looks wrong and is not.** The Assertions package ships its analyzers
at `analyzers/dotnet/cs`, NOT the `analyzers/dotnet/roslyn5.0/cs` that S3 established. That is
deliberate and documented in the csproj: S3 governs the standalone generator packages, which import
`eng/sourcegenerator.targets`; this library cannot import that file (it would strip `lib/` and mark
the package a development dependency) and ships its analyzers itself, unversioned, so they load under
every Roslyn version. Verified rather than assumed, because the mismatch reads as a bug.

⚠️ **On check 6, the 33 bumped versions are not every package.** Ten packages remain on 10.x, and
each was checked rather than waved through: all are `netstandard2.0`, directly or via
`eng/sourcegenerator.targets`. They do not depend on .NET Core, so R3 does not apply to them.

---

## M6 — original checklist

1. Full solution Release build, 0 errors, zero trim warnings from `MintPlayer.Assertions`.
2. Full `dotnet test` sweep.
3. The gate passes, and has been seen to fail.
4. **Re-run the benchmark on .NET 11, on an idle machine**, and compare against PRD §2. Only then
   update README:236 — the baseline is a .NET 10 host measurement and mixing runtimes would attribute
   a runtime difference to our code.
5. Pack and confirm the analyzer payload still lands correctly (S3), verified in both a .NET 10 and a
   .NET 11 consumer.
6. Confirm every bumped version is absent from nuget.org.
7. README and PRDs updated, including the deliberate boundaries so they read as decisions rather than
   gaps.

---

## Definition of done — per milestone

Every one of these encodes a trap from PRD §9 that has already cost time. Run the checklist, do not
recall it.

- [ ] **Grep proves each named symbol exists**, and a test exercises it. Two milestones were once
      marked ✅ with four items never built (§9.6).
- [ ] **Full-solution Release build**, not a project build — the whole repo dogfoods this assertion
      library, so a rename inside `Assertions/` can break `Math.Tests` (§9.8).
- [ ] **Zero trim warnings**, and every `[UnconditionalSuppressMessage]` id copied from actual build
      output rather than from memory or a neighbouring suppression (§9.10).
- [ ] **The allocation gate ran** for any change touching the walker, collection assertions, or
      `FailWith` (§9.1, §9.7).
- [ ] **Any new overload has a test asserting the OLD call shape still binds where it did** (§9.3).
- [ ] **No bulk regex rewrite went in unread**, and tests ran before that commit, not after (§9.5).
- [ ] **Versions bumped for anything that changed**, verified absent from nuget.org (§9.11).

## Before opening the PR

- [ ] Re-run the benchmark on .NET 11, idle machine, and compare against PRD §2.
- [ ] Confirm the gate has been **seen to fail** — introduce a regression, watch it go red, revert.
- [ ] `grep -rn "net8.0\|net9.0" --include=*.csproj` returns nothing.
- [ ] Every packable project's version is absent from nuget.org.
- [ ] Dockerfile base images match their csproj TFMs (§9 / PRD §3.5 — this one is green in CI and
      crashes on container start).

## Risk register

| Risk | Mitigation |
|---|---|
| A feature lands that regresses the benchmark | M2 before M5; every hot-path item measured |
| The release silently publishes nothing | M1 step 3, and a CI guard |
| A "correctness fix" goes quadratic | S4's rule, and Layer 2's strict-vs-unordered assertion |
| A type silently falls onto the reflection fallback, costing 15× with no signal | Extend `GeneratedAccessorInvariantTests`; consider raising MPA0004 to Warning |
| The generator and the reflection fallback disagree about a new trait | Now enforced, not merely warned about: `MemberTraitTests` compares the two member-for-member on a type the generator actually scans |
| Container crashes on start after a green CI run | M1 step 4 — Dockerfile and csproj move together |
