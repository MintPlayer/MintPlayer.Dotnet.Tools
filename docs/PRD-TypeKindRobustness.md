# PRD: Generators and analyzers survive every type kind (#193, #194 and the same bug classes elsewhere)

## Status

**Implemented (M0–M9), shipping as 12.2.0.** Every finding below is fixed except G12 (one hint name per type), which
was rejected by design (see the G table). The CHANGELOG entry is in `SourceGenerators/CHANGELOG.md`; all 11 csproj
under `SourceGenerators/` are at 12.2.0.

Deviations from the design as written:
- **MINT008.** The Warning-severity MINT002 ("only one of `Equals(object)`/`GetHashCode()` declared") became its own
  id, MINT008, so one id has one severity. MINT002 is Info only. Suppressions of the warning must move to MINT008.
- **Description skips `file` types without a diagnostic.** A summary on a file-local type has no partial to land in;
  the generator emits nothing for it rather than reporting.
- **Inject generates for value types.** S3 suggested rejecting structs; the five-kind requirement won. Value types
  chain `: this()`; the README covers `default(S)`, `with` and record equality.
- **EqualityProducer keeps its own preamble** (header after `#nullable`) instead of `Producer.WritePreamble`, so the
  equality snapshots stay byte-identical.
- **Release tracking.** Releases before 12.1.1 were never tracked, so each package's `AnalyzerReleases.Shipped.md`
  records a 12.1.1 baseline of the rules actually in the published 12.1.1 nupkg. OPTIONS002 shipped there, so its
  removal is a "Removed Rules" entry; MINTCLI001 (shipped since 10.1.0) moved to Shipped; Tools got tracking for
  MPSG001/MPSG002.
- Found in M8 and fixed: a nested property of a type mapped with an assembly-level prefix called `MapTo{Type}()`,
  which no longer exists once the prefix is honoured (P19).
- Found in the pre-PR review and fixed: CONFIG005 (a `DefaultValue` the field cannot hold) and OPTIONS004 (`[Options]`
  with `[Config]`) were declared and shipped but never reported; both are now. `[Inject]` on a property with a setter
  was skipped silently; it is INJECT008 (Warning). `[Config]` defaults now apply on nullable fields and `Uri` fields,
  work for enums of every underlying type, and are written in the invariant culture.
- Installation measured, not inferred: all four generator packages (SourceGenerators, Mapper, ValueComparerGenerator,
  CliGenerator) depend on their Attributes package, but they are development dependencies, so `dotnet add package`
  writes an `IncludeAssets` list without `compile` that also filters the dependency; the attributes are CS0246 until
  the Attributes package is referenced explicitly. The
  Attributes and index READMEs that said the generator "brings it along" now say to install both.
- Found in the final pre-PR pass and fixed: a `RootNamespace` that is not a valid namespace name (the SDK passes a
  project named `My-App` through as `My-App`) broke every generator that writes `namespace {RootNamespace}`; Tools
  now sanitizes it once (`StringExtensions.ToSafeNamespace`, applied in `Settings` and `Producer`). `[Options]` with
  `[ConnectionString]` on one member was the one marker pair without a diagnostic; it is OPTIONS005.

