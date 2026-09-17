# Plan — .NET 11, and FluentAssertions parity without losing the performance

Companion to [PRD-Net11-Assertions-Parity.md](./PRD-Net11-Assertions-Parity.md). Branch
`net11-assertions-parity`. Repo policy: **one pull request**; milestones are commit boundaries inside
it.

Two standing rules:

- **Nothing may be added to the passing path.** M2 exists to make that enforceable before any feature
  lands, and every later milestone is measured against what it establishes.
- **Measured, not argued.** A hot-path change is done when the numbers say so. "It should be fine"
  is how the eager candidate matrix shipped at 75× its intended allocation.

On testing: the repo batches test runs to the end. The exception here is **any milestone that touches
the equivalency walker** — M2, M3, M6 — where the allocation gate is the point of the change and
running it is how the change is verified at all.

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

## M4 — Correctness: maximum matching (S4)

Replace greedy first-fit with maximum bipartite matching, implemented lazily with a greedy pre-pass.
Both halves are required: greedy alone is not a maximum matching (the bug), and eager matrix
construction is quadratic in full subtree comparisons (the 75× regression).

Add the test for the case greedy gets wrong — expectations `[A, B]`, subjects `[X, Y]`, `A` matching
both and `B` only `X`.

---

## M5 — Features, cheapest class first (R4)

**M5a — free-on-failure (20 items).** Formatter extensibility with explicit registration (no assembly
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

**M5c — hot-path (63 items), gated on S1.** Chiefly the ~46 missing equivalency options. The ~30
compile-time-decidable ones ride on generator-emitted flags; the ~14 needing runtime reflection go
behind an explicit opt-in so they never cost a test that does not use them.

⚠️ **The naming trap.** Every assertion ends `(…, string? because = null, params object?[] becauseArgs)`
and that tail swallows anything compatible with it. A new parameter that could be a string does not
belong beside `because` — it belongs in a differently named method. This bit three times in the
previous attempt, each time invisible to the compiler and to review.

---

## M6 — Verify

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

## Risk register

| Risk | Mitigation |
|---|---|
| A feature lands that regresses the benchmark | M2 before M5; every hot-path item measured |
| The release silently publishes nothing | M1 step 3, and a CI guard |
| A "correctness fix" goes quadratic | S4's rule, and Layer 2's strict-vs-unordered assertion |
| A type silently falls onto the reflection fallback, costing 15× with no signal | Extend `GeneratedAccessorInvariantTests`; consider raising MPA0004 to Warning |
| The generator and the reflection fallback disagree about a new trait | S1's explicit warning: mirror every scanner change in `ReflectionMemberProvider` |
| Container crashes on start after a green CI run | M1 step 4 — Dockerfile and csproj move together |
