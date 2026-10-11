# PRD: `[Inject]` constructor — one parameter per service type, base call mapped by type (#196)

> Issue: [#196](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/196) · Regressed in 12.2.0 (#195) · Target release: **13.0.0**
> Decisions locked in a design grill on 2026-10-11 (see "Decisions").

## Overview

Since 12.2.0 the `[Inject]` constructor generator emits the same dependency twice when a derived class and
its base inject a member with the same name and service type but different nullability; the generated
constructor fails with `CS0100` (+ `CS0229`).

The fix redefines how the generated constructor is assembled: **a dependency is identified by its type
only** (`SymbolEqualityComparer`, nullable annotation ignored). Every distinct service type becomes exactly
one parameter, regardless of member names or where in the inheritance chain it is declared. The
`: base(...)` call is built by mapping each slot of the **base constructor's signature** to the derived
parameter of that type, so base classes that declare their dependencies in a different order can never
get swapped arguments.

## Problem Statement

### In the wild
MintPlayer.Spark on 12.2.0: seven classes deriving from `DefaultPersistentObjectActions<T>` (base:
`[Inject] IHttpContextAccessor? httpContextAccessor`) that declare their own
`[Inject] IHttpContextAccessor httpContextAccessor` → CS0100. Workaround in Spark: rename the derived field.

### Root cause (verified by spike)
`Generators/InjectSourceGenerator.cs`, `ComputeParameters` (≈ l.411-416):

```csharp
if (!parameters.Any(p => p.Type == parameter.Type && p.Name == parameter.Name))
    parameters.Add(parameter);
```

`Parameter.Type` is rendered with `ParameterTypeFormat` (l.33, includes the nullable `?`), so
`global::X.IDep?` ≠ `global::X.IDep` and both survive. The deeper flaw: identity is "type text + name", but
the name of a member says nothing about which service it is. 12.1.1 escaped by accident (plain
`FullyQualifiedFormat`, no `?`).

### Verified behaviour inventory (spike against current master) and target

| # | Base / Derived member | Today | Target (13.0.0) |
|---|---|---|---|
| A | `IDep? dep` / `IDep dep` (#196) | `(IDep dep, IDep? dep = default)` → CS0100 | `(IDep dep) : base(dep)` |
| B | `IDep dep` / `IDep? dep` | same → CS0100 | `(IDep dep) : base(dep)` |
| C | `IDep dep` / `IDep dep` | `(IDep dep) : base(dep)` | unchanged |
| D | `IDep dep` / `IOther dep` | `(IOther dep, IDep dep)` → CS0100 | `(IOther dep, IDep dep2) : base(dep2)` |
| E | `IDep dep` / `IDep dep2` | `(IDep dep2, IDep dep) : base(dep)` | `(IDep dep2) : base(dep2)` — **behaviour change** |
| F | `IDep? dep` / `IDep? dep` | `(IDep? dep = default)` | unchanged |

### Base-call ordering today
`BaseConstructorArguments` (l.178) is the base's own parameter **names**, positional
(`InjectSourceGenerator.Producer.cs:48`). Order is correct today because the derived reuses the base's
`Parameter` objects, but nothing pins it with tests, and once parameters merge across names (E, D) a
name list can no longer express the mapping.

Separate mismatch found: `GetBaseParameters` (l.432-437) assumes an in-source base with `[Inject]` members
**will** get a generated constructor, skipping the gates `Discover` applies — `GetUnsupportedReason`
(not `partial`, file-local, nested in non-partial; l.129-144), `HasParameterList` (primary ctor /
positional record; l.146-151) — and always passes `Extensibility.Extensible`. The derived then calls a
constructor that does not exist.

## Decisions (design grill 2026-10-11)

| # | Decision |
|---|---|
| D1 | Identity of a dependency = its **type symbol**: `SymbolEqualityComparer.Default` on `Symbol.WithNullableAnnotation(NullableAnnotation.None)`, compared in the transform stage while symbols are available. Names never decide merging. `int?` (`Nullable<int>`) ≠ `int` stays distinct. No keyed-service support exists, so type is sufficient. |
| D2 | **One parameter per distinct type everywhere**: within a single class, across the base chain, and onto base constructors with several same-typed slots (`base(dep, dep)`). |
| D3 | **Required wins**: if any member/slot of that type is non-nullable, the merged parameter is required and rendered without `?`; it is optional (`= default`, placed last) only when every occurrence is optional. |
| D4 | **Naming**: the merged parameter takes the name of its first occurrence in parameter order (own members before base, so the most-derived declaration wins). A different type whose name is already taken gets the smallest numeric suffix `2`, `3`, … that is free (`dep`, `dep2`). |
| D5 | Parameter order unchanged from 12.2.0: auto `IConfiguration` first, own members in declaration order, then base parameters in the base's order minus types already present, optional last. |
| D6 | The base call is built **by type**: for each slot of the base constructor signature (in the base's order), emit the derived parameter of that slot's type. Positional arguments remain. |
| D7 | No collision diagnostic (former INJECT009 idea dropped — D1/D4 make every collision representable). |
| D8 | No positional-call analyzer: the generator must be correct by construction; tests prove it. |
| D9 | Release as **13.0.0** — D2 changes constructor arity and instance sharing for code that compiled on 12.2.0. |

Worked example (from the grill):

```csharp
class ClassC { public ClassC(IDep1 dep1) { } }

partial class ClassB : ClassC {
    [Inject] IDep2 dep2;
    // generated: public ClassB(IDep2 dep2, IDep1 dep1) : base(dep1) { this.dep2 = dep2; }
}

partial class ClassA : ClassB {
    [Inject] IDep1 dep1;
    // generated: public ClassA(IDep1 dep1, IDep2 dep2) : base(dep2, dep1) { this.dep1 = dep1; }
}
```

## Goals
- G1 Every distinct service type yields exactly one constructor parameter (D1-D3); A, B, D compile.
- G2 `: base(...)` arguments always follow the base constructor's signature, mapped by type, for every
  base shape; a swapped binding fails a test.
- G3 The base-gating mismatch is fixed: the derived always targets a constructor that exists.
- G4 Breaking behaviour is documented in the changelog with migration notes.

## Non-Goals
- Named arguments in the base call (would cause CS0121 vs. a hand-written ctor with the same types in a
  different order, and CS1739 when a hand-written ctor with different names suppresses generation).
- An analyzer for positional calls to generated constructors (D8).
- Keyed services.

## Requirements

**R1 — Type-keyed parameter set.** Rework `ComputeParameters` so parameters are collected into an
ordered set keyed on the nullability-stripped type symbol (D1). Adding an occurrence of an existing type:
apply D3 to optionality and rendered `Type`; keep the first name (D4). Adding a new type whose name is
taken: suffix per D4. The auto `__configuration` (no symbol today — give it the `IConfiguration` symbol)
merges with an explicit `[Inject] IConfiguration`; `IOptions<T>` parameters follow the same rule.

**R2 — Member assignment by type.** Each `[Inject]` member (and `IOptions` field) is assigned from the
parameter of its type, not from a parameter of its own name: `this.otherDep1 = dep1;`.
Model change: `InjectField` (or the assignment list) carries the source parameter name. Strings only —
no Roslyn types in models.

**R3 — Base call by type (D6).** `GetBaseParameters` returns the base constructor's slots in the base's
order (with their type symbols, transform-stage only). `BaseConstructorArguments` becomes the list of
**derived parameter names**, one per base slot, resolved by type. For an in-source `[Inject]` base, its
slots are computed with the same R1 function its own generation uses.

**R4 — Base gating.** `GetBaseParameters` applies the same gates as `Discover` (`GetUnsupportedReason`,
`HasParameterList`, real extensibility) before trusting the computed list for an in-source base; when a
gate fails, fall back to the real constructor symbol. The gates are one shared predicate so both call
sites cannot drift.

**R5 — Metadata base defaults.** Parameters forwarded from a compiled base currently always get
`IsOptional = false` (l.454) even when the base parameter has a default value; use
`p.HasExplicitDefaultValue` (subject to D3). Confirm with a RED test first.

**R6 — `[Config] Uri?` classification.** `ClassifyType` (≈ l.874-893) compares
`underlyingType.ToDisplayString()` (includes `?`) with `"System.Uri"` etc. Spike: if `[Config] Uri? x`
is misclassified, fix by comparing the nullability-stripped symbol / `SpecialType`, with a RED test.

**R7 — Release bookkeeping (13.0.0).** Bump `<Version>` in all 10 lockstep csproj files; run
`eng/Assert-PackageVersions.ps1`. `SourceGenerators/CHANGELOG.md` `## 13.0.0`:
- `### Fixed` — #196 (CS0100 on nullability mismatch), base gating (R4), metadata defaults (R5), R6 if confirmed.
- `### Breaking changes` — (1) same-typed members/base parameters collapse to one parameter: hand-written
  `new X(...)` / `: base(...)` calls lose arguments; (2) members of the same type share one resolved
  instance (matters for transient registrations); (3) a name clash between different types renames the
  later parameter (`dep2`), breaking named-argument callers.
- Back-fill under 12.2.0's entry (or a note in 13.0.0): parameters are ordered by declaration order,
  optional last, `IConfiguration` first; positional calls written against 12.1.x may need reordering
  (e.g. Spark's `SparkSubscriptionWorker<T>`).

## Spikes
- **S1 (done)** — RED test for A; inventory A-F (above). Test is in the spike worktree
  `.claude/worktrees/agent-acf957d44f8a2a57a/SourceGenerators/MintPlayer.SourceGenerators.Tests/Generators/InjectBaseMergeTests.cs`;
  its expected ctor text stays valid under D1-D4. Move it in M1, then remove the worktree.
- **S2 (dropped)** — explicit `IConfiguration` vs `__configuration` is answered by D1/D4.
- **S3 (done, both confirmed)** — a compiled `Base(IMa a, IMb? b = null)` was forwarded as required `IMb? b`; a
  `[Config] System.Uri? endpoint` was emitted as `ConfigurationBinder.Get<global::System.Uri>(...GetSection("Endpoint"))`.
- **S4 (done, confirmed)** — a non-partial (INJECT006) or primary-constructor (INJECT007) base with `[Inject]` members
  made the derived class call `base(dep)` against a constructor that does not exist: an extra CS1503.

## Technical Design
- `Parameter` (l.380, transform-only class) already holds `ITypeSymbol? Symbol`; it becomes the merge key.
  The set is built in `ComputeParameters`; nothing symbol-typed crosses into `Models.*`.
- Model changes (strings only): `Assignments` (`MemberAssignment { Member, Parameter }`) replaces `InjectFields` and
  `OptionsFields` (R2); `BaseConstructorArguments` = derived parameter name per base slot (R3);
  `ConfigurationParameterName` replaces `HasExplicitIConfiguration` + `ExplicitIConfigurationName`. Producer emits them as-is.
- Incremental caching: model equality still value-based; the existing `RunIncremental` / keystroke tests
  must stay green.

## Test Cases (`MintPlayer.SourceGenerators.Tests/Generators/InjectBaseMergeTests.cs`)
Each: `run.Errors.Should().BeEmpty(run.ErrorText)`, assert the ctor text, then `run.Emit()` +
`Activator.CreateInstance` and check **which instance landed in which member** (distinct instances per type).

| ID | Scenario | Expected |
|---|---|---|
| T1 | A: base `IDep?` / derived `IDep` (S1 RED) | `(IDep dep)`; base and derived member get the same instance |
| T2 | B: base `IDep` / derived `IDep?` | `(IDep dep)`, required |
| T3 | F: both `IDep?` | `(IDep? dep = default)` |
| T4 | E: same type, different names across base/derived | one parameter `dep2` (derived's name), both members assigned from it |
| T5 | D: same name, different types | `(IOther dep, IDep dep2) : base(dep2)`; each member gets its own type's instance |
| T6 | Two same-typed members in one class | one parameter; both members get the same instance |
| T7 | **Order:** grill example ClassA/ClassB/ClassC | `ClassA(IDep1 dep1, IDep2 dep2) : base(dep2, dep1)`, `ClassB(IDep2 dep2, IDep1 dep1) : base(dep1)`; runtime: every member holds the right instance |
| T8 | Order, generated base declaring `IB b, IA a`, derived declaring `IA a, IB b` | `: base(b, a)`; runtime check |
| T9 | Order, hand-written base ctor `(IB b, IA a)` (no `[Inject]` in base) | `: base(b, a)`; runtime check |
| T10 | Order, base from a referenced compilation (metadata) with `(IB b, IA a)` | `: base(b, a)`; runtime check |
| T11 | Base slots of the same type: hand-written `Base(IDep x, IDep y)` | `: base(dep, dep)` |
| T12 | Multi-level with optional in the root and `IConfiguration` in the middle; explicit `[Inject] IConfiguration` in the leaf | config first, merged with the explicit member; optional last; runtime check |
| T13 | Generic `Base<T>` with `[Inject] IRepo<T>? repo`, derived `: Base<Car>` with `IRepo<Car> repo` | one parameter, required |
| T14 | Name-suffix stability: three different types all named `dep` across a chain | `dep`, `dep2`, `dep3`, pinned exactly |
| T15 | R4: non-partial base and primary-ctor base with `[Inject]` members | derived targets the real ctor; compiles |
| T16+ | R5/R6 outcomes of S3 | as confirmed |

Run during development: `dotnet test SourceGenerators/MintPlayer.SourceGenerators.Tests --filter "FullyQualifiedName~InjectBaseMergeTests"`.
Full suite once at the end. Existing tests asserting the old two-parameter shape for E-style input are
updated, with the change called out in the commit message.

## Milestones (red → green, one PR)
1. **M1 RED** — S3, S4; add `InjectBaseMergeTests` T1-T15 (+ S3 outcomes). Expect T1, T2, T4, T5, T6, T11,
   T12, T13, T14, T15 to fail for the stated reason; T3, T7-T10 pin already-correct behaviour. Commit.
2. **M2** — R1 type-keyed parameter set + R2 member assignment by type.
3. **M3** — R3 base call by type.
4. **M4** — R4 shared gate predicate.
5. **M5** — R5 / R6 as confirmed.
6. **M6** — R7 version 13.0.0, changelog. Full test sweep, `Assert-PackageVersions.ps1`.

## Migration / Backward Compatibility
- A/B/D compile again; for A the signature equals 12.1.1's.
- Breaking (13.0.0): see R7 "Breaking changes". Spark: drop the field-rename workaround; the
  `DefaultPersistentObjectActions<T>` derivatives get a single `IHttpContextAccessor` parameter.

## Acceptance Criteria
- T1-T15 (+ S3 tests) green; full `MintPlayer.SourceGenerators.Tests` suite green.
- Spark's seven affected classes compile on 13.0.0 without the rename workaround.
- CHANGELOG 13.0.0 lists the fixes, the breaking changes with migration notes, and the 12.2.0 reordering.