Every finding, including those discovered during implementation, has its outcome under
[Findings](#findings) and [Findings during implementation](#findings-during-implementation).

Final test sweep. The first three rows are from the last run, after the RootNamespace and OPTIONS005 fixes
(`1b4e8b1`). The E2E-category packaging tests, NewtonsoftJson tests and the TestProjects build are from the M9 sweep
(`f32cb71`); `1b4e8b1` changed no packaging.

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| MintPlayer.SourceGenerators.Tests (excluding E2E) | 1040 | 0 | 0 |
| MintPlayer.SourceGenerators.Tools.Tests | 307 | 0 | 0 |
| MintPlayer.Assertions.SourceGenerator.Tests | 82 | 0 | 0 |
| MintPlayer.SourceGenerators.Tests, E2E (`PackagingTests`) | 17 | 0 | 0 |
| MintPlayer.ValueComparers.NewtonsoftJson.Tests | 15 | 0 | 0 |

The six projects under `SourceGenerators/TestProjects` build.

## Overview

This PRD covers two issues and two rounds of investigation:
- **Round 1:** #193, plus an audit of every generator for its bug class.
- **Round 2:** #194, plus a sweep of every other generator for #194's patterns.

Everything lands in one PR. Shared plumbing (D0) goes in first, so the fixes are written against it instead of being
duplicated per generator.

Issue [#193](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/193): `GenericMethodSourceGenerator` throws
`InvalidCastException` on any attributed method inside a `record`, `record struct`, `struct` or `interface`, reports
CS8785, and emits nothing for the whole compilation. A `[GeneratedRegex]` method in a record, which has nothing to do
with this generator, is enough.

Four investigators then audited every generator, analyzer, code fix and shared helper under `SourceGenerators/` for the
same bug class:
- a crash reachable from unrelated or half-typed user code;
- an assumption that the user's type is a `class`.

Every finding below was reproduced by a probe test against master `920a111` unless it is marked SUSPECTED.

The work also adds one requirement. **Wherever a generator emits a `partial` of a user type, it works for all five
kinds: (1) `class`, (2) `record class`, (3) `record`, (4) `struct`, (5) `record struct`.** Each cell has a test. A
generator that cannot support a kind reports a diagnostic. It does not silently do nothing.

All packages under `SourceGenerators/` are versioned in lockstep. This work ships as **12.2.0** (see Versioning).

## Problem statement

The bug class has three shapes.

1. **Crash → the generator contributes nothing.** An exception in a predicate or transform becomes CS8785, and every
   output of that generator vanishes for the compilation. An exception in an analyzer becomes AD0001. A stack overflow
   cannot be caught: it kills csc/VBCSCompiler or the IDE's analyzer host.
2. **Class-only predicate → silent no-op.** The generator matches only `ClassDeclarationSyntax`. A `record` carrying the
   attribute gets no output and no diagnostic, and the `[Inject]` fields stay `null` at runtime.
3. **Hard-coded `class` keyword → CS0261.** Widening a predicate without fixing the producer turns shape 2 into a
   compile error.

The test harness also hides shape 1 for analyzers (H1 below). That is why none of these crashes was caught earlier.

## Findings

### Crashes

| ID | Where | Trigger | Outcome | Package |
|---|---|---|---|---|
| **C1** (#193) | `GenericMethodSourceGenerator.cs:21, :31` | Any attributed method in a record / record class / record struct / struct / interface. The attribute need not resolve. | InvalidCastException → CS8785, no output | MintPlayer.SourceGenerators |
| **C2** | `ServiceRegistrationsGenerator.cs:307` | `[assembly: ServiceRegistrationConfiguration(DefaultAccessibility = )]` mid-typing, or an unknown enum member | NullReferenceException in a `CompilationProvider.Select` → CS8785, no registrations | MintPlayer.SourceGenerators |
| **C3** | `ServiceRegistrationsGenerator.Producer.cs:134/141/151/158/264` (`lifetimeNames[svc.Lifetime]`) | `[Register((ServiceLifetime)7)]` | KeyNotFoundException → MPSG001. `ServiceMethods.g.cs` is not produced, so valid registrations are lost too. | MintPlayer.SourceGenerators |
| **C4** | `CliCommandSourceGenerator.cs:327` (`ToOptionDefinition`) | `[CliOption(null)]` (legal: `params string[]` receives null) | NRE on a default `ImmutableArray` → CS8785, no Cli output | MintPlayer.CliGenerator |
| **C5** | `UnusedUsingsAnalyzer.cs:51-54` (`usingDirective.Name.ToString()`) | `using Pair = (int, int);` or `using unsafe P = int*;`. `Name` is null for a type alias. | NRE → AD0001; MP001 is lost for the file | MintPlayer.SourceGenerators |
| **C6** | `RoslynTypeInModelAnalyzer.cs:53-101` (`TryFindRoslynType`) | `class Foo<T> { Foo<Foo<T>>? Next => null; }` used by a `[GenerateEquality]` model | **Stack overflow.** It kills the compiler / analyzer host, so the whole build fails. | MintPlayer.ValueComparerGenerator |
| C7 (SUSPECTED) | `Tools/Extensions/LocationExtensions.cs:28-43` (`ToLocation`) | Two trees with the same path, or a stale `LocationKey` | `text.Lines[...]` out of range inside `ReportDiagnostics`, with no try/catch → CS8785 | Tools (in Inject, ServiceRegistrations, Mapper, ValueComparer) |
| C8 (SUSPECTED, dead) | `ClassNamesSourceGenerator.cs:38-69` | Global-namespace field | `namespaceDeclaration.Name` null. The provider is never wired to an output, so it never runs. | MintPlayer.SourceGenerators |

**Outcome: all fixed.** C1 in M3 (discovery through `ForAttribute`). C2 and C3 in M4b; C3 is now REGISTER004 (undefined
lifetime) instead of a lost file. C4 in M5. C5 in M4b. C6 in M7, plus a depth guard that turns any future unbounded walk
into AD0001. C7 in M2 (`ToLocation` bounds-checked; `ReportDiagnostics` never throws and falls back to MPSG002). C8
deleted in M4b.

### Type-kind gaps: silent no-op, or a wrong keyword

| ID | Where | Trigger | Outcome |
|---|---|---|---|
| **K1** | `GenericMethodProducer.cs:33` | Once C1 is fixed: `[GenericMethod]` in a non-class type | Hard-coded `partial class` → CS0261 |
| **K2** | `InjectSourceGenerator.cs:23` (predicate), `InjectSourceGenerator.Producer.cs:~85` (keyword) | `[Inject]` in a record / record class / struct / record struct | No constructor, no diagnostic, fields stay null |
| **K3** | `ServiceRegistrationsGenerator.cs:17` | `[Register(Scoped)] public partial record Svc` (attribute is valid on records) | Dropped silently |
| **K4** | `CliCommandSourceGenerator.cs:18` (predicate), `:441` (`BuildDeclaration` hard-codes `class`) | `[CliRootCommand] public partial record RootCommand` | Nothing generated, no diagnostic. Admitting records alone would give CS0261. |
| **K5** | `Tools/Extensions/SymbolExtensions.cs:36-48` (`GetPathSpec`) | Partial type nested in an **interface** | The interface parent is skipped and `AllPartial` stays true. Output lands at namespace level as a *new* type: silent for Description, CS1061 for Inject, CS0117 for Cli. Callers: GenericMethod, Description, Inject, Cli. |
| **K6** | `MapperGenerator.Producer.cs:119, 147` | Mapping where either side is a `struct` / `record struct` | `if (input is null) return default;` → CS0037 |
| K6' | `MapperGenerator.Producer.cs:105, 133` | Struct destination in `MapTo(this S input, D output)` | Copies into a by-value copy: a silent no-op (certain from C# semantics; not probed) |
| **K7** | `ValueComparerGenerator.Discovery.cs:77` | `[GenerateEquality] ref partial struct S` | `obj is S` → CS8121 |
| **K8** | `InterfaceImplementationAnalyzer.cs:28` (`TypeKind != Class`) | `struct S : IThing` / `record struct` with an extra public member | INTF001 never runs on value types |

**Outcome: all fixed.** K1 in M3, K2 in M4a, K3 in M4b, K4 in M5, K5 in M2 (interface parents in `GetPathSpec`) and in
every caller, K6/K6' in M6 (null guard only for reference types; the copy overload is skipped for a value-type
destination, per S4). K7 is MINT007 (M7). K8 in M4b, kept after S5.

### Other wrong output found along the way (same PR, per the one-PR rule)

| ID | Where | Trigger | Outcome |
|---|---|---|---|
| W1 | `DescriptionSourceGenerator` | Partial type split over 2 files, each with `<summary>` | Two `[Description]` → CS0579 |
| W2 | `DescriptionSourceGenerator` | `partial class W<T>` | Emits a non-generic `partial class W`, a separate type. Silent. |
| W3 | `DescriptionSourceGenerator`, `CliCommandSourceGenerator` | `file partial class` | Emits a non-`file` partial, a separate type: silent (Description), CS1061 (Cli) |
| W4 | `InjectSourceGenerator` (and base lookup `~:68`, `.FirstOrDefault()`) | Partial class split over 2 files, `[Inject]` in each | Two constructors (CS0111 when same types; otherwise each sets half the fields) |
| W5 | `ServiceRegistrationsGenerator` | `[Register]` partial class split over 2 files | Registration emitted twice |
| W6 | `ServiceRegistrationsGenerator` | `[Register]` on a private nested class | CS0122 in generated code; should be a diagnostic |
| W7 | `ClassNamesSourceGenerator` | User class named `ClassNames` in the root namespace; class named `@event` | CS0101/CS0542; `const string event` parse errors |
| W8 | `MapperGenerator.cs:249-251` → `MapperGenerator.Producer.cs:452` | `[GenerateMapper]` taking the error path (e.g. `typeof()` mid-typing) | `MapperEntrypoint.g.cs` gets 10–80 syntax errors: the entrypoint doesn't filter `HasError` |
| W9 | `MapperGenerator.Producer.cs:510-512, 549-551` | Mapped base class plus mapped derived class | `case Base` before `case Derived` → CS8120 |
| W10 | `MapperGenerator.cs:80` | Interface or abstract destination | `new I()` → CS0144, no diagnostic |
| W11 | `MapperGenerator.cs:179-188` | `[MapperConversion]` in `static class Conv<T>` | CS0246 'T' |
| W12 | `CliCommandSourceGenerator` (via `GetPathSpec`; `AllPartial` ignored) | Command nested in a non-partial class | CS0260 on the user's type |
| W13 | `CliCommandSourceGenerator.Producer.cs:325` | Two root commands named `Tool` in different namespaces | `ToolCliExtensions` duplicated → CS0101/CS0111 |
| W14 | `UnusedUsingsAnalyzer.CodeFix.cs:107` (`KeepNoTrivia`) | `#nullable enable` (or a header / `#region`) above an unused first using | The fix deletes the directive, which silently changes nullable semantics |
| W15 | `InterfaceImplementationAnalyzer.CodeFix.cs:172-205` | `class G<T> : IThing { public T Get() … }` | The fix writes `T Get();` into the interface → CS0246/CS0738 |
| W16 | `InterfaceImplementationAnalyzer.Members.cs:135-144` (`SatisfiedNames`, by name) | Interface has `DoIt()`, class adds `DoIt(int)` | Overload never reported; the fix would pick the wrong overload |
| W17 | `ValueComparerGenerator` | Model with an error-typed base or property | Extra CS0120/CS0246 in generated code (noise on already-broken code) |

**Outcome: all fixed, one with a deviation.** W1–W3 in M4b, except that Description skips a `file` type silently
instead of reporting (deviation; for Cli W3 is MINTCLI006, M5). W4 in M4a. W5 in M4b. W6 is REGISTER003 (M4b). W7 in
M4b. W8 and W9 in M6. W10 is MAPC005, W11 is MAPC007 (M6). W12 is MINTCLI005, W13 is MINTCLI009 (M5). W14 in M4b. W15:
the fix declines such a member (M4b). W16 in M4b (members match by signature). W17: such models are skipped (M7).

Minor issues found by reading the code only, out of scope unless the spikes show they are cheap: Cli ignores options
declared on a base class, and Mapper's copy overload for an init-only record emits an empty body. **Outcome:** the
Mapper one was fixed in M6 (the copy overload is not emitted when nothing can be copied). Cli still ignores options on
a base class; it was not taken on.

### GenericMethod rework (#194)

Each #194 item was checked by a probe against master `920a111`: 45 cases.

**G0 — the headline finding, not in the issue: no consumer can use this generator.**
- `GenericMethodAttribute` is declared only inside the generator assembly (`GenericMethodSourceGenerator.cs:79-84`).
- The published 12.1.x packages ship that assembly only under `analyzers/`, so `[GenericMethod]` in user code is
  CS0246.
- Every test fixture declares its own copy of the attribute. Three of them (`XmlDocAndGenericMethodTests.cs:166`,
  `IncrementalOutputCachingTests.cs:200`, `FixedFileSetGuardTests.cs:101`) add a positional `(int count)` constructor
  that the real attribute lacks.
- `ItSkipsANonNumericCount` is misnamed: `Count = 3` generates 3 overloads.

| ID | #194 item | Verdict | Evidence (`Gen` = generator, `Prod` = producer) |
|---|---|---|---|
| G1 | A bare `[GenericMethod]` (default Count) generates nothing | TRUE | `Gen:42` needs `Arguments.Count > 0`. `Count = 0` emits an empty file. |
| G2 | Count is parsed from syntax text; `Arguments[0]` is read whatever its name | TRUE | These all give 0 overloads, silently: `Count = MaxArity`, `2 + 1`, `(uint)3`, `3 /*c*/`, and `Transformer = typeof(X), Count = 3`. |
| G3 | Keyword is always `class` | TRUE | = K1 |
| G4 | Always `public`, so CS0262 | TRUE | `Prod:30`. Fails for `internal` and for a nested `private` type. Omitting the modifier compiles. |
| G5 | Generic containing type | TRUE, worse than stated | `Foo<T>` gets a separate non-generic `partial class Foo`, which self-binds → CS0411. The static path `Prod:63` (`ClassName.M`) has the same problem. Shadowing a containing `T1` is only CS0693, a warning. |
| G6 | Return value dropped | TRUE | `Prod:51` hard-codes `void` and never returns. `bool`, `Task` and `ValueTask<T>` are discarded silently; an `async` method gives CS4014 in generated code. |
| G7 | Method shape not validated | TRUE | Shapes that work: collections of `object` (`object[]`, `List<object>`, `IEnumerable<object>`, `IReadOnlyList<object>`, `ReadOnlySpan<object>`, `params`). Broken: `string[]` and `int[]` (CS0029), `object` (CS9174), no parameters or 2+ parameters (CS0411), generic `Add<T>(IEnumerable<T>)` (CS0411). |
| G8 | Silent skips | TRUE, plus 2 more holes | Non-partial or public: nothing generated, nothing reported. Two holes: an implicitly-private method (no modifier) is skipped silently, and `private protected` is accepted (`Gen:52` checks for the keyword, not the accessibility). |
| G9 | `Transformer` unused | TRUE | Only commented-out code references it. |
| G10 | Over-broad predicate; `GetTypeByMetadataName` per attribute | TRUE | = C1; D2 fixes it. |
| G11 | `typeof(...).FullName` | PARTLY | The string is correct. The real defect is G0. |
| G12 | One hint name per type | **Rejected** | Contradicts `SourceGenerators/CLAUDE.md` ("a generator emits a fixed set of files"; per-type names caused CS0016 in 12.0.0), which `Guards/FixedFileSetGuardTests` enforces. Keep one `GenericMethods.g.cs`. |
| G13 | Header and namespace written per method | TRUE | Two classes give 2 headers and 2 namespace blocks. |
| G14 | `\n` and the space in `>( ` | TRUE | Visible symptom: 4 LF next to 46 CRLF, and the closing `}` lands at column 0. |
| G15 | `#nullable enable` | PARTLY | Not needed today. It is needed once G6 forwards annotated return types. |
| G16 | Overload-resolution trap | TRUE, at runtime | The original is private and the overloads are public, so an outside `b.Add(list)` receives a 1-element collection. |
| G17 | (extra) escaped identifiers | SUSPECTED | `MethodName = symbol.Name` drops `@` (e.g. `@event`). |

**Outcome: all fixed in M3 (`533c5f2`) except G12, rejected.** G0/G9: the attribute ships in the Attributes package;
`Transformer` is dropped. G1/G2: `Count` is the compiler's constant, default 4. G4 and G8: GENMETHOD001 (type not
extendable) and GENMETHOD002 (not private, by `DeclaredAccessibility`); a `Count` outside 2..16 is GENMETHOD004. G5:
overload type parameters come from `UniqueTypeParameterPrefix`. G6: results are returned. G7: GENMETHOD003. G15: a
`where Tn : notnull` constraint when the collection's elements are non-nullable `object`. G16: no arity-1 overload
(S8). G17: `@`-escaped names.

**GenericMethod decisions.** (As planned, then settled by S8 and M3; the final shape is given with each item.)
- **G0, G9:** move the attribute to `MintPlayer.SourceGenerators.Attributes` (namespace
  `MintPlayer.SourceGenerators.Attributes`). Drop `Transformer`. Point every test fixture at the real type. This breaks
  nobody, because no consumer could reference the attribute. *Planned:* `public uint Count { get; set; } = 1` only.
  *Decided instead (S8):* `GenericMethodAttribute(int count = 4)` plus a settable `int Count`, so both
  `[GenericMethod(3)]` and `[GenericMethod(Count = 3)]` work, and a bare `[GenericMethod]` emits arities 2 to 4.
- **G16:** emit overloads only for arity ≥ 2, plus a README note on when to prefer C# 13 `params` collections. That
  removes the trap at its source. A `Count = 1` attribute then produces nothing useful, so the range is 2..16. *Planned
  as an open question:* keep arity 1 behind an opt-in flag instead, as #194 implies. *Decided (S8):* arity 1 is dropped
  with no opt-in; it also hijacked calls from inside the class.
- **G5 (naming):** *planned:* `T1` → `TArg1`, or suffixed until unique. *Decided (S1):* per-index suffixing is not
  enough (`T1` → `T11` collides at arity 11), so the overloads take a whole prefix from
  `PathSpec.UniqueTypeParameterPrefix("T")`: `T1..Tn`, or `TArg1..TArgn` (extended until no `{prefix}{n}` is in scope).
- **G7:** the valid shape is exactly one non-ref parameter whose type a collection expression can build from `object`
  elements. Anything else is a diagnostic.
- **New diagnostics** (in `GenericMethodSourceGenerator.Rules.cs`, tracked in `AnalyzerReleases.Unshipped.md`):
  - GENMETHOD001: the type or a containing type is not partial.
  - GENMETHOD002: the method is not `private` (checks `DeclaredAccessibility`, not the keyword).
  - GENMETHOD003: wrong parameter shape.
  - GENMETHOD004: Count out of range. The cap is 16, matching `Func`/`Action`. Measured: 128 overloads take about
    1.65 s to bind and produce 175K characters.
- **Producer:**
  - one file, with the header and `#nullable enable` written once;
  - one block per type, through D0.3;
  - no accessibility modifier on the type;
  - type parameters and constraints on the containing type;
  - method type parameter names that don't collide with any in scope (shipped as `UniqueTypeParameterPrefix`, see G5
    above);
  - `return` whenever the method is not void;
  - `WriteLine` throughout.

