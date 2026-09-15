# PRD — MintPlayer.Assertions Phase 2: FluentAssertions parity, without losing the performance

Companion to [Assertions-prd.md](./Assertions-prd.md) (v1, delivered) and
[Assertions-plan.md](./Assertions-plan.md). Implementation plan:
[Assertions-phase2-plan.md](./Assertions-phase2-plan.md).

Source: a five-way comparison of `MintPlayer.Assertions` against the FluentAssertions source tree
(`Src/FluentAssertions`), covering primitives/numerics/strings/dates, collections + the equivalency
engine, exceptions/async/execution-time/events, and types/formatting/execution/extensibility, plus a
structural map of this library's own performance mechanism.

---

## 0. THE HARD BOUNDARY — no performance loss

**This is an invariant, not a goal. Any change that violates it is rejected regardless of how much
API surface it buys.**

### What "the performance" actually is

The headline (~15× faster, ~20× fewer allocations) rests on **exactly two benchmarks**
(`MintPlayer.Assertions.Benchmarks/EquivalencyBenchmarks.cs:11-31`), both `BeEquivalentTo` over one
5-type / 4-level / 20-item object graph against FluentAssertions 7.2.2:

| | time | allocated |
|---|---|---|
| MintPlayer.Assertions | 13.08 µs | 20.34 KB |
| FluentAssertions 7.2.2 | 201.08 µs | 409.14 KB |

Nothing else in the library is benchmarked. So the claim is **about the equivalency walker only**,
and the boundary has to be read accordingly: the thing that must not regress is (a) that walker, and
(b) the per-assertion cost of every *other* assertion, which is currently unmeasured and therefore
unprotected.

The mechanism is one design choice: the generator emits `[ModuleInitializer]`-registered
`MemberAccessor`s — `static o => ((Product)o).Price` — into `EquivalencyRegistry`, replacing
`PropertyInfo.GetValue`. Everything else (struct `Assertion`, failure-path-only formatting,
`[CallerArgumentExpression]` instead of stack walking, no LINQ in the walker) supports it.

### The operational rule

> **Nothing may be added to the passing path.**

A passing assertion must not allocate more, reflect more, or branch more than it does today. This is
sharper than "don't regress the benchmark" and it is the rule contributors can actually apply while
writing code.

Corollary that makes most of this PRD tractable: **a feature that only does expensive work when an
assertion FAILS costs nothing.** `FailWith` returns immediately when the condition holds
(`Execution/Assertion.cs:60`), so `RenderMessage`/`Formatter.Format` never run in a green suite.
Pluggable formatters, configurable depth, native test-framework exceptions and richer failure detail
all live here and are free.

Second corollary: **a feature on its own assertion type costs nothing to assertions that don't use
it.** `HaveMethod` on `TypeAssertions` is hot-path reflection, but only for the person who called it.
This is what makes §5 admissible without breaching the boundary.

### Enforcement — what was built, and what was not

The v1 plan records "Benchmarks are present, not gating" (`Assertions-plan.md:85`). That gap made
the boundary unenforceable. Two mechanisms now close it, and one planned mechanism was dropped.

**Built — `PassingPathAllocationTests`.** Runs N passing assertions and asserts the assertion adds
nothing over a bare `Should()` on the same subject. Allocation rather than time, deliberately: bytes
per operation are deterministic and can fail a build honestly, where wall-clock in CI is noise and a
timing gate is either too loose to catch anything or flaky. Relative to a per-case baseline rather
than an absolute byte count, so it states the rule instead of pinning a number that drifts.

**Built — `MPA0005`, the analyzer.** `foreach` over an interface-typed indexable collection boxes
the underlying struct enumerator: one allocation per call, on the passing path, and **invisible in
the source** — the loop is character-for-character identical to an allocation-free one, and only the
static type decides. Neither review nor measurement-by-sampling reliably finds that. See §12.

**Built — `PassingPathBenchmarks`.** Per-assertion wall-clock for one representative assertion per
family, against FluentAssertions. Informational, not gated.

**NOT built — gating the equivalency benchmark.** Still "present, not gating". Wiring BenchmarkDotNet
into CI as a pass/fail gate is noise-prone, and the allocation test plus the analyzer cover the
regressions that actually occurred. Left open deliberately rather than silently: if a *time*
regression with no allocation change ever appears, nothing currently catches it.

**Dropped — a data-driven allocation sweep.** A single test looping a table of every assertion,
reporting all offenders at once, was built and then removed in favour of the analyzer. It found 10
of 34 sampled assertions allocating, which is what surfaced the fixes below — but it only ever covers
rows someone remembered to add, whereas the analyzer is exhaustive over the code. Worth knowing it
existed, and why it is gone.

### What the boundary work actually found

Five *pre-existing* passing-path violations, none introduced by Phase 2. This is the evidence that
Milestone 0 belonged first:

