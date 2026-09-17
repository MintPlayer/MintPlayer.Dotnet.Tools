# PRD — .NET 11, and FluentAssertions parity without losing the performance

Branch: `net11-assertions-parity`. Companion plan: [Plan-Net11-Assertions-Parity.md](./Plan-Net11-Assertions-Parity.md).

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
Nearly every hot-path item is an equivalency option.

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
- The 5 independent version lines (`Vidyano.Sdk` 2.0.2, `NestFiles` 1.0.4, `MSBuild.Tasks` 1.0.2,
  `TokenReplacer.Targets` 1.0.0) — framework-agnostic packages, no reason to renumber.
- Updating the README benchmark table with new numbers **until** it is re-measured on .NET 11 on an
  idle machine. The current baseline is a .NET 10 host measurement; attributing a runtime difference
  to our code would be wrong.

---

## 6. Spikes

Each is timeboxed, produces a measurement or a decision, and blocks the milestone that depends on it.

### S1 — Generator-emitted member flags (blocks the equivalency options work)

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

### S2 — A deterministic operation-count gate (blocks M2 layer 3)

**Question.** Can a time-only regression — extra work that allocates nothing — be caught
deterministically, given that no wall-clock gate can be trusted in CI?

**Method.** An internal counter incremented once per `CompareNode` entry and once per `FindByName`.
Assert an **exact** count for a fixed graph. Verify it catches a deliberately introduced O(n²) change
that allocates nothing (e.g. a memoised matrix on the stack).

**Pass.** Exact, reproducible counts; the synthetic regression fails the test.
**Fail.** Fall back to documenting the time-only gap explicitly rather than pretending it is covered.

This is the strongest available answer to "an allocation gate cannot see a pure slowdown", and it
doubles as executable documentation of the walker's complexity.

### S3 — Roslyn folder scheme (blocks packaging)

**Question.** The folders say `roslyn4.0`/`roslyn4.9`; the packages referenced are **5.3.0**; the
`<Choose>` branch that would select 4.x is dead. What should ship for .NET 11?

**Method.** Determine the Roslyn version in the .NET 11 SDK. Decide between reconciling the dead
branch into a real dual build and collapsing the illusion into honest single-version folders. Verify
by packing and loading the generator in both a .NET 10 and a .NET 11 consumer.

⚠️ Five shipped `build/*.props` files also hardcode these folder names; any change must update all of
them, not just `eng/`.

### S4 — Port and re-measure the matcher fix (blocks the correctness fix)

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