### #194 patterns in the other generators

| ID | Generator | Where | Trigger → outcome | Status |
|---|---|---|---|---|
| P1 | Inject | `InjectSourceGenerator.Producer.cs:48-76` | A nullable `[Inject] IDep?` before a non-nullable one → `IDep dep = null, IDep dep2` → CS1737 | CONFIRMED |
| P2 | Inject | `Producer.cs:126` | Public class with an `[Inject]` of an internal type → `public` ctor → CS0051 | CONFIRMED |
| P3 | Inject | `InjectSourceGenerator.cs:695-701, 792-799, 850-856` | `[Config(Keys.Db)]`, `[Config("a:"+"b")]`, `[ConnectionString(Keys.C)]` → a false "key cannot be empty" (CONFIG002/CONNSTR001); `[Options(Keys.S)]` silently gets a null section. The keys are parsed from syntax text. | CONFIRMED |
| P4 | Inject | `InjectSourceGenerator.cs:907` | `OptionsField.Section` is stored and never read. `[Options("Email")]` binds nothing, although README:194 implies it does. | Read + grep |
| P5 | Inject | `Producer.cs:162-163` | `[PostConstruct] async Task InitAsync()` → fire-and-forget plus CS4014 in generated code | CONFIRMED |
| P6 | Inject | `InjectSourceGenerator.cs:185, 202, 447, 567` | Matches the attribute by simple name, so a user's own `Other.InjectAttribute` triggers generation | CONFIRMED |
| P7 | Inject | `:44`, `Producer.cs:84-87` | Non-partial class, or a non-partial parent → CS0260 on the user's type, with no diagnostic of the generator's own | CONFIRMED |
| P8 | Inject | `Producer.cs:35` | `Inject.g.cs` has no `<auto-generated>` header | Read |
| P9 | ServiceRegistrations | `ServiceRegistrationsGenerator.cs:447-487`, `Producer.cs:281-288` | `Repo<T>` and `Cache<TItem>` (same arity) merge into one `AddX<T>` that mentions `TItem` → CS0246 | CONFIRMED |
| P10 | ServiceRegistrations | same | `Map<TValue,TKey> : IMap<TKey,TValue>` → CS0311; `StringMap<TV> : IMap<string,TV>` → CS0305 + CS0311 | CONFIRMED |
| P11 | ServiceRegistrations | extends W6 | `protected` nested `[Register]` → CS0122 | CONFIRMED |
| P12 | ServiceRegistrations, Cli | SR `:20-21`; Cli `:64-70` | Per-node `GetTypeByMetadataName` (2× and 7× per class); SR matches attributes by simple name | Read; D2 |
| P13 | Cli | `CliCommandSourceGenerator.cs:413` | `private protected` command → `protected internal partial class` → CS0262 | CONFIRMED |
| P14 | Cli | `Producer.cs:322-343` | A private nested root command → the public `{Type}CliExtensions` → CS0122 ×3 | CONFIRMED |
| P15 | Cli | `CliCommandSourceGenerator.cs:47-60` | Non-partial, generic or static command → nothing emitted, no diagnostic | CONFIRMED (non-partial, generic) |
| P16 | Cli | `Producer.cs:192` | The default handler passes `CancellationToken.None`, so Ctrl+C never reaches `Execute` | Read + probe |
| P17 | Mapper | `MapperGenerator.Producer.cs:119, 147`; `:62-91` | Every reference-type mapping → CS8603 ×2 (`return default;` under `#nullable enable`). With no `[MapperConversion]`, an unreachable return gives CS0162. Fails TreatWarningsAsErrors. | CONFIRMED |
| P18 | Mapper | `MapperGenerator.cs:86-99` → `Producer.cs:100` | Class-level `[GenerateMapper]` never sets the accessibility fields, so every mapper is `internal` | CONFIRMED |
| P19 | Mapper | `MapperGenerator.cs:26, 30-31` | The method-name prefix argument is captured and ignored. The existing test passes the default `"MapTo"`, which hid it. | CONFIRMED |
| P20 | Mapper | `Producer.cs:100` | `[GenerateMapper]` on a private nested type → CS0122 ×8, no diagnostic | CONFIRMED |
| P21 | Description | `DescriptionSourceGenerator.cs:24-53` | `/// <summary>` plus an explicit `[Description]` → CS0579 | CONFIRMED |
| P22 | Description | `:16-26` | The predicate admits every constructor, property and event, each costing a `GetDeclaredSymbol` that is then discarded | Read |
| P23 | Attributes package | `BaseConstructorParameterAttribute.cs` | Public, but nothing reads it | grep |
| P24 | JoinMethod | `JoinMethodGenerator.cs:18, 22` | `typeof(...).FullName` lookup (works, but ties the lookup to the generator assembly) | Read |