| Violation | Scale |
|---|---|
| `FailWith`'s `params object?[]` allocating and boxing at the call site | 339 of 366 call sites |
| `AndConstraint` / `AndWhichConstraint` being classes | ~360 return sites |
| `HaveComponent` concatenating its message template on every call | 31 call sites |
| `Items` / `Pairs` copying an already-materialised subject | every collection and dictionary assertion |
| `foreach` boxing the interface enumerator | 27 sites, incl. 2 in `EquivalencyValidator` |

The last one was found by the analyzer, not by measurement, and two of its sites sit in the
equivalency walker the headline number comes from. Twice more, the boundary caught a regression
*while it was being written* rather than after: a `Func<int,bool>` helper that would have captured
and allocated a closure per call, and a message fragment that would have been concatenated eagerly.

### The nine constraints a contributor must follow

Derived from how the current code actually achieves its numbers. Each maps to real code:

1. **Never resolve members by reflection on the equivalency path.** Go through
   `IMemberProvider`/`RegistryMemberProvider` (`Equivalency/RegistryMemberProvider.cs:16`). A new
   `GetProperties`/`GetValue` call defeats the generated accessors and drops the whole gain. The only
   reflection permitted in the runtime is the three existing sites:
   `ReflectionMemberProvider.cs:28,33` (documented fallback), `Formatting/Formatter.cs:199,218`
   (failure path only), `Events/EventMonitor.cs:31,32,210` (why event monitoring carries
   `[RequiresDynamicCode]`).
2. **If a new generated shape is needed, extend `EquivalencyScanner`, not the runtime.** A feature
   needing member info the generator does not emit silently falls back to reflection for those types
   (`Helpers/EquivalencyScanner.cs:10-18`).
3. **No eager message formatting.** The pattern is
   `Assert().ForCondition(cond).BecauseOf(...).FailWith(template, args)`, where `FailWith` bails on
   the condition first. Building a message — or a `$"..."` interpolation containing
   `Formatter.Format` — *before* the condition check puts formatting on the passing path.
4. **No LINQ on traversal.** `EquivalencyValidator.cs` and the collection assertions are explicit
   loops. A `.Select()`/`.Where()` in `CompareNode`/`CompareMembers` adds an enumerator + closure per
   node.
5. **No boxing that isn't already there.** `MemberAccessor.Getter` is `Func<object, object?>`, so
   value-typed members already box once per node — that is the accepted floor. Do not add a second
   hop.
6. **No per-node dictionary lookups keyed on strings.** Option lookups are guarded by count checks
   first (`EquivalencyValidator.cs:342,353-358,368-370`). A new option must cost nothing when unused.
7. **Don't make `Should()` allocate more.** Subject assertion classes are already one allocation per
   call; per-call state multiplies across every assertion in a suite.
8. **Anything requiring `Activator.CreateInstance`, `MakeGenericMethod` or `Expression.Compile()`
   breaks AOT**, not just speed — the package asserts `IsAotCompatible=true` with zero trim warnings.
   Note the plan records that two trim suppressions once cited the wrong diagnostic id, so "the
   AOT-clean claim was not actually being enforced" (`Assertions-plan.md:115-116`); suppressions must
   be exact.
9. **Keep `Func<>` + `[CallerArgumentExpression]`, never FA's `Expression<Func<>>`.** This is the
   single most important API-shape rule in this document — see §1.

### What is NOT a constraint

**Backward compatibility.** Package versions are bumped for this work, so a source break is an
acceptable price for the right API. This is a deliberate licence, and it changes several decisions
below: names that diverge from FluentAssertions for no good reason get **renamed**, not aliased
(§3.1, §8), and `Subject` shapes can change where the correct shape differs from today's (§2.1).

The one thing this licence does *not* extend to is the boundary above. A faster-but-uglier API is
still preferred to a prettier-but-slower one.

---

## 1. The `Expression<Func<>>` decision (why MintPlayer is already faster per call)

FluentAssertions takes `Expression<Func<T,bool>>` for predicates so it can print the predicate source
in the failure message. That costs an expression-tree allocation at every call site plus
`Expression.Compile()` — runtime IL emit, AOT-hostile, slow on first call.

MintPlayer takes plain `Func<T,bool>` plus `[CallerArgumentExpression]`, which recovers the predicate
**source text at compile time for free** and never compiles anything. The result is the same message
quality at strictly lower cost, and it is why several MintPlayer assertions are *cheaper than FA's*
today (`ExceptionAssertions.Where`, `EventAssertions.WithArgs`, `GenericCollectionAssertions.Contain`).

**Every new predicate-taking assertion in Phase 2 uses `Func<>` + `[CallerArgumentExpression]`.** Any
FA signature quoted in this document that uses `Expression<>` is to be read as its `Func<>`
equivalent. This is a deliberate, permanent divergence from FA's signatures.

---

## 2. Correctness defects (P0 — these are bugs, not gaps)

Ship these first. They are not API surface; they are wrong answers and a leak.

### 2.1 `AggregateException` is not unwrapped

