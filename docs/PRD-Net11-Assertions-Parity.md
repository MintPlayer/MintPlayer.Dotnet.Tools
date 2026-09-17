# PRD — .NET 11, and FluentAssertions parity without losing the performance

Branch: `net11-assertions-parity`. Companion plan: [Plan-Net11-Assertions-Parity.md](./Plan-Net11-Assertions-Parity.md).

> **§9 "Traps already paid for" is the most important section here.** Twelve failure modes that
> already happened on this codebase — several shipped green. Read it before writing code.

Supersedes the abandoned `dotnet-11` branch (PR #181), which mixed these goals with unclear scope.
Parts of that branch are *measured, working* code and are named below as porting candidates rather
than as work to redo.

---

## 1. Requirements

| # | Requirement | Source |
|---|---|---|
| R1 | Support .NET 11 | user |
| R2 | Drop support for .NET 8 and .NET 9 | user |
| R3 | Every library that depends on .NET Core — **including `MintPlayer.Assertions`** — gets version `11.0.0-rc.1` | user |
| R4 | Add the missing FluentAssertions features, **without sacrificing performance** | user |
| R5 | Build a performance test so the README benchmark cannot regress | user |

**R4 and R5 are the same requirement stated twice.** The generator exists to make assertions fast;
R4 is only interesting if R5 holds. Where they conflict, R5 wins and the feature is deferred.

---

## 2. THE HARD BOUNDARY

> **The observation in `Assertions/MintPlayer.Assertions/README.md:236` must not regress.**

Measured on this branch, on an idle machine, before any change:

| | README claims | Measured 2026-09-17 | Δ |
|---|---:|---:|---|
| FluentAssertions 7.2.2 | 201.08 µs / 409.14 KB | **204.52 µs / 406.95 KB** | +1.7% / −0.5% |
| MintPlayer.Assertions | 13.08 µs / 20.34 KB | **13.13 µs / 20.34 KB** | +0.4% / **exact** |

**15.6× faster, 20.0× less memory.** BenchmarkDotNet 0.14.0, .NET 10 host, SDK 11.0.100-rc.1,
`Fairness checks passed: generated accessors active`.

> **Status 2026-09-17 — the boundary was not merely held, it moved.** After every milestone the same
> benchmark, re-run net11-vs-net11, measures **15.25 µs / 6.16 KB** against
> FluentAssertions' 276.91 µs / 397.04 KB — **18× faster, 64× less memory**. The README table has
> been updated to these numbers, which means **the gate is now set against the improved figure, not
> the original one**: a change that returns the library to 14.83 KB/op, let alone the original 20.34, is now a regression. That is
> deliberate. Per-milestone detail is in `Plan-Net11-Assertions-Parity.md` § STATUS.
>
> **An unplanned confirmation of §2's premise.** Across four runs this library allocated the same figure
> to the hundredth of a KB every time, while FluentAssertions allocated 404.26 KB and then 397.04 KB. Bytes
> reproduce exactly *here* because nothing on the passing path allocates conditionally — that is a
> property this code earned, not one every library has, and it is the reason a byte-exact gate works
> at all. The timings across six runs spanned 9.29–15.25 µs and 150.55–276.91 µs; the README
> quotes the less flattering run whole rather than the best figure from each.

Two facts make this usable as a gate:

1. **Allocation reproduces to the byte.** 20.34 KB both times. Bytes are a property of the emitted IL
   and the object graph, not of the machine — they survive a loaded CI runner unchanged.
2. **Time does not.** The same benchmark on a loaded machine measured FluentAssertions at 335 µs and
   235 µs in two consecutive runs — a 40% swing on identical code. Any absolute wall-clock threshold
   is therefore either flaky or too loose to catch anything.

### The operational rule

> **Nothing may be added to the passing path.**

A passing assertion must not allocate more, reflect more, or branch more than it does today. This is
sharper than "don't regress the benchmark" and it is the rule a contributor can apply while typing.

**Corollary 1 — failure-path features are free.** `Assertion.FailWith` returns immediately when the
condition holds (`Execution/Assertion.cs:60`), so formatting never runs in a green suite.
**Corollary 2 — own-type features are free to everyone else.** `HaveMethod` on `TypeAssertions` is
hot-path reflection, but only for the caller who invoked it.

---

## 3. What the investigation found

Three parallel investigations. Every claim below was verified against the source, and the four most
consequential were re-verified independently.

### 3.1 The headline claim is protected by nothing

- **No allocation test exists.** `grep -rn "GetAllocatedBytesForCurrentThread"` → **0 hits**, repo-wide.
- **The benchmark never runs in CI.** All three workflows do `restore → build → dotnet test`.
  `dotnet test` does not discover a BenchmarkDotNet console app, so `MintPlayer.Assertions.Benchmarks`
  is compiled and never executed on any branch or PR.
- **The library does not run its own analyzers.** `MintPlayer.Assertions.csproj:39` references the
  generator without `OutputItemType="Analyzer"`, so its rules ship to consumers and never see its own
  code. This is why MPA0004 has been ignorable.
- **Only `BeEquivalentTo` is benchmarked at all** — 2 benchmark methods, nothing else. Every string,
  numeric, collection and exception assertion is unmeasured and therefore unprotected.

### 3.2 There is a live correctness bug on master

`EquivalencyValidator.cs:261-281` matches unordered collections with **greedy first-fit**:

> *"Greedy bipartite matching: each expectation item claims the first unmatched subject item it is
> fully equivalent to."*

Expectations `[A, B]` against subjects `[X, Y]` where `A` matches both and `B` only `X`: greedy gives
`X` to `A`, strands `B`, and **reports a difference between two equivalent collections**. This ships
today and is independent of any performance concern.

### 3.3 The passing path already violates the boundary

None of these were introduced by this work; all are on master:

| Violation | Scale |
|---|---|
| `FailWith(string, params object?[])` is the only overload — array built at the call site, value types boxed into it, discarded when the assertion passes | every `FailWith` call |
| `AndConstraint<T>` is a **class** | ~360 assertion methods |
| `Items`/`Pairs` do `[.. Subject]`, copying an already-materialised array or `List<T>` | every collection/dictionary assertion |
| `foreach` over interface-typed collections boxes the struct enumerator | **30 sites**, 3 in the walker |
| `EquivalencyOptions` eagerly builds **7 collections** per call whether used or not | every `BeEquivalentTo` |
| `IsExcluded`'s wildcard loop has no `Count == 0` guard, so it boxes an enumerator per member | ~2× per member per node |

The worst boxing site is `FindByName` (`:421-428`) — a linear scan called once per expectation member
per node, boxing an enumerator each time.

### 3.4 Every shipped package is in the `--skip-duplicate` trap

`dotnet-build-master.yml:78` publishes with `--skip-duplicate`. **All 45 packable projects currently
carry a version that is already published on nuget.org**, and in every case it is the latest.
Merging master today runs green and **publishes nothing**.

This makes R3 load-bearing rather than cosmetic: a PR that changes 90 target frameworks and no
`<Version>` ships nothing, silently, with a green checkmark.

### 3.5 Four framework references live outside any csproj

These break at *runtime* or *deploy* time, not at build time:

| Location | Problem |
|---|---|
| `Verz/MintPlayer.Verz/Program.cs:83` | Hardcodes `lib/net10.0` when loading a downloaded plugin. If that TFM is ever dropped from the plugin packages, `verz` throws `FileNotFoundException` with no build error anywhere. |
| `Beid/DemoWebApp/Dockerfile:1,5` | `aspnet:10.0` / `sdk:10.0`. If the csproj moves to net11.0 and these do not, CI is green and the **container crashes on start**. |
| `Sdks/MintPlayer.Vidyano.Sdk/Vidyano/Vidyano.props:9` | Ships `<CommonTargetFramework>net9.0</CommonTargetFramework>` **to consumers** — already out of support. |
| `Sdks/.../Vidyano.props:10` | Ships `<CommonLangVersion>13</CommonLangVersion>` to consumers. |

### 3.6 The Roslyn packaging story is not what it looks like

`sourcegenerator.targets:16` declares `<RoslynVersion></RoslynVersion>` — **empty**, and nothing sets
it. The `<Choose>` always takes the `Otherwise` branch, so there is **one compilation**, copied into
both `analyzers/dotnet/roslyn4.0/cs` and `roslyn4.9/cs`. The multiplexing is a packaging illusion.

Worse: every generator csproj then overrides the Roslyn packages to **5.3.0**, while the folders still
advertise `roslyn4.0`. A generator built against 5.3.0 and advertised under `roslyn4.0/cs` cannot load
in a genuine Roslyn 4.0 host. Spike S3 addresses this.

### 3.7 The strategic finding: parity is a generator problem

MintPlayer implements **12 of FluentAssertions' ~62** equivalency options. Of the ~50 missing:

- **~30 need only compile-time facts the generator already has** — member name, member type,
  accessibility, `[EditorBrowsable]`, explicit-interface status, is-enum, is-record, is-string,
  is-collection. These can be emitted as flags on `MemberAccessor` and selected with a **bitwise test**
  at walk time.
- **~14 genuinely need runtime reflection** — `IncludingAllRuntimeProperties`,
  `PreferringRuntimeMemberTypes`, predicate-based `Excluding`/`Including`, auto-conversion, and the
  four `Using(IRule/IStep)` extensibility hooks.
- **4 are diagnostics** (`WithTracing`, `WithFullDump`, …) and are FREE-ON-FAILURE.

Across the whole surface the gap classifies as **20 free-on-failure, 95 own-type, 63 hot-path**.
Nearly every hot-path item is an equivalency option. Roughly 100 of the own-type items are the
Types/MemberInfo/Assembly/selector family, which §5 cuts — so the surface actually in scope is closer
to **20 free-on-failure, ~40 own-type, 63 hot-path**.

**This is the design that makes R4 and R5 compatible.** The generator is not merely what makes the
library fast — it is what makes feature parity affordable, because a flag baked into emitted metadata
costs a bitwise AND where FluentAssertions pays reflection.

---

## 4. Strategy

1. **Migrate and version first** (R1–R3), so the rest lands on a .NET 11 baseline and the release
   actually ships.
2. **Build the gate before the features** (R5). The gate is what lets R4 proceed without argument.
3. **Fix what the gate finds** — the pre-existing violations in §3.3 are the first thing it will
   report, and they must be fixed before any feature is added on top.
4. **Then add features, cheapest class first**: free-on-failure → own-type → hot-path.
5. **Every hot-path item is measured, not argued.** A feature that moves the allocation numbers does
   not land until it does not.

### The reflection policy

Two kinds of reflection exist in this library and conflating them is how the previous attempt went
wrong. The rule, in priority order:

1. **Never in the equivalency walker.** This is what the generator exists to eliminate and where the
   15× lives. The reflection fallback already there is silent — a type the scanner skips is compared
   correctly and 15× slower with no signal — so anything that *widens* its reach is a regression even
   when it allocates nothing.
2. **Never per-node, per-member, or per-assertion**, cached or not. A `ConcurrentDictionary` lookup is
   not free when it runs once per node.
3. **Acceptable only when the assertion *is* the reflection question** — `HaveProperty("Name")` cannot
   be answered any other way — and only on an assertion type nothing else touches.

⚠️ The lesson from the previous attempt is precise and worth stating: the isolated `Reflection/`
folder was **not** the problem. It was verified to touch nothing shared. The damage was two reflection
calls that leaked into `EquivalencyValidator` itself — `IsRecord`'s `GetMethod("<Clone>$")` and
`IsGenericDictionary`'s `GetInterfaces()`. Both were cached; one was correctly short-circuited behind
an option check and one ran per collection node. **Watch the walker, not the folder names.**

### Rejected outright

- **`IEquivalencyStep` / `IMemberSelectionRule` / `IOrderingRule` plug-ins.** A virtual call per node
  on the passing path is exactly the cost this library exists to avoid. `Using<T>(…)` covers the case
  that comes up. To be documented as a permanent boundary, not a gap.
- **`[ValueFormatter]` assembly scanning.** A reflective type lookup a trimmer defeats silently.
- **Test-framework exception detection by probing loaded assemblies.** Same reason; a registration
  seam says it explicitly instead.
- **`Reflection.Emit` for event handler shapes.** Costs AOT.

---

## 5. Scope

### In

- Every project to .NET 11; .NET 8 and .NET 9 dropped (exceptions in §7).
- Every .NET Core library to `11.0.0-rc.1`.
- The four out-of-csproj references in §3.5.
- A four-layer performance gate (plan M2).
- The greedy-matching correctness fix (§3.2), with its allocation cost measured.
- The six passing-path violations in §3.3.
- FluentAssertions parity in the order set by §4.

### Out

- **`MintPlayer.Assertions` keeps no `net8.0` leg.** Decided explicitly: consumers on net8.0 — the
  current LTS, supported until Nov 2026 — stay on 1.1.0. This reverts the deliberate net8.0/net9.0
  test legs tracked as R4.1/M13 in `docs/PRD-TestCoverage.md:327`, and contradicts the product
  reasoning recorded in `Assertions/MintPlayer.Assertions/README.md:30`. Recorded here so the decision
  is traceable rather than silent.
- **The SourceGenerators `10.20.x`–`10.22.x` version line is not swept.** Those are `netstandard2.0`
  Roslyn components; they do not depend on .NET Core, so R3 does not reach them by its own wording.
  128 published versions of minor history would be discarded for no benefit.
- No `net8.0` anywhere, **including `MintPlayer.SourceGenerators.Testing`**, which moves from its
  deliberate `net8.0` pin to `net10.0;net11.0`. Decided explicitly after the trade-off was put:
  .NET 8 is deprecated and consumers on a net8.0 or net9.0 test project use the previous version.
  ⚠️ Two consequences to expect rather than rediscover: NuGet will not roll *forward* to a newer TFM,
  so a net8.0 or net9.0 test project referencing the new version gets `NU1202: package is not
  compatible`; and the csproj comment explaining the net8.0 pin (the `IsExternalInit` polyfill and the
  CS0433 collision it avoids) must be rewritten rather than left contradicting the file.
- **The whole Types / MemberInfo / Assembly / type-selector family — ~100 members — is cut.** This is
  FluentAssertions' architecture-test surface: `HaveProperty`, `HaveMethod`, `AllTypes.From(asm)
  .ThatImplement<I>().Should().BeSealed()`, `assembly.Should().NotReference(other)`, plus
  `MethodInfoSelector`, `PropertyInfoSelector` and their assertion types.

  Cut for three reasons that compound: (a) it is the **one family where the reflection policy above
  cannot be honoured**, because the assertion *is* the reflection question; (b) it **cannot be source
  generated even in principle** — a generator needs a compile-time target and the `Type` here comes
  from a runtime assembly scan — so it is the least aligned with what this library is for; and (c) it
  is the single largest block of the gap, for the surface least connected to the performance story.

  Architecture testing is also a different product, served properly by NetArchTest and ArchUnitNET.
  Revisit only if a user asks, and then as a separate package so it cannot touch this one's hot path.
- The 5 independent version lines (`Vidyano.Sdk` 2.0.2, `NestFiles` 1.0.4, `MSBuild.Tasks` 1.0.2,
  `TokenReplacer.Targets` 1.0.0) — framework-agnostic packages, no reason to renumber.
- Updating the README benchmark table with new numbers **until** it is re-measured on .NET 11 on an
  idle machine. The current baseline is a .NET 10 host measurement; attributing a runtime difference
  to our code would be wrong.

---

## 6. Spikes

Each is timeboxed, produces a measurement or a decision, and blocks the milestone that depends on it.

### S1 — Generator-emitted member flags (blocks the equivalency options work) ✅ RESOLVED

**✅ RESOLVED — implemented, with the pass condition met in substance but not to the letter.**

`MemberTraits` (Property, Field, NonPublic, NonBrowsable, ExplicitInterface) is emitted by
`EquivalencyScanner` onto every `MemberAccessor`, mirrored in `ReflectionMemberProvider`, and
`IncludingInternalMembers` is wired end to end.

**The design that made it cheap: two tables, not one mask.** Members excluded by default live in a
second registration (`RegisterExtendedAccessors`) and are concatenated on demand, cached per
(type, traits). The default walk is handed the *same array* it was handed before traits existed, so
`CompareMembers` does no filtering and `FindByName` — O(members²) per node — does not grow because a
type happens to have internal members nobody asked about. The obvious alternative, one table plus a
`(traits & mask)` test in `CompareMembers`, puts that cost on every comparison in every suite to
serve options that are off by default. `TraitsDoNotChangeTheDefaultWalk` pins this.

**Measured: 13,976 → 13,992 B/op with the option unset. +16 bytes per comparison, not per node.**
That is two object-size roundings — one field on `EquivalencyOptions`, one on the walker's `Context`
— and it is the honest cost of the option existing at all. The spike said "byte-identical"; this is
0.11%, and no per-node cost, so it is being taken as a pass. Node and member-lookup counts are
unchanged at 133/112.

**It did not start there.** The first working version cost **2,768 B/op — a 20% regression** from a
capturing lambda sitting below an early return; see §9.15, which is the more useful half of this
spike. Getting from 16,744 to 13,992 is what produced the tight byte gate that now sits beside the
100 KB smoke alarm.

**Completeness, and why it matters.** The generator cannot emit an accessor for a member it may not
reference — an `internal` member of another assembly without `InternalsVisibleTo`, or any `protected`
member, since the generated registration is a namespace-level static class. Rather than compare a
smaller set for those types, the type's extended table is marked incomplete and the runtime falls
back to reflection for any request that wants those traits. Correct, ~15× slower, and charged only to
comparisons that opt in.

**The ⚠️ in the original spike was the right warning and is now enforced by a test.**
`MemberTraitTests` compares the generated table against the reflection table member-for-member and
trait-for-trait, on a type the generator actually scans — `TheGeneratorRegisteredThisTypeAtAll` is a
separate test precisely so that a registration failure cannot quietly turn the parity tests into
reflection-against-itself. The agreed rules: `private`/`private protected` are returned by neither
(the generator physically cannot), explicit interface implementations are returned by neither yet,
and `[EditorBrowsable(Never)]` is a trait rather than an exclusion.

**Open for M5c:** the remaining ~29 compile-time-decidable options now have the mechanism they were
blocked on. `ExplicitInterface` is defined but unemitted on both sides — deliberately, so the two
stay in agreement — and emitting it needs a second accessor shape (a cast to the interface).

**Question.** Can the ~30 compile-time-decidable options be driven from flags emitted onto
`MemberAccessor`, selected with a bitwise test, without moving the benchmark?

**Method.** Add a `MemberTraits` flags enum (field/property, non-public, non-browsable,
explicit-interface). Emit from `EquivalencyScanner`. Filter in `CompareMembers` with `(traits & mask)`.
Wire **one** option end to end (`IncludingInternalMembers`). Measure allocation before and after on the
benchmark graph.

**Pass.** Byte-identical allocation with the option unset; the filter is branch-only.
**Fail.** Any measurable cost → the flags move behind an opt-in, as `RespectingRuntimeTypes` is today.

⚠️ The scanner change must be mirrored in `ReflectionMemberProvider`. A trait the generator emits and
the fallback does not means the same option behaves differently depending on whether a type happened
to be scanned — worse than not having the option.

### S2 — A deterministic operation-count gate (blocks M2 layer 3) ✅ RESOLVED

**✅ RESOLVED — in favour of shipping the counters.** `EquivalencyDiagnostics` carries `[ThreadStatic]`
`Nodes` / `MemberLookups` / `MatchProbes`, and `EquivalencyWalkerGateTests` pins them at 133 / 112 / 20
for a fixed graph. It earned its cost immediately: `AnAlignedCollectionCostsOneProbePerItem` is what
makes the M4 matcher fix safe to attempt, and the counters caught the parallel-test interference trap
(§9.9) within minutes of being written. **Open question for review:** they add a static-bool read per
node to shipped code. Measured as no detectable cost, but if that is unacceptable the whole of
`EquivalencyDiagnostics` can be compiled out behind a symbol — the gate tests then go with it.

**Question.** Can a time-only regression — extra work that allocates nothing — be caught
deterministically, given that no wall-clock gate can be trusted in CI?

**Method.** An internal counter incremented once per `CompareNode` entry and once per `FindByName`.
Assert an **exact** count for a fixed graph. Verify it catches a deliberately introduced O(n²) change
that allocates nothing (e.g. a memoised matrix on the stack).

**Pass.** Exact, reproducible counts; the synthetic regression fails the test.
**Fail.** Fall back to documenting the time-only gap explicitly rather than pretending it is covered.

This is the strongest available answer to "an allocation gate cannot see a pure slowdown", and it
doubles as executable documentation of the walker's complexity.

### S3 — Roslyn folder scheme ✅ RESOLVED

**Outcome: one build at Roslyn 5.0.0, shipped as `analyzers/dotnet/roslyn5.0/cs`.**

Measured the compiler in each installed SDK rather than trusting the folder names:
`.NET 10.0.112 → Roslyn 5.0.0`, `.NET 10.0.401 → 5.9.0`, `.NET 11 rc.1 → 5.11.0`.

Two findings followed. **A 4.x/5.x dual build would be unreachable**: now that net8.0/net9.0 are
gone, no SDK both builds our TFMs and runs a 4.x compiler, so the 4.x flavour could never be
selected. And **the 5.3.0 pin was arbitrary** — the whole solution compiles unchanged against 5.0.0,
so it excluded the oldest supported SDK for nothing.

The old arrangement was the worst available: advertising `roslyn4.0`/`roslyn4.9` told a 5.0.0 host
"this folder is for you" and then handed it a 5.3.0-built assembly, which fails with CS8032. A host
below the advertised version now finds no folder and skips the generator instead.

Also removed: the `<Choose>` on `$(RoslynVersion)` in `eng/sourcegenerator.targets` **and** in the
shipped `sourcegenerator_tools.props` — a property declared empty that nothing ever set, identical
package versions in both arms, and `ROSLYN_4_*` constants no source file tests. All five shipped
`build/*.props` moved with it; fixing only `eng/` would have left downstream generator authors
packing into a folder this repo no longer ships. 263 tests pass, including the E2E that installs a
freshly-packed nupkg into a net11.0 consumer.

### S3 — original question (kept for the reasoning)

**Question.** The folders say `roslyn4.0`/`roslyn4.9`; the packages referenced are **5.3.0**; the
`<Choose>` branch that would select 4.x is dead. What should ship for .NET 11?

**Method.** Determine the Roslyn version in the .NET 11 SDK. Decide between reconciling the dead
branch into a real dual build and collapsing the illusion into honest single-version folders. Verify
by packing and loading the generator in both a .NET 10 and a .NET 11 consumer.

⚠️ Five shipped `build/*.props` files also hardcode these folder names; any change must update all of
them, not just `eng/`.

### S4 — Port and re-measure the matcher fix (blocks the correctness fix) ✅ RESOLVED

**✅ RESOLVED — the two-phase design holds on this branch.** Greedy first-fit is kept as a pre-pass
(the only phase an aligned collection runs, ~n comparisons and one `bool[]`), handing off to Kuhn's
augmenting paths over a lazily filled, memoised grid only when greedy strands an expectation. The
aligned-collection probe count is exactly 20 for the benchmark graph — pinned, not bounded — and the
walk is unchanged by the fix. The eager-grid version this replaces measured 1,027,288 B/op against
13,824; that number is now written above the loop rather than in this document.

Reproducing the bug needs a NON-TRANSITIVE equivalence, which was not obvious up front: with a
uniform item type the candidate graph is a disjoint union of complete bipartite blocks and greedy is
accidentally optimal, so a typed array cannot exhibit the bug at all. The tests use `object[]` for
that reason, and say so.

**Question.** The `dotnet-11` branch has a lazy + greedy-pre-pass matcher measured at 13,640 B/op
against an eager implementation's 1,027,288 B/op. Does that hold on this branch?

**Method.** Port it. Measure on the benchmark graph. Confirm the maximum-matching correctness test
(the case greedy gets wrong) passes, and that an already-aligned collection costs ~n probes rather
than n².

**Why a spike and not just a task.** Maximum matching is a correctness fix whose obvious
implementation is quadratic. The generalisable rule, which belongs in the contributor guidance:
**any change that replaces "first acceptable answer" with "best answer" turns a linear number of
subtree comparisons into a quadratic one unless the comparisons are lazy and the common case is
short-circuited first.**

---

## 7. Open decisions

| # | Decision | Recommendation |
|---|---|---|
| D1 | `PackAsTool` projects (`SlnLaunch`, `Solve`) cannot usefully multi-target — two TFMs produce two tool folders | Single-target `net11.0`; tools are installed deliberately, reach matters less |
| D2 | `Verz.Targets` / `FolderHasher.Targets` pack a **single-TFM path** into `build/`; multi-targeting breaks the pack expression | Keep single-target; follow the `MSBuild.Tasks` netstandard2.0 precedent if VS hosting is ever needed |
| D3 | `Verz/Program.cs:83`'s hardcoded `lib/net10.0` | Probe the `lib/` folders rather than hardcode; user previously said Verz would be handled differently |
| D4 | `LangVersion` — 42 projects pin `14`, 2 pin `latest`, 44 unpinned | `14` → `15` is safe; **deleting** the pin breaks the 10 netstandard2.0 projects, which default to C# 7.3. Normalise the two `latest` pins. |

---

## 8. Success criteria

1. Every project builds on .NET 11; **no `net8.0` or `net9.0` remains anywhere**, with no exceptions.
2. Every .NET Core library is at `11.0.0-rc.1`, **verified against nuget.org** so the release is not
   silently skipped.
3. The README:236 observation is reproduced on .NET 11 and is **no worse** than the §2 baseline.
4. The gate exists, runs in `dotnet test`, and demonstrably fails when a regression is introduced —
   proven by introducing one deliberately.
5. Greedy matching is replaced by maximum matching, with the allocation cost measured.
6. The FluentAssertions gap closes in the §4 order, each hot-path item measured.
7. `IsAotCompatible=true` with zero trim warnings; every suppression's diagnostic id checked **against
   a build**, not guessed.

---

## 9. Traps already paid for

**Read this section before writing code, not after.** Every item below actually happened during the
previous attempt at this work (branch `dotnet-11`, PR #181) or during the investigation that produced
this PRD. Each cost real time, and several shipped green. They are written as rules because the
war story is not the useful part.

### 9.1 Correctness fixes that go quadratic

**What happened.** Greedy first-fit collection matching was replaced with maximum bipartite matching —
a genuine correctness fix. The obvious implementation builds the candidate grid up front. Each cell is
a **full recursive subtree comparison**, so a 20-item collection went from ~20 subtree walks to 400.
Measured: **1,027,288 B/op against 13,824 B/op** for the same graph under `WithStrictOrdering`.

**The rule.** *Any change that replaces "first acceptable answer" with "best answer" turns a linear
number of subtree comparisons into a quadratic one, unless the comparisons are lazy and the common
case is short-circuited first.* Laziness there is not an optimisation; it is the difference between
shippable and not. The fix needed **both** a lazily-filled memoised grid **and** a greedy pre-pass,
with augmenting paths run only on what greedy could not place.

**Where it can recur.** `CompareMultisets` is the closest twin: it is the O(n) hash path for
value-like items, and the moment anyone makes it honour a custom comparer, a case-insensitive string
option or a float tolerance — all *correctness* improvements, all of which break hashing — the obvious
fallback is pairwise matching, a silent O(n)→O(n²) cliff on collections of primitives. Also at risk:
"show me which items are missing" failure reporting, `FindByName` if the member set ever widens, and
`SatisfyRespectively` if it ever has to decide which inspector matches which item.

### 9.2 Reflection in the walker, not the folder names

**What happened.** An isolated `Reflection/` folder looked like the risk and was verified to touch
nothing shared. Meanwhile two reflection calls leaked into `EquivalencyValidator` itself —
`IsRecord`'s `GetMethod("<Clone>$")` and `IsGenericDictionary`'s `GetInterfaces()`. Both were cached,
which made them look harmless; one ran **per collection node**.

**The rule.** Judge reflection by *where it executes*, never by which folder it lives in. A cache does
not make a per-node call free. See the reflection policy in §4.

### 9.3 The `because` parameter swallows anything string-shaped

**What happened, three separate times:**
- `Contain(params KeyValuePair[])` hijacked single-pair `Contain(key, value)` calls.
- `WithArgs`'s `[CallerArgumentExpression]` parameter ate a positional `because` argument.
- `HaveAttribute(name, value)` lost overload resolution to `HaveAttribute(name, because)`.

Every assertion ends `(…, string? because = null, params object?[] becauseArgs)`. That tail is
compatible with almost anything, the compiler picks a candidate silently, and **neither the compiler
nor code review can see that the wrong one won**. Each was fixed by a distinct method name
(`ContainAll`, a reordered signature, `HaveAttributeWithValue`).

**The rule.** A new parameter that could bind to a string does not belong beside `because`. Give it a
different method name. When adding any overload to an existing assertion, write a test that asserts
the *old* call shape still binds where it did.

### 9.4 Indexing an interface is NOT the fix for a boxed enumerator

**What happened.** `foreach` over an `IReadOnlyList<T>` boxes the struct enumerator. The obvious fix —
an indexed `for` loop over the same interface — was applied to 27 sites and reported as free. It is
not: every indexer call and every `Count` read is an un-inlinable interface dispatch. Measured over
100k loops of 8 items:

| | time | allocated |
|---|---:|---:|
| `foreach` over `IReadOnlyList<int>` | 22.1 ms | 4,000,040 B |
| indexed `for` over the interface | **30.2 ms** | 40 B |
| type-check, then `ReadOnlySpan<T>` | **10.6 ms** | 40 B |
| array subject via span | **5.0 ms** | 40 B |

**The rule.** Iterate a `ReadOnlySpan<T>` — from the array directly, or `CollectionsMarshal.AsSpan`
on a `List<T>`. Test the **array case before the `List` case**: an array is not a `List<T>`, so
testing only for the list silently drops every `T[]` onto the copying path, and arrays are what test
code writes. The analyzer's message must say this, or it will keep prescribing the slower fix.

### 9.5 A bulk regex rewrite introduces bugs the compiler cannot see

**What happened, twice.** A blanket rename across the collection assertions replaced a local named
`actual` that held a *value* found at a dictionary key with the whole `Subject`, so
`ContainKeyAndValue` stopped reporting what was actually there. It compiled. Review missed it. Only
the test suite caught it.

**The rule.** Prefer compiler- or analyzer-verified transformations over pattern rewrites in this
codebase. If a bulk rewrite is unavoidable, read every hunk, and run the tests before the commit —
not at the end of the milestone.

### 9.6 "Done" means a grep says the symbols exist

**What happened.** Two milestones were marked ✅ with **four items never built**. The gap was found
later by grep during a documentation pass, not by the milestone's own review.

**The rule.** A milestone is done when a grep proves each named symbol exists and a test exercises it.
"It felt finished" is not a completion criterion.

### 9.7 Reasoning where a measurement was available

**What happened, twice.** Indexed loops were described to the user as "a free fix" without measuring
(§9.4 shows they are 1.4× slower). Later, an audit of the new `Reflection/` folder came back clean and
was reported as reassurance — while a 75× allocation regression sat in the walker, in code written two
milestones earlier and never measured.

**The rule.** When a measurement is cheap and available, take it before making a claim. Allocation is
measurable in seconds with `GC.GetAllocatedBytesForCurrentThread()` and is deterministic even on a
loaded machine. "This should be fine" is how §9.1 shipped.

### 9.8 Renames scoped to one directory, in a repo that dogfoods itself

**What happened.** A rename inside `Assertions/` broke `Math/MintPlayer.Math.Tests` — the whole repo
uses this assertion library. A single-project build was green; CI was not.

**The rule.** Any rename in `MintPlayer.Assertions` requires a **full-solution Release build** before
pushing, not a project build.

### 9.9 Process-wide configuration and parallel tests

**What happened.** Tests that mutate `Formatter.Options`, the formatter registry or
`AssertionConfiguration.ExceptionFactory` ran beside every other test under xUnit's default
parallelism, so unrelated assertions failed with whatever renderer or exception type was installed at
that instant. Not a flake — two tests sharing one process-wide setting.

**The rule.** Any feature that adds process-wide configuration requires
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, or the configuration must be
async-local — and async-local defeats the purpose here, because a failing assertion usually has no
scope. Decide which, deliberately, when the feature is designed.

### 9.10 Suppressions with guessed diagnostic ids suppress nothing

**What happened.** Two trim suppressions cited `IL2070` where the build wanted `IL2090` and `IL2075`.
The v1 plan records the same mistake, which meant the "AOT-clean" claim was not being enforced at all.

**The rule.** Every `[UnconditionalSuppressMessage]` id is copied from a build's actual output. Never
from memory, never from the neighbouring suppression.

### 9.11 A green publish that publishes nothing

**What happened.** See §3.4 — all 45 packages sit at an already-published version and master pushes
with `--skip-duplicate`. In the previous attempt `MintPlayer.Assertions` was left at 1.1.0 through an
entire feature PR; merging would have shipped none of it, green.

**The rule.** Bumping the version is part of the change, not a release chore. Verify against
nuget.org before merge.

### 9.12 Packaging tests that pass against a stale package

**What happened.** The packaging suite used a constant version (`99.9.9-packtest`) in the shared
global packages folder, so NuGet never re-extracted and the tests validated a previously packed
artifact. They passed while the real package was wrong.

**The rule.** A packaging test must evict its version from the global packages folder, or use a unique
version per run. If a packaging test has never failed, confirm it *can*.

### 9.13 `x is null` on an unconstrained generic allocates

`items[i] is null` where `T` is unconstrained compiles to `box !T` followed by a null test. Over an
`int[8]` that is **192 B/op** — 24 B per item — to establish eight times that an `int` is not null.
The source reads as an ordinary null check, there is no analyzer for it, and no amount of review
finds it; `PassingPathAllocationTests` did, and only because the test happened to use `int[]`.

Two things make it worse than it first looks:

1. **The obvious guard has the same bug.** Writing `if (default(T) is null)` inline does not reach
   zero — the guard boxes too. It measured 192 → 24 B/op, which looks like a fix in a changelog and
   is not one. It has to be a `static readonly bool` on the generic type (`ItemsCanBeNull`,
   `KeysCanBeNull`), so the box happens once per closed generic type at type initialisation.
2. **A gate only covers the instantiations it names.** `NotContainNulls` over `string[]` allocates
   nothing and always did. The whole family is invisible unless a test exercises a **value-type**
   instantiation on purpose. Any new allocation gate over a generic assertion should pick a value
   type deliberately, not whichever element type reads nicely.

Same trap, same session, third site: `TryGetValueForKey`'s `key is not null`.

### 9.14 Reading a materialising property "for safety" at the top of a method

Six dictionary methods opened with `var pairs = Pairs;` and never read the local. `Pairs` copies the
subject — a `Dictionary<,>` is neither an array nor a `List<>`, so there is nothing to hand back as a
span without one — and all six answer through `TryGetValueForKey`, which goes straight at the
dictionary's own `TryGetValue`. Cost: 56 B/op for a two-entry dictionary, proportional to size for a
real one, on the passing path of `ContainKey` and five others.

It survived review because it reads as setup, and because every other method in the file legitimately
starts the same way. The general shape: **a property whose name is a noun can still be the most
expensive line in the method.** When a property materialises, say so in its own doc comment — nobody
reads the getter before using it.


### 9.15 A capturing lambda allocates on paths that never reach it

`EquivalencyRegistry.TryGetAccessors(type, wanted, out members)` returns on its **first line** for the
default case. Far below that early return sat a `GetOrAdd(key, k => …)` whose lambda captured two
locals. That cost **2,768 B/op** on the benchmark graph — 13,976 → 16,744, a 20% regression — because
the compiler allocates the closure's display class at **method entry**, for the whole enclosing
scope, on every call, including the ones that return before the lambda exists.

Three things make this worth writing down:

1. **The operation counters were identical before and after** — 133 nodes, 112 member lookups, both
   times. No extra work was done. The walk simply allocated an object it never used, twice per
   structural node. An operation-count gate is structurally blind to this, which is precisely the
   complement of the case S2 was built for.
2. **The 100 KB smoke alarm shrugged at it.** A 20% regression passed every gate in the repo. There
   is now a tight byte bound (`TheDefaultWalkStaysUnderItsMeasuredByteCost`) alongside it; a loose
   bound and a tight bound catch different things and the loose one alone is not enough.
3. **The fix is mechanical**: `static` lambda plus the `GetOrAdd(key, factory, state)` overload, or
   move the body to its own method. The rule for this codebase: **no capturing lambda in a method on
   the equivalency hot path**, however cold the branch it sits in looks.

### 9.16 A bisect edit that survives into the "restored" state

Finding 9.15 took a bisect: revert to HEAD, re-apply one file at a time, measure each step. One of
those steps `sed`-ed the two `GetMembers` call sites to a literal `MemberTraits.None` to isolate the
option, and the backup used to restore the full change was taken **after** that edit. The restore
therefore brought back a version with the feature disconnected — and the next measurement, 13,992
B/op, was of code where the option did nothing.

The tests caught it (`IncludingInternalMembersTests` failed immediately), which is the only reason
the number was not reported as the feature's cost. **A measurement taken during or after a bisect is
worthless until the functional tests pass on the same build.** Measure only after green, never
between.

Corollary: back up the working tree **before** the first bisect edit, not part-way through, and
prefer `git stash` over `cp -r` — a stash cannot silently contain a debugging edit made after it was
taken.


### 9.17 The failure message's path, built for every comparison that succeeds

The walker threaded its position through the recursion as a string and rebuilt it at every step:
`$"{path}.{member}"` once per member per node, `$"{path}[{i}]"` per collection item, `$"{path}[?]"`
per match probe. On the benchmark graph that is **6,848 bytes — 49% of the entire comparison** — and
a passing comparison reads none of it, because a path is only needed to report a difference or to
test a configured exclusion.

Replaced by `PathStack`: a push/pop stack of `PathSegment` on the walk's context, rendered only when
something asks for the text. **13,992 → 6,808 B/op**, with the node, member-lookup and probe counts
unchanged and all 958 tests passing untouched.

Three things worth keeping:

1. **It was found by measuring, not by reading.** The attribution took one experiment — replace the
   concatenation with a constant, re-measure — and gave the answer in under a minute. Reading the
   method would have shown a string concatenation that looks entirely ordinary.
2. **The first design did not compile, and the reason is not obvious.** A `ref struct` chained
   through the recursion by a `ref` field needs no stack at all; C# rejects it, because a ref field
   cannot refer to a ref struct (CS9050). The note is in `PathStack` so nobody re-derives it.
3. **The `IsExcluded` checks now test the CONFIGURATION before rendering the path.** Getting that
   order wrong hands the entire win straight back, because that method runs twice per member on
   every node and almost no comparison configures an exclusion.

**Push and pop must stay balanced**, which is why `CompareNode` splits into a push/try/finally
wrapper around a `CompareNodeCore`. An early `return` added to the middle of the walk unbalances the
stack, and the symptom is not a crash — it is silently wrong paths in later failure messages.

### 9.18 An initial capacity that costs more than the growth it avoids

`PathStack`'s segment list looked like an obvious candidate for `new List<PathSegment>(16)`: growing
from empty reallocates at 4, then 8, then 16, so pre-sizing trades three allocations for one. It
measured **worse** — 6,808 → 6,848 B/op on the walk and 1,168 → 1,360 on the per-comparison fixed
cost — because a 16-element array of a 16-byte struct is 256 bytes charged to every comparison,
including the shallow ones that are most of them. The doubling growth only ever reaches the depth
actually used.

The general rule: **a capacity hint is a guess about the common case, and the common case is usually
smaller than the worst case you were picturing.** Measure it like any other change; "obviously
fewer allocations" is not the same as fewer bytes.


### 9.19 An interpolated failure template is built before anything decides it is needed

`Assert().ForCondition(ok).BecauseOf(…).FailWith($"… {occurrence} …", …)` reads as "report this if it
failed". It is not: the interpolated string and every `ToString()` inside it are evaluated at the
**call site**, as arguments, before `FailWith` can look at the condition. A passing assertion builds a
message nobody will ever read and throws it away.

Measured on three assertions written in one sitting: **288 B/op** on `Contain(item, Exactly.Twice())`,
**272** on `HaveCount(AtLeast…)`, **136** on `BeReadable()`.

**The rule: a template that is interpolated must be inside an `if`.** A constant template is fine —
`FailWith("Expected {subject} …", value)` allocates nothing when the condition holds, because the
arguments are already-existing references and the formatting happens inside. The moment a `$` appears
in front of the template, the whole call needs guarding.

This is the same shape as §9.15 one level up: the cost is not in the branch you are reading, it is in
getting to it.

### 9.20 A struct dictionary key without `IEquatable<T>` boxes on every lookup

`MemberSelection` is two enum fields and is used as part of a `ConcurrentDictionary` key, on a path
that exists precisely to be a cache hit. Without `IEquatable<T>`, `EqualityComparer<T>.Default` falls
back to `ObjectEqualityComparer`, which **boxes both operands on every comparison**: **96 B/op** on a
comparison using `ExcludingFields`.

Worse than the cost is that a comment in that very file asserted the opposite — that the compiler's
structural equality made it free. The compiler does generate correct value equality; it does not
generate `IEquatable<T>`, and `EqualityComparer<T>.Default` cannot use what is not declared.

**The rule: any struct used as a dictionary or set key implements `IEquatable<T>` and overrides
`GetHashCode`.** And a belief about allocation that has not been measured does not belong in a
comment, because the next reader will trust it.

### 9.21 Collections created in field initialisers, for options nobody set

`EquivalencyOptions` built **nine** collections in its field initialisers, so every `BeEquivalentTo`
call allocated all nine whether or not a single option was used — and the overwhelming majority of
calls use none. It sat as a written-down, unaddressed note for a long time because the cost was small
next to the walk.

Adding two more for the M5c options is what finally made it visible: the walk went 6,808 → 6,984
B/op and the byte gate refused the change. Making all nine lazy (`(field ??= new(…)).Add(x)`, with a
shared empty sentinel on the read side) took the walk to **6,360** — below where it started — and the
per-comparison fixed cost from 1,168 to 720.

Two things generalise:

1. **A small waste that is paid on every call is a budget, not a rounding error.** It stayed
   unaddressed because each instance was cheap; nine of them were a quarter of the fixed cost.
2. **The conversion has a hazard the compiler will not catch on the write side.** `ComparingByValue`
   and `ComparingByMembers` each *remove* from the opposite set, and removing from a set that was
   never created is a `NullReferenceException`, not a no-op. Two existing tests caught it. When
   making a collection lazy, grep for every use, not just the ones that add.

### 9.22 An extension method is invisible until its namespace is imported

The string-collection assertions were first written in `MintPlayer.Assertions.Collections`, beside
every other collection assertion. They did not compile at the call site. Instance assertions do not
care which namespace they live in; **extension methods do**, and this library's README promises that
one `using MintPlayer.Assertions;` covers everything.

They live in the root namespace now, as `AssertionScope` already does for the same reason — and that
file carries a comment explaining it, which is how the fix was found.

**The rule: any new extension method on the public surface goes in the root namespace**, whatever
folder its file sits in. A test that exercises it from a file with only the one `using` is what
proves it.