**Outcome: all fixed.** Inject P1–P8 in M4a (`aa06171`): P3 through `GetArgument`; P4 decided by S7 (the section is
documentation only, OPTIONS002 removed); P5 is INJECT005; P7 is INJECT006. ServiceRegistrations P9–P12 in M4b
(`ae28b74`): P9/P10 compile, and a mapping that cannot be expressed is REGISTER005; P11 is REGISTER003. Cli P13–P16 in
M5 (`c057a32`): P14 is MINTCLI007/MINTCLI008, P15 is MINTCLI002/003/004. Mapper P17–P20 in M6 (`dd5f7ce`): P20 is
MAPC006; P19 needed a follow-up for nested properties in M8 (I2 below). P21/P22 in M4b, P23 `[Obsolete]` in M4b, P24 in
M7. P25 was found by S3; see below.

Clean on every #194 pattern: ValueComparer/Equality, which handles accessibility, generic containers, nullable and
records with zero warnings. Also clean: JoinMethod apart from P24, Description apart from P21/P22, and Inject partials
for every accessibility and for generics.

### Test-harness gap

**H1.** `MintPlayer.SourceGenerators.Testing/GeneratorHarness.cs:255-266` (`RunAnalyzerAsync`) passes no
`onAnalyzerException` and filters the results to the analyzer's own IDs, which drops AD0001. Given the C5 trigger it
returned **zero diagnostics**: a crashing analyzer looks the same as a quiet one. `ApplyCodeFixAsync` (~line 418) has
the same gap. The generator path has a weaker version of it: CS8785 is a warning, so `run.Errors` stays empty when a
generator crashes. This ships in **MintPlayer.SourceGenerators.Testing**, so downstream test suites are affected as
well.

**Outcome: fixed in M1 (`fb60069`).** The harness is strict by default and throws `ComponentCrashedException` on CS8785,
MPSG001 or AD0001; M8 (`37c02ca`) added MPSG002. `AllowComponentCrashes()` opts out.

### Verified clean

These were probed and found clean:
- ValueComparerGenerator on all five kinds: it keeps the user's keyword and does not collide with members C# synthesizes
  for records.
- Description on all five kinds.
- JoinMethodGenerator.
- CliCommandInterfaceAnalyzer.
- Mapper's static-class predicate.
- INTF001 on records and on the members C# synthesizes for them.
- `GetPathSpec` with class, record class, record, struct and record-struct parents.
- `Producer.Produce`, which already catches and reports MPSG001.

### Findings during implementation

Found after the audit, while implementing or reviewing. All are fixed in this PR.

| ID | Found in | Finding | Outcome |
|---|---|---|---|
| P25 | S3 (M0) | A type with a parameter list (positional record, primary-constructor class or struct) plus the generated Inject constructor gives CS8862. | INJECT007 (M4a, `aa06171`) |
| I1 | M7 | MINT002 had two descriptors: Info for an already-declared member, Warning for "only one of `Equals(object)`/`GetHashCode()` declared". One id must have one descriptor (and one severity) once `DiagnosticInfo` is keyed by id. | The warning is MINT008; MINT002 is Info only (`ee62787`). Behaviour change in the CHANGELOG. |
| I2 | M8 | Once the assembly-level prefix was honoured (P19), a nested mapped property still called `MapTo{Type}()` (CS1061). | The producer looks the pair up among the generated methods (`7525f6f`). |
| I3 | M6 | A `null` source collection mapped to a plain `null` in a non-nullable `List<T>`/array property: CS8601 (found by building `TestProjects/MapperDebugging`). | Mapped to `null!`; the nullable test covers lists and arrays (`3e1c5af`). |
| I4 | Pre-PR review | CONFIG005 (a `DefaultValue` the field cannot hold) and OPTIONS004 (`[Options]` with `[Config]`) were declared and shipped but never reported: the first produced uncompilable or wrong code, the second silently took the `[Config]` path. | Both reported (`755a34f`). |
| I5 | Pre-PR review | `[Inject]` on a property with an ordinary setter was skipped silently and stayed null. | INJECT008, Warning; still not assigned (`755a34f`). |
| I6 | Pre-PR review | `[Config]` defaults: ignored on nullable fields and on `Uri` fields; an enum default on a non-`int` enum emitted `Small.7`; numeric defaults were not cast to the field type; floating-point defaults were culture-formatted (`3,5d` under nl-BE). | All fixed (`755a34f`). |
| I7 | Final pre-PR pass | A `RootNamespace` that is not a valid namespace name (`My-App`, which the SDK passes through as is) made every generator writing `namespace {RootNamespace}` emit CS0246/CS0102/CS0548: ServiceRegistrations, ClassNames, Mapper, CliGenerator and the MintPlayer.Assertions equivalency registration. | Sanitized once in Tools (`StringExtensions.ToSafeNamespace`, in `Settings` and `Producer`): invalid characters become `_`, a leading digit gets `_`, a keyword is `@`-escaped (`1b4e8b1`). |
| I8 | Final pre-PR pass | `[ConnectionString]` with `[Options]` on one member had no diagnostic and silently took the `[ConnectionString]` path. Every other marker pair already had one (CONFIG006, CONFIG008, CONNSTR003, OPTIONS003, OPTIONS004). | OPTIONS005, Error (`1b4e8b1`). |
| I9 | Docs pass (`c58c7b6`) | Installation, measured on packed packages: the generators are development dependencies, so `dotnet add package` writes an `IncludeAssets` list without `compile` that also filters the `*.Attributes` dependency; the attributes are CS0246. The READMEs said the generator brings them along. | Every README says to install both packages. |
| I10 | M2 review | The first `DiagnosticInfo` held the `DiagnosticDescriptor`, a Roslyn object in a pipeline value. | It holds the rule id (`a577a26`); a second, different descriptor under one id throws (`44d1c0d`). See D0.3. |
| I11 | M8 | A diagnostic that could not be reported was turned into MPSG002 (C7), but the harness did not treat it as a crash. | MPSG002 joined the harness's crash ids (`37c02ca`). |