FA has `AggregateExceptionExtractor` (`Specialized/AggregateExceptionExtractor.cs:11-40`). An action
throwing `AggregateException(inner: ArgumentException)` satisfies `Throw<ArgumentException>()` in FA.
MintPlayer compares the caught exception directly (`Specialized/ActionAssertions.cs:74`,
`FuncAssertions.cs:73`), so the same test **fails**.

This changes what `Throw<T>` *means*, and it changes `Subject`: FA's is `IEnumerable<TException>`
(several matches can survive extraction) where MintPlayer's is a single `TException?`.

> **Decided against FA's shape during implementation.** `IEnumerable<TException>` costs a collection
> allocation on the passing path of every exception assertion, and §0 outranks FA-shape-matching.
> The case it buys — several matching inner exceptions, each needing separate assertions — is
> vanishingly rare. `ExceptionExtractor` returns the **first** match, seeing through nested wrappers
> via `Flatten()`, and stays opaque when the caller asks about `AggregateException` itself.

Cost: none. Pure `try`/`catch` plus type tests; the throw dominates.

### 2.2 `ExecutionTime` on a hanging action hangs forever

FA runs the action on a background task and **polls** (`Specialized/ExecutionTimeAssertions.cs:40-63`),
so `BeLessThan(1.Seconds())` against an action that never returns fails after ~1s. MintPlayer invokes
inline on the calling thread and only then compares (`Specialized/ExecutionTimeAssertions.cs:74-89`),
so the same assertion **never returns** and the test run wedges.

Two further consequences of the inline design: an exception thrown by the action propagates raw
rather than from a controlled point, and chaining re-runs the action (FA measures once into an
`ExecutionTime` object that every assertion reads).

Cost: FA's polling design is more expensive than inline for the common fast-action case. Resolve by
keeping a cheap path for actions that complete promptly and only escalating to a polled/background
measurement when a limit exists to exceed — do not blanket-adopt `Task.Run` + polling.

### 2.3 `FailWith` allocates on every passing assertion

`FailWith(string, params object?[])` (`Execution/Assertion.cs:58`) allocates the `object?[]` and boxes
value-type arguments **on every call, passing or failing** — only the *formatting* is deferred. FA
avoids this with `FailWith(string, params Func<object>[])` and `Given<T>(Func<T>)`
(`Execution/AssertionChain.cs:225,205`).

This is the one place MintPlayer is measurably **slower per call than FA**, and it sits on the hot
path of every assertion in every suite. Fixing it is a performance *gain* and directly serves the
boundary. It is also the item most likely to be invisible without the §0 micro-benchmarks.

### 2.4 `CompleteWithinAsync` leaks a timer

`Task.WhenAny(task, Task.Delay(timeout))` with no `CancellationTokenSource`
(`Specialized/AsyncFunctionAssertions.cs:115`), so the delay survives the success path. Small, but
per-assertion.

### 2.5 `Assertions/MintPlayer.Assertions.Analyzers` is an empty directory

No project, no sources. The analyzers actually live in
`MintPlayer.Assertions.SourceGenerator/Diagnostics/` under namespace
`MintPlayer.Assertions.Analyzers.Diagnostics`. Remove the directory or document why it exists.

Related doc staleness: `MintPlayer.Assertions.SourceGenerator/README.md:7-9` claims "There is no
analyzer test infrastructure in this repo", but `MintPlayer.Assertions.SourceGenerator.Tests/` now
exists with per-analyzer and per-generator tests.

---

## 3. Free wins (no mechanism at all — comparison or property read)

All of these are pure additions with no allocation, no reflection, no LINQ. They are the highest
surface-per-cost items in the document.

### 3.1 `BeApproximately` / `NotBeApproximately` — rename, not alias

The behaviour already exists as `BeCloseTo`/`NotBeCloseTo` (`Primitives/NumericAssertions.cs:162,170`),
generic-math based so it covers every numeric `T` where FA needs 9 overloads each. **This is the
single highest-volume naming incompatibility for anyone arriving from FA** — it is *the* float
assertion.

Note the actual shape of the divergence: FA uses `BeCloseTo` for **temporal** types and
`BeApproximately` for **numerics**. MintPlayer uses `BeCloseTo` for both, so the numeric spelling is
the odd one out — and it collides conceptually with the temporal one. With no backward-compatibility
constraint: **rename the numeric overloads to `BeApproximately`/`NotBeApproximately` and leave
`BeCloseTo` on the temporal types.** That is both FA-aligned and internally more consistent than
today. Pure rename, zero cost.

### 3.2 `BeNull` / `NotBeNull` on nullable value types

`int?.Should().BeNull()` does not compile today. MintPlayer collapses nullable into the same class per
family and exposes only `HaveValue`/`NotHaveValue`; `BeNull`/`NotBeNull` exist solely on
`ReferenceTypeAssertions` (`Primitives/ReferenceTypeAssertions.cs:30,37`). The logic already exists
under a different name in every class — this is a discoverability cliff, not a feature gap.

FA's `NotBeNull` returns `AndWhichConstraint<…, TSubject>` so the unwrapped value chains; matching
that costs one constraint per call (`AndWhichConstraint` already exists).

### 3.3 The sixteen missing temporal negatives

`NotBeBefore` / `NotBeOnOrBefore` / `NotBeAfter` / `NotBeOnOrAfter` across **DateTime,
DateTimeOffset, DateOnly and TimeOnly**. MintPlayer has every positive direction; the negatives read
better than the logically-equivalent positive and are conspicuously absent. One comparison operator
each.

### 3.4 `BeOneOf` where it is missing

Present on numerics, enums, DateTime, DateTimeOffset, DateOnly. **Missing on strings, comparables,
objects and TimeOnly.** Strings are the most common `BeOneOf` target of all. Write as `foreach` with
early exit — not LINQ `Contains` — and allocate only when building the failure message.

### 3.5 `DateTimeOffset.BeExactly` / `NotBeExactly`

Semantically load-bearing, not sugar. FA's `Be` compares instants, so
`2024-01-01T12:00+00:00` equals `2024-01-01T13:00+01:00`; `BeExactly` additionally requires the same
`Offset`. **Unexpressible in MintPlayer today** — `Primitives/DateTimeOffsetAssertions.cs:28` is the
only equality. One extra `Offset` comparison.

### 3.6 Remaining free items

- `HaveMillisecond` / `NotHaveMillisecond` on DateTime and DateTimeOffset. `TimeOnlyAssertions`
  already has `HaveMilliseconds`; DateTime stops at `HaveSecond` — an asymmetry that bites on
  timestamp-truncation tests.
- `NotBeTrue` / `NotBeFalse` on nullable bool: null-tolerant negatives. `NotBeTrue()` passes for both
  `false` and `null`, which `BeFalse()` does not. Real tri-state-flag tests need it.
- `Imply(bool consequent)` on booleans: `!subject || consequent`.
- `BeRankedEquallyTo` / `NotBeRankedEquallyTo` on comparables. The `CompareTo() == 0` vs `Equals()`
  distinction is *why* `IComparable` assertions exist separately; `ComparableAssertions.Be`
  (`Primitives/ComparableAssertions.cs:36`) conflates them, so `decimal` 1.0 vs 1.00 and custom
  `IComparable` types silently give the wrong answer.
- `Match` predicate on numerics, enums and nullables — the universal escape hatch, currently only on
  `ReferenceTypeAssertions`. Use `Func<>` + `[CallerArgumentExpression]` (§1), **not** FA's
  `Expression<>`.
- Exceptions: non-generic `Throw()`/`ThrowAsync()`, `NotThrow<TException>` exclusion, sync
  `NotThrowAfter`, `WithoutMessage`, runtime-`Type` inner-exception overloads, `And` alias.
- Async: `ThrowWithinAsync`, `NotCompleteWithinAsync`, `NotThrowAfterAsync` on the generic variant,
  the whole `TaskCompletionSource` family, and **all `ValueTask` support** (grep for `ValueTask`
  returns nothing).
- Collections: bulk membership — shipped as `ContainAll` / `NotContainAny`, see §8 — `BeSupersetOf`,
  `BeProperSubsetOf`/`BeProperSupersetOf`, `HaveElementAt`/`HaveElementPreceding`/
  `HaveElementSucceeding`, key-selector overloads on `OnlyHaveUniqueItems` and `NotContainNulls`,
  comparer-lambda overloads on `Equal`/`StartWith`/`EndWith`, `ContainItemsAssignableTo`.
- Dictionaries: `Equal`/`NotEqual`, the bulk `params`/`IEnumerable<KVP>` `Contain`/`NotContain`
  overloads, and the missing count family (`HaveCountGreaterThan` etc.).

---

## 4. Cheap if written deliberately

Admissible, but each has an obvious naive implementation that would breach the boundary. The correct
mechanism is named.