## Current five-kind matrix

| Generator | class | record class | record | struct | record struct |
|---|---|---|---|---|---|
| GenericMethod | OK | **crash** (C1) | **crash** | **crash** | **crash** |
| Inject | OK | no-op (K2) | no-op | no-op | no-op |
| Description | OK | OK | OK | OK | OK |
| Cli command (AttributeUsage Class) | OK | no-op (K4) | no-op | n.a. (CS0592) | n.a. |
| Cli command nested in a parent of this kind | OK | OK | OK | OK | OK |
| ServiceRegistrations (emits no partial; AttributeUsage Class) | OK | no-op (K3) | no-op | n.a. | n.a. |
| Mapper (emits no partial; maps to/from) | OK | OK | OK | CS0037 (K6) | CS0037 (K6) |
| ValueComparer / GenerateEquality | OK | OK | OK | OK (ref struct: K7) | OK |
| INTF001 analyzer + fix | OK | OK | OK | not run (K8) | not run (K8) |
| UnusedUsings | OK | OK | OK | OK | OK |
| ClassNames | OK | n.a.: lists classes by design | n.a. | n.a. | n.a. |
| JoinMethod | n.a.: no user type | | | | |

Existing tests cover the class column almost everywhere. Explicit `record class` has no fixture anywhere. Equality
(`Snapshots/EqualityShapes.cs`) is the only generator with five-kind coverage, and `GetPathSpec` has no test at all.

**Final state (12.2.0):** every cell above is OK or n.a., each with a test. A `ref struct` model is MINT007, and INTF001
runs on structs.

## Goals

1. **No crash from unrelated or half-typed code.** No generator throws, and no analyzer throws or recurses without
   bound, on any input the probes found (C1–C6). The two SUSPECTED items are either fixed defensively (C7) or deleted
   (C8).
2. **Five kinds, every partial-emitting generator.** GenericMethod, Inject, Description and Cli (record-class kinds,
   since its AttributeUsage is Class) emit the user's actual keyword. ServiceRegistrations accepts records. Mapper maps
   structs. INTF001 runs on structs.
3. **Unsupported means a diagnostic.** `ref struct` for Equality, a non-partial parent, a `file` type, a private nested
   `[Register]` class, an interface or abstract mapper destination: each one reports a diagnostic and emits nothing,
   never broken code.
4. **The harness surfaces crashes.** Generator runs fail on CS8785 or MPSG001. Analyzer and code-fix runs fail on
   AD0001 or an `onAnalyzerException`.
5. **Fix every W, G and P finding.** G12 (one file per type) is rejected; see the table.
6. **Generated code compiles warning-free** under `<Nullable>enable</Nullable>` with TreatWarningsAsErrors (G6, G15,
   P5, P17). Equality already meets this; every generator gets the same compile check.
7. **Less boilerplate:** the shared plumbing in D0 replaces the hand-rolled copies listed there.

## Non-goals

- Supporting `struct` or `record struct` where the attribute's own `AttributeUsage` forbids it: Cli commands and
  `[Register]`. A DI service or a CLI command as a value type is not a supported shape. CS0592 already tells the user.
- ClassNames for records or structs. The generator lists *class* names by design.

## Design

### D0. Shared plumbing in MintPlayer.SourceGenerators.Tools

Rule: extract a helper only when there are at least 3 copies, or when the copies are themselves the bug.

**D0.1. Reading attribute arguments.**
- Copies:
  - Cli `CliCommandSourceGenerator.cs:447-497`;
  - ServiceRegistrations: four copies of the accessibility decoding (`:71-74, 97-100, 228-231, 260-263`) and `:300-307`;
  - Mapper `ElementAtOrDefault` at `:24-27, 77-81, 268-269, 330-339`;
  - JoinMethod `:22-23`, Equality `Discovery.cs:285`, Inject (P3, from syntax text) and GenericMethod (G2, from
    syntax text).
- Removes the bug class behind C2, P3 and G2. About 80–100 lines saved.

```csharp
bool TryGetArgument<T>(this AttributeData a, string name, out T value); // ctor parameter name or named property
T GetArgument<T>(this AttributeData a, string name, T fallback);        // enums, typeof (INamedTypeSymbol), null-safe
```

**D0.2. Finding an attribute by metadata name.**
- Five styles exist today:
  - `Name == nameof(X)` (ServiceRegistrations ×4)
  - `GetTypeInfo(attr).Type?.Name` (Inject ×6)
  - `ToDisplayString()` (Mapper ×10)
  - `typeof(X).FullName` (JoinMethod, GenericMethod)
  - `Discovery.IsAttribute` (Equality, the correct one)
- Fixes P6, P24 and G11. About 30 lines saved.

```csharp
AttributeData? FindAttribute(this ISymbol s, string metadataName);
IEnumerable<AttributeData> FindAttributes(this ISymbol s, string metadataName);
```

**D0.3. Equatable discovery result and a diagnostic reporter.**
- Copies:
  - Inject `PostConstructDiagnostic` and `ConfigDiagnostic`, mapped twice (`Producer.cs:22-33`);
  - Equality `DiagnosticInfo` and `ValueComparerDiagnosticReporter` (`ValueComparerGenerator.cs:89-98`,
    `DiscoveredType.cs:34`);
  - hand-rolled switches in ServiceRegistrations (`Producer.cs:41-42`) and Mapper (`Producer.cs:26-27`);
  - two empty reporters in ClassNames.
- Equality's `DiscoveredType` is the model to generalize. About 60–80 lines saved.

*As planned,* `DiagnosticInfo` held the descriptor. *Decided instead* (`a577a26`, `44d1c0d`): a pipeline value holds
no Roslyn object, so `DiagnosticInfo` is keyed by rule id. The constructor registers the descriptor under its id in a
process-wide registry, and a second, different descriptor under an id already seen throws (one id, one descriptor).
`Rule` looks the descriptor up at report time and is never compared. Both are hand-written equatable classes rather
than records.

```csharp
sealed class DiagnosticInfo // compares RuleId, Location and Args
{
    DiagnosticInfo(DiagnosticDescriptor rule, LocationKey? location, EquatableArray<string> args); // registers rule by id
    static DiagnosticInfo Create(DiagnosticDescriptor rule, Location? location, params object?[] args);
    string RuleId { get; }
    LocationKey? Location { get; }
    EquatableArray<string> Args { get; }
    DiagnosticDescriptor Rule { get; } // looked up by RuleId
}
sealed class Discovered<TModel>(TModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);
IncrementalValuesProvider<Discovered<TModel>> ForAttribute<TModel>(this SyntaxValueProvider p, string metadataName,
    Func<SyntaxNode, CancellationToken, bool> predicate,
    Func<GeneratorAttributeSyntaxContext, CancellationToken, Discovered<TModel>> transform);
// .Models() / .Diagnostics() split; ReportDiagnostics(...) on the init context
```

**D0.4. File preamble and namespace block.**
- 8 producers write `Header` themselves, 4 of them write `#nullable enable` in two different orders, and Inject writes
  neither (P8).
- The `OpenBlock($"namespace {ns}")` + `?.Dispose()` pattern is copied in Description, GenericMethod and Inject; Cli
  and Equality branch by hand.
- About 40 lines saved.

```csharp
protected virtual bool NullableEnable => true;   // Producer.Produce writes header + #nullable before ProduceSource
IDisposable OpenNamespace(this IndentedTextWriter w, string? ns); // no-op when null/empty
```

*Decided in M2:* the preamble is opt-in, `protected virtual bool WritePreamble => false`, with `NullableEnable` read only
when it is on, because producers in other packages built on Tools write `Header` themselves and would get it twice.
Every producer touched in this work opts in, except `EqualityProducer` (see Status).

**D0.5. Re-declaring a type: the extended `GetPathSpec`** (D1 below). It also absorbs:
- Description's keyword logic (`:47-49`);
- Cli `BuildDeclaration` (`:405-445`, the source of P13);
- Equality `KeywordOf`/`DeclaredName` (`:229-244`);
- the type-parameter and constraint builders (Inject `:336-390`, ServiceRegistrations `BuildConstraintClause`);
- the four partial checks (Inject `:44`, Description `:51, 58`, Cli `:47`, Equality `Discovery.cs:47`).

Not extracted: hint names (fixed per generator by policy) and `DiagnosticDescriptor` declarations (one `new(...)`
each, nothing to share).

### D1. One source of truth for "how do I re-declare this type"

The keyword logic is duplicated in four places today, and that duplication is the root of K1, K2, K4, K5, W2 and W3.
Move it into Tools (`SymbolExtensions`) so that callers stop hand-writing `partial class`:
- `GetPathSpec` gains an `Interface` element (K5).
- `GetPathSpec` also exposes the target type's own declaration header: `[file] partial record struct Name<T>` plus its
  constraints. That header is the keyword the user actually wrote (`record` vs `record class` is read from the syntax),
  generic parameters (W2) and `file` (W3).
- The result reports why a type is unsupported (non-partial parent, `file` type where the generator cannot target it)
  as a value, so each generator turns that into its own diagnostic.
- The header **never carries an accessibility modifier** (G4, P13). C# takes it from the user's declaration.
- The result exposes the type-parameter names in scope (`UniqueTypeParameterName(preferred)`), so generated generic
  methods never shadow a containing type's parameters (G5).

**Build on the existing `GetPathSpec` / `OpenPathSpec`** (`Tools/Extensions/SymbolExtensions.cs`); do not add a
parallel API. `GetPathSpec` already models the nesting chain, and the change extends it:
- **Parent kinds:** `EPathSpecType` gains `Interface` (K5), alongside the existing Class/Struct elements.
- **Each element** records the user's keyword (`class`, `record`, `record class`, `struct`, `record struct`,
  `interface`), its type parameters and constraints, and `IsPartial`. It never records accessibility.
- **The target type itself** becomes the last element, so `OpenPathSpec` opens the namespace, every parent and the
  type in one `using`. This replaces the "write parents, then hand-write `partial class Name`" step in every producer.