| Item | Naive (rejected) | Required mechanism |
|---|---|---|
| `HaveLineCount` / `NotHaveLineCount` / `ContainLine` / `NotContainLine` | FA's `SplitLines` allocates a `string[]` plus one `string` per line on **every** call | `ReadOnlySpan<char>` + `MemoryExtensions.EnumerateLines()`, `SequenceEqual` — allocation-free; allocate only for the message. Directly relevant: this repo ships source generators and asserts on generated output |
| `OccurrenceConstraint` (`Contain("x", Exactly.Twice())`, `MatchRegex(…, AtLeast.Once())`) | `Regex.Matches(...).Count` allocates a `MatchCollection` plus a `Match` per hit | `MemoryExtensions.IndexOf` loop over spans for the literal case; `Regex.Count` (.NET 7+) for the regex case. One small class per call site is the accepted floor |
| `MatchRegex(Regex)` / `NotMatchRegex(Regex)` | — | **This is a fix, not a cost.** Today `MatchRegex` calls static `Regex.IsMatch` (`Primitives/StringAssertions.cs:344`), which goes through the framework cache capped at `Regex.CacheSize` (default 15). A suite with more than ~15 distinct patterns **re-parses and re-compiles on every call**. A `Regex`-typed overload lets callers hoist a `[GeneratedRegex]` out of the hot path |
| String `config` overloads (`IgnoringCase`, whitespace, newline style) | FA clones a default options object per call plus a `Func<>` closure (`StringAssertions.cs:153`) | A `StringComparison`/flags-enum parameter — zero allocation, no closure. Normalisation over spans, not `Replace` |
| Better unordered-collection matching | — | **Re-classified during implementation: this was a correctness bug, not an allocation one.** Greedy first-fit let each expectation claim the first subject item it matched, stranding a later expectation with only one candidate left — expectations `[A, B]` against subjects `[X, Y]` where `A` matches both and `B` only `X` reports a difference although the perfect matching `A→Y, B→X` exists. So `BeEquivalentTo` could **fail on equivalent data** depending on item order. Replaced with maximum bipartite matching via augmenting paths. The allocation win (one reusable collector instead of a `List<Difference>` per probe — ~400 on a failing 20×20) came along with it |

---

## 5. Failure-path only (free by construction)

Everything here is reached only after an assertion has already failed. The boundary permits it
outright.

- **A pluggable formatter seam.** `Formatting/Formatter.cs` is a closed `public static class` with
  `private const MaxDepth = 3` / `MaxEnumerableItems = 32` / `MaxStringLength = 512`. There is **no**
  extension point at any level — a third party cannot change how any type renders. FA has three
  levers (global registry, scope-scoped, `[ValueFormatter]` attribute). Adopt the first two;
  **reject the attribute scan** (assembly scanning is exactly what the AOT posture rules out).
- **Configurable `MaxDepth` / `MaxLines` / `UseLineBreaks`**, and an actionable depth-exceeded
  message naming the knob (FA does; MintPlayer cannot, because there is no knob).
- **Multi-line indented graph rendering.** MintPlayer emits one flat line; large object diffs are
  unreadable. FA has `FormattedObjectGraph` (208 lines).
- **Native test-framework exceptions.** MintPlayer always throws `AssertionFailedException`, so
  runners that special-case their own type (inconclusive vs failed, IDE diffs) see a generic
  exception. The late-bound reflection sits in `AssertionScope.ReportFailure`/`Dispose`, reached only
  on failure, and caches once. ⚠️ The v1 PRD calls per-framework exceptions an explicit non-goal
  (`Assertions-prd.md:49-51`) — **this needs a decision, not an assumption.**
- **`AssertionScope` inspection**: `Discard()`, `AddPreFormattedFailure`, reportables. MintPlayer has
  `HasFailures` and nothing else, which is what blocks a `BeEquivalentTo`-style "here is the full
  diff context" block.
- **Richer detail for reflective assertions** (listing the members that didn't match) — reflect only
  when building the message.

---

## 6. Equivalency: the real decision

MintPlayer has **12 options** against FA's **~60**. The v1 PRD does not declare this a deliberate
limitation — the opposite. It names FA's reflection walker as problem #1, lists equivalency in the v1
surface, and states **"Everything above ships in v1. There is no deferred tier"**
(`Assertions-prd.md:154`). So the narrowness reads as scope not reached, against a document that
disclaims a deferred tier. **Phase 2 exists partly to correct that record.**

### 6.1 Compile-time decidable — buildable with no new reflection

These are filters over a member table the generator already builds, or facts it already knows:

- `IncludingFields` / `ExcludingFields` / `IncludingProperties`, internal-member inclusion,
  `ExcludingExplicitlyImplementedProperties`, `ExcludingNonBrowsableMembers`. `MemberAccessor`
  already carries `IsProperty`; `EquivalencyScanner.cs:111-126` already knows accessibility,
  `[EditorBrowsable]` and explicit-interface status — it just does not emit them. **Add flags to
  `MemberAccessor`.**
- `ExcludingMembersNamed(params string[])`, `Excluding<TMember>()` / `Excluding(Type)` —
  `MemberAccessor.Type` is already emitted.
- `WithMapping` (subject↔expectation name mapping) — pure name-level.
- `Including` at arbitrary depth. Today it is **root-level only**, enforced at
  `EquivalencyValidator.cs:163` via `path.Length == 0`. Path bookkeeping only.
- `ExcludingMissingMembers` / `ThrowingOnMissingMembers` — a policy branch at
  `EquivalencyValidator.cs:169`.
- `ComparingEnumsByName` / `ComparingEnumsByValue` — emit an enum flag on the accessor and it is
  compile-time. (Enums are currently *skipped* by the scanner at `EquivalencyScanner.cs:51`.)
- `ComparingRecordsByValue` / `ComparingRecordsByMembers` — `INamedTypeSymbol.IsRecord` is a
  compile-time fact; MintPlayer has no notion of records today.
- `WithStrictOrderingFor` / `WithoutStrictOrderingFor` **by path** — path-string matching.
- String/null tuning (`IgnoringCase`, whitespace, newline style, `ComparingNullStringsAsEmpty`,
  `ComparingNullCollectionsAsEmpty`) — runtime flags on already-typed values.
- `Using<T>(IEqualityComparer<T>)` — trivially runtime, no reflection; just a missing overload.

### 6.2 The ceiling — inherently runtime, and why

- **`RespectingRuntimeTypes` in the general case.** It resolves `expectation.GetType()` and asks the
  registry — which only holds types the generator *saw*. A type appearing only through polymorphism
  (derived type instantiated in library code, a plugin, an EF/Castle proxy, a mock) is never
  registered → silent reflection fallback, or an empty member list under trimming. **A source
  generator can only close the world visible in the compilation.** This is the fundamental limit.
- **Anonymous types.** `EquivalencyScanner.cs:71` cannot name them in generated code at all, so
  `BeEquivalentTo(new { Name = "x" })` — FA's most common idiom — is **always** the reflection path.
  A structural limitation of keying the registry by `Type`.
- **Open generics / type parameters.** `EquivalencyScanner.cs:78` bails on anything containing a type
  parameter, so a generic helper `void AssertSame<T>(T a, T b) => a.Should().BeEquivalentTo(b)`
  registers nothing and every instantiation reflects.
- **`IEquivalencyStep` / `IMemberSelectionRule` / `IMemberMatchingRule` / `IOrderingRule` plug-ins**
  and FA's globally mutable `EquivalencyPlan` — third-party code supplied at runtime, by definition
  outside the compilation. Supporting them costs MintPlayer its single-pass design, not its
  accessors. **Recommend rejecting** — this is the architectural fork that turns the library back
  into FA.
- `WithAutoConversion` (`Convert.ChangeType`/`TypeDescriptor`), multidimensional arrays
  (`Array.GetValue(int[])`), XML steps, tracing/`WithFullDump`.

### 6.3 Known engine behaviours worth revisiting

- Depth cut treats deeper nodes as **equal, silently** (`EquivalencyValidator.cs:116`). FA never does
  this. A silent pass is the worst failure mode an assertion library has.
- Cycles are silently treated as equal (`:120`); FA offers a `ThrowException` mode.
- Dictionary comparison is non-generic `IDictionary` only (`:123,:210`), boxing keys and using
  `Convert.ToString` for path text.
- `IsValueLike` is a hard-coded type list (`:407-419`).

---

## 7. Hot-path reflection — admissible only because it is opt-in

The Types/MemberInfo/Assembly/selector family is **entirely absent** (MintPlayer has 20 methods on
`Primitives/TypeAssertions.cs` against FA's ~55 on a 1923-line class, plus FA's `MethodInfo`,
`PropertyInfo`, `ConstructorInfo`, `AssemblyAssertions`, `TypeSelector`, `MethodInfoSelector`,
`PropertyInfoSelector` and `AllTypes`).

For this family the reflection **is** the predicate — `HaveProperty`, `HaveMethod`,
`HaveAccessModifier`, `HaveExplicitMethod` (`GetInterfaceMap`), `AssemblyAssertions.Reference`
(`GetReferencedAssemblies`), `AllTypes.From` (`assembly.GetTypes()`). It cannot be deferred to the
failure path.

**This does not breach the boundary**, because it lives on assertion types nobody else touches: you
pay only if you call it. But two things must hold:

1. **It must not leak into shared infrastructure.** No change to `Assertion`, `AssertionScope`,
   `Formatter` or the equivalency path to accommodate it.
2. **Trimming/AOT must be annotated honestly**, following the `[RequiresDynamicCode]` precedent
   already set by `EventMonitor`. `TypeSelector` in particular is unannotatable under trimming.

Note the generator alternative: `HaveProperty`/`HaveMethod`/`HaveAccessModifier` against a
*statically known* type are compile-time decidable. MintPlayer owns the machinery
(`GenerateAssertionGenerator`, `EquivalencyRegistry` + `[ModuleInitializer]`) and does not currently
point it at reflection assertions. **That is the differentiated answer to this family** and worth a
spike before copying FA's runtime approach.

Also missing and cheap (no reflection, pure API surface): `Stream` assertions
(`BeWritable`/`BeSeekable`/`BeReadable`/`HavePosition`/`HaveLength`/`BeReadOnly`/`BeWriteOnly`) and
`BufferedStream.HaveBufferSize`. XML (`XDocument`/`XElement`/`XAttribute`) is likewise
reflection-free, just absent — decide whether it is in scope at all.

---

## 8. Naming divergences — rename to match FA where FA is not worse

Drop-in FA compatibility is an **explicit non-goal** (`Assertions-prd.md:56-61`), and that stands: the
`Func<>`-over-`Expression<>` divergence (§1) and the extra `Not*` members (§11) are deliberate and
stay. But *gratuitous* divergence — a different name for the same thing, with no design reason — buys
nothing and costs every migrating user a compile error that MPA0100 cannot fix, because it carries no
per-method rename table.

With versions being bumped, rename:

| FluentAssertions | MintPlayer today | Action |
|---|---|---|
| `BeApproximately` / `NotBeApproximately` (numerics) | `BeCloseTo` / `NotBeCloseTo` | **rename** (§3.1) |
| `WithInnerExceptionExactly<T>()` | `WithInnerExactly<T>()` | **rename** |
| `HaveSameCount` / `NotHaveSameCount` | `HaveSameCountAs` / `NotHaveSameCountAs` | **rename**, and add the generic `IEnumerable<TExpectation>` form |
| `WithArgs(predicate, because)` | parameter order differs | **reorder** to match |
| `monitor.Should().Raise("X")` | `monitor.Raise("X")` | **decide** — adding `.Should()` is pure ceremony; keeping the terse form is defensible. If kept, it goes in the README's divergence list |
| `action.ExecutionTime().Should()` | `action.Should().ExecutionTime()` | **keep MintPlayer's** — it is the more consistent entry point (everything else starts at `Should()`). Document it |

Everything renamed above leaves MPA0100 with nothing to map. For the two entries deliberately kept,
either add them to MPA0100's table or list them in the README as known breaks — a migrating user
should never meet an unexplained compile error.

### Bulk membership: `ContainAll` / `NotContainAny`, decided during implementation

The bulk collection and dictionary overloads were first added as more `Contain` / `NotContain`
overloads, copying FA. That shape carries a trap, and FA has it too: `Contain(oneItem)` matches both
the single-item overload and a `params` bulk one in **expanded form only** — neither is applicable in
normal form, because both end in a `params` parameter with no argument — so the tie goes to whichever
needs no defaulted arguments, which is the bulk one. It compiles and reports a collection-shaped
message for what the author wrote as a single-item assertion.

`StringAssertions` had already settled the naming question in this library: `ContainAll` (all
present), `NotContainAny` (none present), `NotContainAll` (at least one missing). The bulk methods now
use it. The ambiguity is gone by construction rather than by a resolution hint, `NotContainAny` says
which of the two negations it means, and the terse `params` form is safe again because
`ContainAll(a, b)` cannot collide with `Contain(x)`.

`[OverloadResolutionPriority]` was considered and rejected: it makes the chosen overload depend on an
attribute the reader cannot see at the call site — the same invisibility that made the boxed
enumerator so hard to find. It is the right tool when renaming is impossible; here it was not.

Also dropped: the generic `HaveSameCount<TExpectation>(IEnumerable<TExpectation>)` overload listed
earlier. The non-generic `IEnumerable` form already accepts every typed collection, so FA call sites
bind to it unchanged and a second overload would only add ambiguity.

---

## 9. Events: the one genuine architectural tension

FA's `EventHandlerFactory` uses **`System.Reflection.Emit`** — a `DynamicMethod` per event signature
emitting IL that boxes args into an `object[]`. That handles *any* delegate shape and is structurally
incompatible with Native AOT and with a generator-first design.

MintPlayer already avoids it: `Delegate.CreateDelegate` against a generic
`EventRecorder.Handle<TArgs>` (`Events/EventMonitor.cs:48-85,219`) — reflection but **not codegen**,
cheaper and AOT-friendlier, with the hole honestly attributed via `[RequiresDynamicCode]`. The cost is
shape coverage: no 3+ parameters, no non-void return, no value-type sender. Unsupported shapes land in
`UnmonitoredEvents` rather than throwing.

Both designs pay reflection **once per monitored event at `Monitor()` time**, not per raise.

**Recommendation: do not adopt `Reflection.Emit`.** Closing the shape gap fully means either losing
AOT or generating handlers at compile time — and the latter only works for types the generator can
see, not `Monitor<IFoo>()` over a runtime implementation. Missing event features that *are* cheap:
the options overload, `MonitoredEvents`, `GetRecordingFor`, multi-predicate `WithArgs`,
interface-declared events (`typeof(T).GetEvents()` misses them today), the weak subject reference,
and the no-events-at-all guard.

---

## 10. `[GenerateAssertion]`: what it cannot do

Worth recording, because it bounds how much of §3–§7 can be generated rather than hand-written. The
generator emits one fixed template — `FailWith("Expected {subject} to <phrase>{reason}, but found
{0}.", assertions.Subject)` — onto one of four buckets (`StringAssertions`, `BooleanAssertions`,
`NumericAssertions<T>`, `ObjectAssertions`).

Impossible under it: custom failure messages; two-stage messages; multiple related failures (the null
guard is folded into the condition with `&&`, so a null subject gets the *generic* message, not "was
null"); returning `AndWhichConstraint` so callers can `.Which`; generic assertions; assertions on a
generic subject type; `Span<T>`/`ref struct` subjects; async predicates; a typed `Should()` entry
point for a domain type; and any control over rendering.

So Phase 2's assertions are hand-written against the `Assert()` seam — which itself lacks FA's
`Given<T>`/`WithExpectation`/reportables/lazy args (§2.3, §5).

---

## 12. MPA0005 — the analyzer the boundary needed

Added during implementation; not foreseen when this document was written.

`foreach` over a variable whose static type is `IReadOnlyList<T>` or `IList<T>` boxes the underlying
struct enumerator. These two loops are character-for-character identical and only the static type
decides which allocates:

```csharp
List<int> a = ...;           foreach (var x in a) { }   // struct enumerator, no allocation
IReadOnlyList<int> b = ...;  foreach (var x in b) { }   // boxed enumerator, one allocation
```

Nothing at the call site says so, which is why review misses it and why a sampled allocation test
only finds the instances it happens to cover. A static rule is exhaustive.

Scoped narrowly on purpose. It fires only for **indexable** interfaces, where an indexed loop is a
free fix; iterating an `IEnumerable<T>` parameter boxes too, but there is often nothing the author
can do, and a rule that cannot be acted on gets suppressed wholesale — taking the actionable cases
with it. It fires only inside `MintPlayer.Assertions`, because consumers iterate interfaces all day
and are right to.

**It also exposed that the library was not running its own analyzers at all.** The `ProjectReference`
to the analyzer project lacked `OutputItemType="Analyzer"`, so it only ordered the build for packing
and every rule — MPA0001 through MPA0004 — was blind to the code it was written to guard.

## 11. Where MintPlayer is already ahead (do not regress these)

- `[GenerateAssertion]` — zero-ceremony, zero-reflection custom assertions. No FA equivalent.
- `AllowingVacuousComparison` + the vacuity guard: a comparison that asserts nothing **throws** rather
  than passing. FA has no equivalent. This is a real class of silently-green test.
- **Analyzers.** The FA checkout ships none. MPA0001 (un-awaited assertion, **error**, with code fix)
  and MPA0003 (undisposed `AssertionScope`) catch two classes of silently-passing test FA has no
  in-repo defence against. MPA0004 warns when an equivalency expectation is erased to `object` and
  silently drops to reflection.
- ~14 `Not*` members FA lacks (`NotContainSingle`, `NotOnlyContain`, `NotStartWith`, `NotEndWith`,
  `NotAllBeOfType`, …).
- `JsonDocument` support (FA covers only `JsonNode`/`JsonArray`).
- `WithParameterName`, `ThrownExceptionTask` (needs only the *new* type argument when chaining
  `WithInnerException`), `WithMessage(StringComparison)`.
- `[CallerArgumentExpression]` subject naming instead of FA's runtime source-file parsing —
  cheaper, AOT-safe, and it makes FA's `[CustomAssertion]` stack-skipping unnecessary by
  construction.
- Allocation-free `WildcardPattern` (two-pointer span matcher) instead of FA's string machinery.

---

## 12. Success criteria

1. **The boundary holds, and is enforced.** Equivalency benchmark gating; new per-assertion
   micro-benchmarks; an allocation test on the passing path. No passing-path regression.
2. All of §2 fixed, with §2.1's API-shape decision recorded.
3. All of §3 shipped.
4. §4 shipped using the named mechanisms, not the naive ones.
5. §6.1 shipped — the equivalency options gap closes to the compile-time-decidable boundary, and
   §6.2 is documented in the README as the deliberate ceiling it is.
6. `IsAotCompatible=true` with zero trim warnings preserved; any new `[RequiresDynamicCode]` /
   `[RequiresUnreferencedCode]` annotation exact (see constraint 8).
7. Repo policy: **one pull request**, tests run once at the end.

## 13. Open questions (need a decision before the relevant milestone)

Backward compatibility is not a constraint, so the questions that were about *whether we may break
something* are closed. What remains is genuine scope and design:

1. **§5** — native test-framework exceptions contradict the v1 non-goal
   (`Assertions-prd.md:49-51`). Revisit or reaffirm? Free at runtime, but it means shipping framework
   detection the v1 PRD deliberately refused.
2. **§7** — build the Types/MemberInfo/Assembly/selector family at all? If yes: copy FA's runtime
   reflection, or spike the source-generated variant first? The latter is the differentiated answer
   and the reason MintPlayer exists, but it is a spike, not a known quantity.
3. **§7** — is XML (`XDocument`/`XElement`/`XAttribute`) in scope, or explicitly out? Reflection-free
   and purely additive, so the only cost is surface area to maintain.
4. **§6.2** — confirm rejecting the `IEquivalencyStep` / `IMemberSelectionRule` plug-in model
   **permanently**, and say so in the README, so it stops reading as an unfinished gap rather than a
   design boundary.
5. **§9** — keep `monitor.Raise(...)` terse, or adopt FA's `monitor.Should().Raise(...)`?