- **Unsupported reasons** are a value on the PathSpec: a non-partial element (generalizing today's `AllPartial`), a
  `file` type, or an accessibility a generator can't target.
- **Type-parameter names in scope** are exposed as well, e.g. `string UniqueTypeParameterName(this PathSpec spec,
  string preferred)`.

The PathSpec stays an equatable value, so it can sit in incremental models.

**Every generator that emits a partial of a user type moves onto it:**
- GenericMethod, Description, Inject and Cli already call it; they switch to the extended form and delete their
  hand-written keyword and type-parameter code.
- Equality (`KeywordOf` / `DeclaredName`) migrates once S1 confirms the output is byte-identical against the snapshot
  tests.

Generators that don't emit a partial of a user type (ServiceRegistrations, Mapper, ClassNames, JoinMethod) only use
the unsupported-reason checks where a diagnostic needs them (P14, P20, W6).

GenericMethod, Description, Inject and Cli all switch to it. Equality already has `KeywordOf` (Discovery.cs:229), which
the spike S1 evaluates for folding in.

### D2. `ForAttributeWithMetadataName` everywhere a marker attribute drives discovery

GenericMethod (C1, G10), Inject (K2, W4, P6), ServiceRegistrations (K3, W5, P12) and Cli (K4, P12) move to
`ForAttributeWithMetadataName` with a `TypeDeclarationSyntax` / `MethodDeclarationSyntax` predicate.
- It fixes the over-broad predicate by construction: unrelated attributes never reach the transform.
- It gives one callback per attributed declaration. Inject and Description then aggregate per symbol across
  `DeclaringSyntaxReferences` to fix W1 and W4, and ServiceRegistrations dedupes per symbol for W5.
- It must keep the incremental caching guarantees from `PRD-IncrementalCaching.md`: transforms return equatable models,
  never symbols or syntax.

### D3. Value-type awareness in Mapper (K6, K6')

Carry `IsValueType` on `TypeToMap`:
- Emit the null guard only for reference types.
- For a value-type destination, skip the `MapTo(input, output)` copy overload. A `ref` overload is the alternative,
  decided in S4.

### D4. Small targeted fixes

- **C2:** read the enum value with `namedArg.Value.Value is int i ? (EGeneratedAccessibility)i : Unspecified`.
- **C3:** validate the lifetime in the transform and report a diagnostic for undefined values.
- **C4:** guard `!aliasArgument.IsNull`.
- **C5:** use `usingDirective.NamespaceOrType`. W14: use `KeepLeadingTrivia | KeepDirectives`.
- **C6:** mark `OriginalDefinition` as visited before walking its properties. That makes the walk finite for expanding
  generics.
- **C7:** bounds-check in `ToLocation` and return null; wrap `ReportDiagnostics` diagnostic materialization in
  try/catch.
- **C8:** delete the unwired provider.
- **K7:** report a diagnostic for `IsRefLikeType` models.
- **K8:** include `TypeKind.Struct` in INTF001.
- **W6, W10, W11, W12, W13:** each reports a diagnostic instead of emitting broken code. New diagnostic IDs follow each
  package's existing numbering.
- **W7:** emit identifiers with `@`-escaping and skip or rename the reserved `ClassNames` collision.
- **W8:** filter `HasError` in `MapperEntrypointProducer`. W9: order type-switch cases most-derived first.
- **W15:** decline the fix when the signature references a type parameter of the containing type.
- **W16:** match interface members by signature, not by name.
- **W17:** skip models whose base or property types are `TypeKind.Error`.

### D4b. Fixes from the #194 sweep

- **GenericMethod (G0–G17):** see "GenericMethod decisions" above.
- **Inject:**
  - P1: optional (nullable) parameters go last.
  - P2: the constructor gets the least accessibility of the class and its parameter types.
  - P3: config, connection-string and options keys are read through D0.1.
  - P4: implement the `[Options("Section")]` binding the README promises. S7 decides whether it gets implemented or
    removed. *Decided (S7):* neither; `Section` stays documentation only, the README shows `BindConfiguration`, and the
    never-reported OPTIONS002 is removed.
  - P5: a non-void `[PostConstruct]` is a diagnostic (INJECT005; the method is not called).
  - P6: D0.2.
  - P7: "must be partial" is a diagnostic (INJECT006, through D1).
  - P8: D0.4.
  - *Added by S3:* all five kinds are generated, value types chain `: this()`, and P25 (a parameter list) is INJECT007.
- **ServiceRegistrations:**
  - P9: substitute type parameters by position, not by name.
  - P10: build the service type from the implemented interface's type arguments, and report a diagnostic when the
    mapping is not expressible.
  - P11: widen W6 to every accessibility below `internal`.
- **Cli:**
  - P13: fixed by D1.
  - P14 and P15: diagnostics.
  - P16: use the `SetAction(async (parseResult, ct) => …)` overload.
- **Mapper:**
  - P17: nullable-correct return (`D?`, or throw) and no unreachable return.
  - P18: set both accessibility fields in the class-applied branch.
  - P19: honour the method-name prefix.
  - P20: diagnostic (same family as W6/W10).
- **Description:**
  - P21: skip types that already carry `[Description]`.
  - P22: narrow the predicate to type declarations.
- **P23:** mark `BaseConstructorParameterAttribute` `[Obsolete]`, so it can be removed in 13.0. Removing a public type
  now would be a breaking change.
- **P24:** D0.2.

### D5. Harness (H1)

Changes to `GeneratorHarness`:
- `RunAnalyzerAsync` and `ApplyCodeFixAsync` pass `onAnalyzerException` and fail on it, and keep AD0001 in the returned
  set.
- `GeneratorResult` exposes `GeneratorExceptions` (from `GeneratorDriverRunResult.Results[].Exception`), and a default
  assertion helper fails on CS8785 or MPSG001.

This lands first, because every regression test below depends on it.

## Spikes

Each spike is time-boxed and ends in a written decision appended to this PRD before implementation of the affected
milestone.

- **S1. Keyword helper shape.**
  - Can one Tools API produce the declaration header for every caller: GenericMethod, Description, Inject, Cli and
    (optionally) Equality's `KeywordOf`?
  - Does reading `record` vs `record class` from syntax survive partial parts that spell it differently? The ValueComparer
    probe compiled a mix of `partial record class` and `partial record`.
  - Verify the header at C# 9, 11 and latest, matching `EqualityCompileMatrixTests`.
- **S2. Harness tightening blast radius.** Turn on H1's crash assertions and run the existing suite once. Count the
  tests that start failing: each one is a hidden crash or a test that relied on the gap. This decides whether H1 is
  opt-out (a default assertion) or opt-in.
- **S3. Inject on structs.**
  - Does the generated constructor make sense for a `struct` / `record struct`? It does compile.
  - Can `ActivatorUtilities` / `ServiceProvider` construct one? If not: support records, and report a diagnostic for
    value types instead of generating.
  - Decide before M3.
  - *Decided (see S3 results):* generate for all five kinds, value types chain `: this()`, and P25 is INJECT007.
- **S4. Mapper struct destinations.** Decide between skipping the copy overload and emitting `ref D output`, by checking
  which existing call sites use the copy overload.
- **S5. INTF001 on structs (K8).**
  - Confirm the code fix handles `struct` and `record struct` (it uses `TypeDeclarationSyntax`).
  - Check the false positives on a real codebase, as was done for #191: e.g. the `Equals`/`GetHashCode`/`ToString`
    override exclusion already applies.
  - This is a new warning on existing user code, so record the decision in the CHANGELOG.
- **S7. `[Options("Section")]` (P4).** Find out whether any known consumer relies on it (Spark, and the
  `TestProjects`). Then implement the binding the README describes, or remove `Section` and correct the README.
- **S8. GenericMethod arity 1 (G16).** Decide between dropping arity 1 and keeping it behind an opt-in. Measure how
  often an arity-1 overload is the intended call versus the trap (search downstream repos).
- **S6. `ForAttributeWithMetadataName` and caching.** For each migrated generator, confirm with the existing
  `IncrementalOutputCachingTests` pattern that an unrelated keystroke still re-runs nothing. *Run in M8 rather than
  M0;* see its result below.

## Spike results (M0)

Full write-ups were produced per spike: S1.md, S2.md, S345.md and S78.md.

**S1. The extended `GetPathSpec`: adopted, additive.**
- The type itself becomes `PathSpec.Target`.
- `OpenPathSpec(spec, includeTarget: true, baseList, targetLeadingLines)` opens the namespace, every parent and the type
  in one `using`. `includeTarget` stays `false` by default, because Tools is published.
- `EPathSpecType` gains `Record`, `RecordStruct` and `Interface`.
- `EPathSpecUnsupportedReason` has the values `None`, `TargetNotPartial`, `ParentNotPartial`, `FileLocal` and
  `UnsupportedKind`. When several apply, the first wins in this order: the target, then parents inside-out; within one
  element, kind, then `file`, then partial.
- Rules:
  - Always write `record` for a reference record. It is the only spelling C# 9 accepts, and it merges with a user part
    spelled `record class`.
  - Never repeat constraints, accessibility, `readonly`, `ref`, `static`, `sealed` or `abstract`.
  - Do repeat interface variance (`in`/`out`); omitting it is CS1067.
  - `@`-escape every identifier.
  - `IsPartial` means "any part is partial".
- Found while testing:
  - `OpenPathSpec` wrote `record class`, which is CS8773 at C# 9.
  - A `file` type re-declared in another file is a phantom type, so `FileLocal` is unsupported for every element in the
    chain.
  - C# 15 `union` binds as `TypeKind.Struct`, so it is reported as `UnsupportedKind` until `IsUnion` is no longer
    experimental.
- Equality on the new API is byte-identical (snapshots 18/18).
- `UniqueTypeParameterName` must return names that are free **for every index**: `T1` → `T11` collides at arity 11 or
  more. GenericMethod therefore takes a prefix (`TArg`, extended until no `{prefix}{n}` collides).

**S2. Strict harness: opt-out, strict by default.** Turning it on broke none of 659 existing tests, and it detects C1,
C3 and C5. Roslyn moves AD0001 into the `onAnalyzerException` callback, so the harness re-adds it from there. This
shipped in M1 (`fb60069`).

**S3. Inject:**
- Generate for **all five kinds**; the user's five-kind requirement wins over the spike's suggestion to reject value
  types. DI constructs a struct through `ActivatorUtilities` or a `typeof` registration.
- Value types chain `: this()`, which avoids CS0171 at C# 10.
- README: `default(S)` / `new S()` skip the constructor; record equality compares the injected fields; `with` copies
  them and does not re-run `[PostConstruct]`.
- **New finding P25:** a type with a parameter list (positional record, primary-constructor class or struct) plus the
  generated constructor gives CS8862. Report an Inject diagnostic instead.

**S4. Mapper:**
- Emit the null guard only for reference types.
- Skip the copy overload for value-type destinations. Nothing calls it, and with a struct destination it never worked.
- P17: emit `[return: NotNullIfNotNull("input")] D? Map(this S? input)` for reference types. Use the literal
  `"input"`, because `nameof` there needs C# 11. Only emit the attribute when the attribute type exists in the
  compilation. Value types keep `S` → `D`.
- Remove the unreachable return (CS0162).
- There are no downstream users of the Mapper.

**S5. INTF001 on structs: keep.** It produced 0 new warnings across about 2,400 files (Spark, AspNetCore.Tools, this
repo). The code fix works on struct and record struct. Update the stale comment at
`InterfaceImplementationAnalyzer.CodeFix.cs:45`.

**S7. `[Options("Section")]`: keep `Section` as documentation and fix the README.**
- The attribute's XML doc and `PRD-ConfigAttribute.md` already call it informational; the README contradicts them.
- Auto-binding would merge different sections bound to one options type, and could duplicate list entries.
- Remove the never-reported OPTIONS002. Correct the `DependencyInjectionDebugging` sample.

**S8. GenericMethod arity 1: drop it, with no opt-in.**
- It also hijacks calls from inside the class: `Add(List<object>)` there receives 1 element.
- No usages exist in any local repo.
- Attribute shape: `GenericMethodAttribute(int count = 4)` plus a settable `int Count`, range 2..16.
- G16 test: an outside `b.Add(list)` no longer compiles (the original is private); an in-class call reaches the
  original.
- G15: under `#nullable enable`, `this.Add([t1])` gives CS8601. The generated parameters must be nullable-aware
  (`T1?` or `object?` elements). *Shipped (M3):* a `where Tn : notnull` constraint when the collection's elements are
  non-nullable `object`.

**S6. Caching (run in M8, `9928df1`): holds.** Every generator moved onto `ForAttributeWithMetadataName` got an
`IncrementalOutputCachingTests` case on a non-class kind or the assembly-level form, each with the four irrelevant
edits, and re-runs nothing. `Guards/PipelineModelGuardTests` also scans every model and producer of every generator by
reflection and fails on any `Microsoft.CodeAnalysis` type.

## Test plan

All tests go into `MintPlayer.SourceGenerators.Tests` (Tools-level tests go into `MintPlayer.SourceGenerators.Tools.Tests`).
They are written per milestone and run as one sweep at the end.

1. **Five-kind theory per partial-emitting generator.** One `[Theory]` per generator, fed by a shared
   `TypeKinds` member-data set: `class`, `record class`, `record`, `struct`, `record struct`. Each kind is tested at top
   level, nested in a parent of each kind, and nested in an interface. Each case asserts:
   - no CS8785 or MPSG001;
   - `run.Errors` is empty;
   - the emitted header uses the user's keyword;
   - a runtime or behaviour assertion where the generator has one (Inject sets the fields; GenericMethod's generated
     overload is callable).

   Generators covered: GenericMethod, Inject, Description and Cli (class-kind records). Mapper and ServiceRegistrations
   get the same matrix for their mapping and registration semantics.
2. **Unrelated-code regression (the #193 case).** For each generator, a compilation contains:
   - a `[GeneratedRegex]` partial method in a record, a record struct, a struct and an interface;
   - plus one valid use of the generator.

   Assert that no crash diagnostic appears and that the valid use still generates.
3. **One regression test per C, K and W finding**, using the trigger from the tables above. C6 is a guarded test: a
   recursion that would overflow is detected by a depth counter in a test-only hook, or by running the analyzer on a
   dedicated thread with a small stack, so the regression cannot take down the test host.
4. **PathSpec unit tests (Tools):**
   - parents of all five kinds, plus an interface;
   - 1 and 2 levels of nesting;
   - generic parents;
   - a non-partial parent (`AllPartial == false`; shipped as `UnsupportedReason == ParentNotPartial`);
   - the global namespace.
5. **Harness tests:** a deliberately crashing generator and a deliberately crashing analyzer make the harness assertions
   fail.
6. **Compile matrix:** extend `EqualityCompileMatrixTests` with an explicit `record class` fixture and a `ref struct`
   (diagnostic) fixture.
7. **Warning-free compile for every generator.** Recompile the generated output with `NullableContextOptions.Enable`
   and assert zero warnings in `*.g.cs`, as the sweep probes did. This covers G6, G15, P5 and P17.
8. **The #194 GenericMethod matrix:** class / record / struct / record struct × public / internal × generic /
   non-generic × static / instance × void / value-returning / Task-returning. Each case asserts that the generated
   overload is callable and forwards its result. Add one test each for G1, G2 (const, arithmetic, cast, trivia, named
   argument order), G7 (each broken shape gives GENMETHOD003) and G16 (an outside call binds to the original).
9. **D0 unit tests** in `MintPlayer.SourceGenerators.Tools.Tests`: argument reading (const, arithmetic, cast, enum,
   typeof, missing → fallback), `FindAttribute` against a same-named type in another namespace, and the discovery
   scaffolding's caching (an unrelated edit re-runs nothing).
10. **Test fixtures use the shipped attributes.** Remove the hand-declared `GenericMethodAttribute` copies (G0), so a
    fixture can never again test an attribute shape that doesn't ship.

## Milestones

Commit per milestone. Run the test sweep once at the end.

M3 to M7 were developed on separate branches after M2 and merged back (the `Merge M…` commits). M4 was split into
M4a (Inject) and M4b (the rest of MintPlayer.SourceGenerators).

| M | Content | Status | Commits |
|---|---|---|---|
| M0 | Spikes S1, S2, S3–S5, S7, S8 (results appended here; S6 moved to M8) | Done | `465b0ee` |
| M1 | H1: the harness surfaces generator exceptions, AD0001 and analyzer exceptions. Harness tests. | Done | `fb60069` |
| M2 | D0 + D1 in Tools: argument reading, `FindAttribute`, `Discovered<T>`/`DiagnosticInfo`/`ForAttribute`, preamble/`OpenNamespace`, and the extended `GetPathSpec`/`OpenPathSpec`. Unit tests. C7. | Done; `DiagnosticInfo` re-keyed by rule id (I10) | `f14a562`, `a577a26`, `44d1c0d` |
| M3 | GenericMethod rework (C1, K1, G0–G17): the attribute moves to the Attributes package; fixtures updated; GENMETHOD001–004 | Done | `533c5f2` |
| M4a | Inject (K2, K5, W4, P1–P8, P25; after S3/S7): all five kinds, `: this()` for value types, INJECT005–007, OPTIONS002 removed | Done | `aa06171` |
| M4b | ServiceRegistrations (C2, C3, K3, W5, W6, P9–P12; REGISTER003–005), Description (W1–W3, P21, P22), ClassNames (W7, C8), UnusedUsings (C5, W14), INTF001 (K8, W15, W16; after S5). P23 `[Obsolete]`. | Done | `ae28b74` |
| M5 | MintPlayer.CliGenerator: C4, K4, K5, W3, W12, W13, P12–P16; MINTCLI002–009 | Done | `c057a32` |
| M6 | MintPlayer.Mapper: K6/K6' (after S4), W8–W11, P17–P20; MAPC005–007; list/array CS8601 (I3) | Done | `dd5f7ce`, `3e1c5af` |
| M7 | MintPlayer.ValueComparerGenerator: C6, K7, W17, P24; `record class` fixture; Equality onto `GetPathSpec` (per S1); MINT007, MINT008 (I1) | Done | `ee62787` |
| M8 | S6 caching checks and the pipeline-model guard; the five-kind, unrelated-code and warning-free compile theories for every generator (one shared helper); MPSG002 as a harness crash; release tracking for every package; the Mapper nested-prefix fix (I2) | Done | `37c02ca`, `7525f6f`, `fb0631c`, `9928df1` |
| M9 | CHANGELOG 12.2.0 entry (behaviour changes called out); README: new diagnostics, GenericMethod usage and the `params` note; PRD status; one full test sweep | Done | `55eadd8`, `f32cb71` |
| Pre-PR | Docs brought in line with 12.2.0; CONFIG005/OPTIONS004/INJECT008 and the `[Config]` default fixes (I4–I6); installation instructions (I9); `RootNamespace` sanitizing and OPTIONS005 (I7, I8); this PRD and the older PRDs updated | Done | `49ffa19`, `755a34f`, `c58c7b6`, `1b4e8b1`, and the final Docs commit |

## Versioning

Every package under `SourceGenerators/` moves 12.1.1 → **12.2.0** in lockstep (bumped on branch
`fix/193-generator-non-class-crash`).

This is a minor release, not a patch:
- The Attributes package gains a public type (`GenericMethodAttribute`), and Tools gains public API (D0).
- New diagnostics can turn previously silent code into warnings or errors: GENMETHOD001–004, non-void
  `[PostConstruct]`, "must be partial", and INTF001 on structs. *As shipped:* GENMETHOD001–004, INJECT005–008,
  REGISTER003–005, MAPC005–007, MINTCLI002–009, MINT007, MINT008 (the former Warning-severity MINT002), OPTIONS005,
  MPSG002, CONFIG005 and OPTIONS004 (declared before, reported only now), and INTF001 on structs.
- Some output changes visibly:
  - class-level mappers become `public` (P18);
  - the Mapper method-name prefix is honoured (P19);
  - GenericMethod no longer emits arity 1 (G16; confirmed by S8);
  - a `RootNamespace` that is not a valid name is sanitized (I7), which moves the generated code of such a project
    from a namespace that did not compile to a valid one.

Nothing public is removed: `BaseConstructorParameterAttribute` is only marked `[Obsolete]`, and the generator-internal
`GenericMethodAttribute` was never reachable by consumers. So it is not a major release. Two diagnostic changes need
action from users who suppressed them: OPTIONS002 (never reported) is removed, and suppressions of the
Warning-severity MINT002 must move to MINT008; the CHANGELOG lists both under Behaviour changes.

## Downstream

The trigger came from `MintPlayer.Spark.Settings` (`SparkQuota`, a record with `[GeneratedRegex]`),
MintPlayer/MintPlayer.Spark#268. After 12.2.0 is published, bump the package reference there in the same
unit of work and confirm the CS8785 is gone.

**Status: open, waiting on the 12.2.0 publish.** Two things to check when bumping: `[GenericMethod]` users (none were
found in S8) lose the arity-1 overload, and if the Spark projects reference a generator package with an
`IncludeAssets` list, they must also reference its `*.Attributes` package (I9).
