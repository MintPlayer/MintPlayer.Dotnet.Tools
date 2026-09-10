# PRD: INTF001 code-fix correctness and a multi-project code-fix harness

## Overview

`InterfaceImplementationAnalyzer` (`INTF001`) reports public members of a class that are absent from an
interface the class implements, and `InterfaceCodeFixProvider` offers to add them to that interface —
including when the interface lives in a *different project*. The design is right: report a diagnostic at a
location inside your own compilation and let only the fix reach outward. (Roslyn's analyzer driver silently
discards any diagnostic whose location lies in a syntax tree the analyzed compilation does not contain, so
the alternative design does not work at all.)

The implementation does not hold up. Issue
[#179](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/179) reports four defects and one test
gap, filed while building the equivalent `SPARK016`/`SPARK017` fixes in **MintPlayer.Spark**, a live consumer
on whose build `INTF001` fires today. All five were verified against master `8a98149`; **three further
defects were found during verification**, two of them more severe than anything in the issue. This PRD covers
all eight plus the harness work, in one pull request.

Line numbers are against
`SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/Diagnostics/` unless stated otherwise.

## Problem Statement

Today, on ordinary C#:

```csharp
// Project A
public interface IBase { string Name { get; } }
public interface IPerson : IBase { }
public interface IAuditable { DateTime CreatedOn { get; } }

// Project B
public record Person : IPerson, IAuditable        // ← record: the fix throws before the light bulb renders
{
    public string Name { get; }                    // ← declared on IBase; the fix re-declares it on IPerson
    public DateTime CreatedOn { get; }             // ← reported as "missing from IPerson" — a false positive
    public string LastName { get; }                // ← genuinely missing from both
    public string Note;                            // ← field: the fix throws NotImplementedException
}
```

1. `Person` is a `record`. The analyzer gates on `TypeKind.Class`, which includes records, but the fix calls
   `.First()` on an `OfType<ClassDeclarationSyntax>()` chain (`Codefix.cs:27`) —
   `InvalidOperationException: Sequence contains no elements`, thrown during *registration*.
2. Even as a `class`: the analyzer tests every public member against **every** interface separately
   (`Analyzer.cs:31` + `:58`), so `CreatedOn` is reported as missing from `IPerson` and `Name` as missing from
   `IAuditable`. Both are false positives. Multi-interface classes are unusable with this rule today.
3. The fix hardcodes `classSymbol?.Interfaces.FirstOrDefault()` (`Codefix.cs:49`) and so writes into
   `IPerson` regardless of which interface the diagnostic named.
4. `Note` is a field. It survives the fix's filter, reaches `CreateInterfaceMember`, and hits
   `_ => throw new NotImplementedException("Member type not supported")` (`Codefix.cs:110`).
5. `Name` is get-only, but the fix always emits `{ get; set; }` (`Codefix.cs:104-108`) — **CS0535**, a hard
   compile error, in code that compiled a moment ago.

And none of this is caught, because the code-fix harness can build only one project holding one document —
so the cross-project path the provider exists for has never executed under test.

## Goals

1. `INTF001` reports only genuinely missing members, with a defensible rule for classes implementing more
   than one interface.
2. The code fix never throws at the user, for any input.
3. The code fix never emits code that fails to compile, and never silently does nothing.
4. The code fix edits the interface the diagnostic actually named, in whichever project it lives.
5. The code-fix harness can express N projects, and fails loudly on a fixture that does not compile or on a
   fix that reports success while changing nothing.
6. Every defect below has a test that fails before the change and passes after.

## Non-Goals

1. Generating implementations on the class from an interface member (the inverse direction). Roslyn's own
   "Implement interface" fix owns that.
2. A `FixAll` strategy beyond the existing `BatchFixer` — see [R2.4](#r24--one-diagnostic-one-member) for why
   batch behaviour changes, but the provider stays.
3. Widening `INTF001` to structs, or to interfaces reached through a base *class*.
4. Rewriting `UnusedUsingsAnalyzer`, beyond the two shared-consistency items in [R6](#r6--consistency-cleanups).

---

## Verified defect inventory

Severity is the user-visible outcome, not the size of the diff.

| # | Defect | Site | Verdict | Severity |
|---|---|---|---|---|
| D1 | Fix targets `Interfaces.FirstOrDefault()`, not the interface the diagnostic named | `Codefix.cs:49` | Confirmed | High |
| D2 | Public field, event or nested type reaches `CreateInterfaceMember` and throws | `Codefix.cs:63-69`, `:110` | Confirmed, broader than filed | High |
| D3 | Fix compares against `interfaceSymbol.GetMembers()` only, re-declaring base-interface members | `Codefix.cs:64` | Confirmed | Medium |
| D4 | Interface document located by `d.FilePath == …` string match; silent no-op on miss | `Codefix.cs:52-56` | Confirmed | Medium |
| D5 | Code-fix harness is single-project, single-document | `MintPlayer.SourceGenerators.Testing/GeneratorHarness.cs:216-283` | Confirmed | High |
| **N1** | **`record` throws `InvalidOperationException` during fix registration** | `Codefix.cs:27` | **New** | **High** |
| **N2** | **Get-only / init-only property emitted as `{ get; set; }` → CS0535** | `Codefix.cs:104-108` | **New** | **High** |
| **N3** | **Analyzer requires every public member on every implemented interface → false positives** | `Analyzer.cs:31`, `:58` | **New** | **High** |

Corrections to the issue as filed, all verified empirically:

- **D2's constructor claim is wrong, and its member list is incomplete.** The dropped constructor exclusion is
  harmless — `CanBeReferencedByName` is `false` for `.ctor` and already filters it. The crash set is
  **field, event, nested type**; the issue missed nested types. Also safe for unrelated reasons: `const`
  (static), indexers (`this[]` is not referenceable by name), operators (static), finalizers (protected).
- **D2 "crashes" overstates the IDE outcome.** The throw is inside the `createChangedSolution` delegate and
  `missingMembers` is lazy, materialising at `.ToArray()` (`:78`) during `GetOperationsAsync`. Roslyn routes
  that through `IExtensionManager`, which catches, disables the provider for the session, and reports
  non-fatally. The realistic symptom is a light bulb that vanishes or does nothing, not a devenv crash. The
  exception still escapes a public extension point, and `BatchFixer` and the test harness see it raw. N1 is
  the more serious of the two, because it throws in the *registration* path.
- **D5's file reference points at the published package.** `GeneratorHarness.cs:224-240` is
  `MintPlayer.SourceGenerators.Testing` **v10.0.0**, on nuget.org, not repo-local test infrastructure. It has
  10 call sites, 9 of them in `MintPlayer.Assertions.SourceGenerator.Tests`. This constrains the fix to an
  additive overload — see [R5](#r5--multi-project-code-fix-harness).
- **D4's masking story is confirmed and is the strongest argument in the issue.** `git log -S` places the
  `filePath:` argument in **d73d877** (#172), whose own comment at `GeneratorHarness.cs:229-236` states that
  without it the comparison "matches nothing and returns the solution unchanged… the test passes and the
  entire body of the fix stays unreachable."

---

## Requirements

### R1 — Analyzer: correct multi-interface semantics

**R1.1** — A public member is reported **once**, when it is absent from every interface the class declares
(**variant A**, settled by [S1](#s1--what-does-intf001-mean-for-a-class-implementing-several-interfaces-gates-r1-r2-2h)).
Membership is tested against the union of all declared interfaces and their bases, so a member carried by one
interface is never reported against another. Interfaces that exist only in metadata still count towards the
union — they satisfy a member — but are never offered as a target, since they cannot be edited.

**R1.2** — Member lookup continues to honour base interfaces (`iface.AllInterfaces`, `Analyzer.cs:38-40`).
Unchanged; recorded so the fix can be aligned to it in [R2.3](#r23--honour-base-interfaces).

**R1.3** — The message format changes to name the candidate set rather than a single interface:

```
"Public member '{0}' is not defined in any implemented interface ({1})"
```

`{1}` is the comma-separated list of in-source interfaces. Under variant A the diagnostic deliberately does
**not** name one target — choosing between them is the fix's job ([R2.5](#r25--offer-one-action-per-candidate-interface)).
`INTF001`'s id, title, category and severity are unchanged. Existing analyzer tests that assert the old
message text are updated in M3.

**R1.4** — The diagnostic carries the member name in its `properties` bag, so the fix acts on the reported
member rather than recomputing the whole set ([R2.4](#r24--one-diagnostic-one-member)):

```csharp
properties: ImmutableDictionary<string, string?>.Empty.Add(MemberNameProperty, member.Name),
```

The `Diagnostic.Create(DiagnosticDescriptor, Location, ImmutableDictionary<string,string>, object[])`
overload is present on the pinned Roslyn 5.3.0 and round-trips intact ([S3](#s3--does-the-properties-bag-survive-the-analyzer-driver-round-trip-gates-r13-r15-1h)).
**This repo has no existing properties-bag precedent** — `grep properties:` across all analyzer code returns
nothing — so this establishes the pattern.

> **Superseded.** An earlier draft of R1.3 put the *interface* identity in the bag, fully qualified, so the
> fix could recover the single interface the diagnostic named. Variant A removes the need: there is no single
> named interface to recover. The fully-qualified display string survives as the code action's equivalence
> key instead ([R2.5](#r25--offer-one-action-per-candidate-interface)).

### R2 — Code fix: correctness

**R2.1 — Never throw.**
`RegisterCodeFixesAsync` uses `FirstOrDefault()` and matches `TypeDeclarationSyntax`, not
`ClassDeclarationSyntax`, so records and a stale span both return quietly instead of throwing (**N1**). The
existing `is null` guard on `Codefix.cs:28` becomes reachable — today it is dead code that reads as a
guard and is not one.

**R2.2 — Share one member predicate.**
Analyzer and fix use the same candidate filter, exposed once (**D2**). `CreateInterfaceMember` returns
`null` for anything it does not handle and the result is filtered out — a code fix must not throw at the
user, even if the predicate later drifts.

**R2.3 — Honour base interfaces.**
The fix's membership test uses `interfaceSymbol.GetMembers().Concat(interfaceSymbol.AllInterfaces.SelectMany(i => i.GetMembers()))`,
matching `Analyzer.cs:38-40` (**D3**). Re-declaring an inherited member is CS0108 — a warning, not an error,
which is why this ranks below N2, but it fires on every `IEntity`-style hierarchy and leaves a diff to
hand-clean.

**R2.4 — One diagnostic, one member.**
The fix adds only the member named in the diagnostic's properties bag ([R1.4](#r14)), not every member it
considers missing. Today one invocation rewrites the whole interface, which is what makes D1 and D3 damaging
rather than merely wrong. The code action title changes from "Add missing members to interface" to name the
single member; `BatchFixer` continues to provide the add-them-all behaviour, correctly, one diagnostic at a
time.

**R2.5 — Offer one action per candidate interface.**
The fix enumerates the class's in-source interfaces and registers one code action each — *"Add 'LastName' to
IPerson"*, *"Add 'LastName' to IAuditable"* — replacing the hardcoded `Interfaces.FirstOrDefault()` (**D1**).
Each action's `equivalenceKey` is the interface's fully-qualified display string, not its short name, so two
same-named interfaces in different namespaces cannot collide and `BatchFixer` groups correctly. With a single
interface — the overwhelmingly common case — exactly one action is offered and the UX is unchanged.

This is what makes variant A safe where per-interface reporting was not: under the rejected variant B,
*Fix all occurrences* would have added every missing member to **every** interface, which is the same
wrong-target failure D1 is about, merely arrived at from the other direction. See
[S1](#s1--what-does-intf001-mean-for-a-class-implementing-several-interfaces-gates-r1-r2-2h).

**R2.6 — Locate the interface document by symbol, not by path string.**

```csharp
foreach (var reference in interfaceSymbol.DeclaringSyntaxReferences)
    if (solution.GetDocumentId(reference.SyntaxTree) is { } id) { … }
```

`Solution.GetDocumentId(SyntaxTree)` exists on the pinned `Microsoft.CodeAnalysis.Workspaces` 5.3.0 (**D4**).
This is O(1) instead of O(every document in the solution), and immune to path case, separator, and linked or
generated files. A partial interface with several declarations is handled by iterating the references rather
than taking `Locations[0]`.

**R2.7 — Preserve accessor shape.**
A property's generated interface member mirrors the class property's accessors: `{ get; }` for get-only,
`{ get; set; }` for both, `{ get; init; }` for init-only, and a set-only property is not offered at all
(**N2**). Emitting `{ get; set; }` unconditionally produces CS0535 against the very class being fixed — the
only defect here that turns compiling code into non-compiling code.

**R2.8 — Never silently no-op.**
Every early-out path that returns the unmodified solution (`Codefix.cs:50, 56, 60, 75`) is either eliminated
by R2.1–R2.7 or moved into `RegisterCodeFixesAsync`, so the light bulb is not offered when the fix cannot
act. Roslyn wraps an unchanged solution in a valid `ApplyChangesOperation`, so a user-visible no-op is
indistinguishable from success — this is exactly the failure d73d877 documented.

### R3 — Diagnostics

No new diagnostic IDs. `INTF001` keeps its id, title, category and `Warning` severity; only its message
arguments and properties bag change.

| ID | Severity | Message | Change |
|---|---|---|---|
| `INTF001` | Warning | `Public member '{0}' is not defined in the interface '{1}'` | Message format unchanged; gains a properties bag ([R1.3](#r13), [R1.4](#r14)). Reporting *shape* may change per [S1](#s1--what-does-intf001-mean-for-a-class-implementing-several-interfaces-gates-r1-r2-2h). |

### R4 — Test coverage

Each defect gets a test that fails on master before the change. At minimum:

**Analyzer** — class implementing two interfaces where each member is on exactly one of them: no diagnostic
(**N3**); member absent from both: one diagnostic. Member on a base interface: no diagnostic (regression
guard on R1.2).

**Code fix** — `record` implementing an interface: fix is offered and applies (**N1**); public field, public
event, public nested type present: no throw, member ignored (**D2**); get-only, init-only and set-only
properties (**N2**), asserted by *compiling* the fixed output, per `SourceGenerators/CLAUDE.md`; base-interface
member not re-declared (**D3**); two interfaces, diagnostic on the second: member lands on the second
(**D1**); interface in a referenced project (**D4**, **D5**); fix applied twice is idempotent.

**R4.1** — The cross-project test is the acceptance gate for the whole PRD. If it cannot be written, R5 is
not done.

### R5 — Multi-project code-fix harness

`MintPlayer.SourceGenerators.Testing` is published at **10.0.0** with `GenerateDocumentationFile`, so changes
are **additive only**. `ApplyCodeFixAsync(string, string, string)` stays and delegates to the new overload
with a single one-file project; all 10 call sites — 9 in `MintPlayer.Assertions.SourceGenerator.Tests`, 1 in
`SourceGenerators/…/_Infrastructure/CodeFixHarness.cs` — compile and behave identically.

```csharp
public sealed record FixtureProject(string Name, IReadOnlyDictionary<string, string> Sources)
{
    public static FixtureProject Of(string name, params (string FileName, string Source)[] sources);
}

public sealed record CodeFixResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    string FixedSource,
    bool Applied,
    string? ActionTitle = null,
    IReadOnlyDictionary<string, string>? Documents = null)   // new; keyed by file name
{
    public string Document(string fileName);                 // throws, listing available names
}

public Task<CodeFixResult> ApplyCodeFixAsync(
    string analyzerTypeName, string codeFixTypeName,
    IEnumerable<FixtureProject> projects, string? diagnosticId = null);
```

`FixedSource` is asserted directly at 21 sites across this repo and must keep its meaning; N documents are
exposed through the new `Documents` map instead.

**R5.1** — Projects are supplied in dependency order, each referencing all its predecessors
(`ProjectInfo.Create(…, projectReferences: previous)`). Diagnostics come from the **last** project's
compilation. Every document gets a real `filePath` (`$"/{project}/{file}"`). The fixed document is located by
`Location.SourceTree` across **all** projects.

**R5.2 — Fail loudly on a fixture that does not compile.** `compilation.GetDiagnostics()` filtered to errors,
before the analyzer runs. Today compile errors are ignored entirely: `CodeFixResult` carries only *analyzer*
diagnostics, so a mis-wired `ProjectReference` yields zero diagnostics, `Applied: false`, and every
"it declines to fix here" test passes for the wrong reason. Spark's harness records this greening 3 of its 8
tests.

**R5.3 — Fail when a fix reports success but changed no document.**
`applied.GetChanges(solution).GetProjectChanges().SelectMany(p => p.GetChangedDocuments()).Any()`.
This is the permanent close on the d73d877 trap.

**R5.4** — R5.2 and R5.3 apply to **both** overloads, on by default
([S2](#s2--can-the-loud-guards-be-turned-on-for-the-existing-overload-gates-r54-2h) overturned the original
opt-in plan: all 242 SourceGenerators tests pass with the guards on). R5.2 is escapable through
`requireCompilableFixture: false` for a purely syntactic analyzer whose fixture deliberately names an
unreferenced package; the opt-out is stated at the call site, never in the harness. R5.3 has no escape hatch.

**R5.7** — After the fix applies, every project of the changed solution is compiled and the errors exposed as
`CodeFixResult.Errors` / `ErrorText`. A fix that emits uncompilable code reports no diagnostic of its own, so
asserting on the fixed text alone passes while the consumer's build breaks — and a fix that edits an
interface in one project can break the class implementing it in another, which no single-project fixture can
show.

**R5.5** — The generator DLLs are copied to the test bin root by `CopyGeneratorRuntimeAssets` and
`Assembly.Load`-ed by simple name into the default ALC. Coverage attribution depends on this. The harness
change must not disturb it.

**R5.6** — `C:\Repos\MintPlayer.Spark\tests\MintPlayer.Spark.SourceGenerators.Tests\_Infrastructure\CodeFixHarness.cs`
is a working 253-line implementation of everything above and is the reference. It is not a dependency, and
nothing is to be cloned or copied wholesale — the published package's API shape differs.

### R6 — Consistency cleanups

Small, in-scope, same files:

- `InterfaceCodeFixProvider` gains `[Shared]`, which `UnusedUsingsCodeFixProvider` already has and it lacks.
- `ImmutableArray.Create("INTF001")` becomes `ImmutableArray.Create(DiagnosticRules.MissingInterfaceMemberRule.Id)`.
- `_Infrastructure/CodeFixHarness.cs:27-33` `DiagnoseAsync` is dead — zero call sites repo-wide. Remove.
- File-name casing: `.Codefix.cs` (Interface) vs `.CodeFix.cs` (UnusedUsings). Normalise to `.CodeFix.cs`.

### R7 — Declare the Workspaces dependency explicitly

`InterfaceCodeFixProvider` derives from `CodeFixProvider`, which lives in
`Microsoft.CodeAnalysis.CSharp.Workspaces`. `MintPlayer.SourceGenerators.csproj` **never references it**. It
compiles only because `Microsoft.CodeAnalysis` 5.3.0 is a metapackage that drags in Workspaces and Features
transitively.

Compare `MintPlayer.Spark.LibraryGenerators.csproj`, which declares the dependency the way an analyzer
should:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.3.0"
                  PrivateAssets="all" ExcludeAssets="runtime" />
```

`ExcludeAssets="runtime"` is the point: it satisfies the compiler while shipping nothing, because the IDE host
supplies Workspaces and the command-line host (`csc`) never loads a fix provider at all.

**R7.1** — Reference `Microsoft.CodeAnalysis.CSharp.Workspaces` explicitly with
`PrivateAssets="all" ExcludeAssets="runtime"`, and drop the `Microsoft.CodeAnalysis` metapackage in favour of
`Microsoft.CodeAnalysis.CSharp` + `.Analyzers`, matching both Spark and `eng/sourcegenerator.targets`.

**R7.2** — Verify the packed nupkg's `analyzers/dotnet/cs` folder is unchanged by R7.1. It should already
carry no Workspaces assemblies — `AddAnalysisTimeDependencies` packs only
`Microsoft.Extensions.DependencyInjection.Abstractions` — so today's outcome is correct *by accident*. R7.1
makes it correct by declaration. Gated by [S4](#s4--does-narrowing-the-workspaces-reference-change-the-packed-output-gates-r7-1h).

**R7.3** — This repo already survives the reflection half of the trap that Spark documents: the test projects
and the Testing package carry a real Workspaces reference precisely because locating a component reflects over
the assembly and loads every type (`MintPlayer.SourceGenerators.Testing.csproj:38-45`,
`MintPlayer.SourceGenerators.Tests.csproj:28-32`). Note the residual risk: `LoadableTypes()`
(`GeneratorHarness.cs:334-345`) **swallows** `ReflectionTypeLoadException` and keeps whatever loaded, so if
Workspaces ever went missing, `InterfaceCodeFixProvider` would silently vanish from the type list rather than
fail. `Instantiate<T>` throws a well-worded `ComponentTypeNotFoundException` in that case
(`:307-325`), which is what makes the swallow survivable. No change required; recorded so R7.1 is not
mistaken for a fix to a live bug.

---

## Technical Design

**Analyzer** (`InterfaceImplementationAnalyzer.cs`) — the `foreach (var iface in namedTypeSymbol.Interfaces)`
loop at `:31` inverts: iterate candidate members in the outer loop and test each against the union of all
declared interfaces (subject to S1). `Diagnostic.Create` at `:63` moves to the properties-bag overload.

**Shared predicate** — a new `static` helper holding the candidate filter (`public`, non-static,
`CanBeReferencedByName`, not implicitly declared, `IMethodSymbol` or `IPropertySymbol`, no
`[NoInterfaceMember]`), consumed by both `Analyzer.cs:43-49` and `Codefix.cs:63-69`. Colocated with the
analyzer, following the `SymbolHelpers.cs` precedent in `Assertions/…/Diagnostics/`.

**Code fix** (`InterfaceImplementationAnalyzer.CodeFix.cs`) — `RegisterCodeFixesAsync` reads the properties
bag, resolves the interface symbol and the single target member, and registers only when both resolve.
`CreateInterfaceMember` becomes `MemberDeclarationSyntax?` and grows accessor-shape handling.
`AddMissingMembersToInterfaceAcrossProjects` narrows to one member and switches to
`Solution.GetDocumentId`.

**Harness** (`MintPlayer.SourceGenerators.Testing/GeneratorHarness.cs`) — new overload building an
`AdhocWorkspace` from `IEnumerable<FixtureProject>`; existing overload wraps it. `MetadataReferences()` is
shared across all projects unchanged.

## Milestones

All in one PR. Milestones are commit boundaries; the test suite runs once, after M5 (repo policy).

| ID | Milestone | Requirements | Δ |
|---|---|---|---|
| M0 | Spikes [S1](#s1--what-does-intf001-mean-for-a-class-implementing-several-interfaces-gates-r1-r2-2h)–[S3](#s3--does-the-properties-bag-survive-the-analyzer-driver-round-trip-gates-r13-r15-1h) | — | gate |
| M1 | Harness: N-project overload + loud guards | R5.1–R5.6 | enables everything |
| M2 | Cross-project + multi-interface tests, red | R4, R4.1 | proves M1 |
| M3 | Analyzer: multi-interface semantics + properties bag | R1, N3 | |
| M4 | Code fix: never throw, never miscompile | R2.1, R2.2, R2.7, N1, N2, D2 | |
| M5 | Code fix: right interface, right member, right document | R2.3–R2.6, R2.8, D1, D3, D4 | |
| M6 | Consistency cleanups, Workspaces reference, version bumps | R6, R7 | |

Ordering rationale: M1 before everything, because until the harness can express two projects no test for D1,
D3 or D4 can fail for the right reason — that is the whole lesson of d73d877. M2 lands the tests red so the
subsequent milestones are demonstrably driven by them. M3 precedes M4/M5 because the properties bag is the
input the fix work depends on.

## Spikes

### S1 — What does INTF001 mean for a class implementing several interfaces? *(gates R1, R2, 2h)*

**Question.** Today a public member is reported once per interface it is absent from, so
`class P : IPerson, IAuditable` reports four diagnostics for three members, two of them false positives
(N3). Two candidate semantics: **(a)** report once per member when it is on *no* declared interface — the
fix then has to ask which interface to add it to; **(b)** keep per-interface reporting but suppress when the
member is present on any other declared interface — the diagnostic keeps naming a single unambiguous target.

**Do.** Write both as analyzer test fixtures against the multi-interface shapes that actually occur in
MintPlayer.Spark (which is where `INTF001` fires today — `IBatchedLoadActions.OnDeleteRowAsync`). Count
diagnostics and check whether the resulting code action is unambiguous in each.

**Decision rule.** If (b) yields one diagnostic per genuinely-missing member/interface pair with no false
positives → take (b), it preserves the message format and needs no UI for choosing a target. If (b) still
produces duplicates for a member missing from two interfaces → take (a) and have the fix offer one code
action per candidate interface. If neither is clean → report once per member and target the *first* declared
interface explicitly, documenting it in the rule description rather than leaving it as an accident of
`FirstOrDefault()`.

**Why it is a spike, not a task.** N3 was not in the issue and changes what the diagnostic *means*. R2.5
(resolve target from the properties bag) has a different shape under (a) than under (b), so guessing here
costs the code-fix work twice.

**RESULT — run 2026-09-10 against master `8a98149`. Take (a). The decision rule's two clauses both fired and
(a) wins on a ground the rule did not anticipate: `FixAll`.**

Both variants were implemented and run over six fixtures. Diagnostic counts:

| Fixture | shipped | variant A | variant B |
|---|---|---|---|
| `single-interface-one-missing` | 1 | 1 | 1 |
| `two-interfaces-split-nothing-missing` | **2 (both false)** | **0** | **0** |
| `two-interfaces-one-genuinely-missing` | **4 (2 false)** | **1** | 2 |
| `base-interface-member` | 0 | 0 | 0 |
| `spark-shape-batched-load-actions` | 1 | 1 | 1 |
| `cross-project` | 1 | 1 | 1 |

N3 is confirmed with numbers: a two-interface class where each member sits on its own interface produces
**two diagnostics, both false**, and neither is suppressible without suppressing the rule.

Both variants remove every false positive, so the rule's first clause ("(b) with no false positives → take
(b)") and its second ("(b) still produces duplicates → take (a)") both fire. The tiebreak is `FixAll`. Under
(b), `LastName` — missing from both interfaces — yields two diagnostics on the same source location, and
*Fix all occurrences in document* would add the member to **both** `IPerson` and `IAuditable`. That is the
same wrong-target damage D1 describes, reached from the other side, and it would be introduced by the very
change meant to remove it. Under (a) the member yields one diagnostic and the ambiguity surfaces where it
belongs — as a choice between two code actions, with `BatchFixer` grouping on `equivalenceKey` so a batch
run picks one interface and stays consistent.

Cost of (a): the message format changes (R1.3) and the interface key leaves the properties bag (R1.4). For a
single-interface class, still exactly one diagnostic and one action — no behaviour change for the common
case, which is every fixture in the current test suite and the live Spark report.

### S2 — Can the loud guards be turned on for the existing overload? *(gates R5.4, 2h)*

**Question.** R5.2 (fail on fixture compile errors) and R5.3 (fail when nothing changed) are what make a
cross-project test meaningful. Applying them only to the new overload leaves 9 Assertions tests and 12
SourceGenerators tests running under the old, silent semantics — two standards in one package.

**Do.** Add both guards unconditionally in a scratch branch and run
`MintPlayer.Assertions.SourceGenerator.Tests` and `MintPlayer.SourceGenerators.Tests`. Record which tests go
red and whether each is a genuine latent defect or a fixture that never needed to compile.

**Decision rule.** If zero tests go red → apply unconditionally, no flag, no `10.1.0` behavioural caveat. If a
small number go red and each is a real latent defect → fix them in this PR and still apply unconditionally
(they are exactly the class of bug this PRD exists to remove). If many go red for fixture-hygiene reasons →
fall back to R5.4 as written, opt-in on the new overload, and note the split in the package README.

**Why it is a spike, not a task.** It is a behaviour change to a published API with call sites outside the
area being changed, and the answer decides whether the package bump is `10.1.0` or needs a release note.

**RESULT — run 2026-09-10. Mixed. Guards stay on by default; one explicit, documented opt-out is added.**

Both guards applied unconditionally, then both suites run:

| Suite | Result |
|---|---|
| `MintPlayer.SourceGenerators.Tests` | **242 passed, 0 failed** |
| `MintPlayer.Assertions.SourceGenerator.Tests` | **57 passed, 8 failed** |

All 242 SourceGenerators tests — including the 12 code-fix tests — pass with both guards on, so nothing in
the area this PRD touches depends on the silent semantics.

All 8 Assertions failures are guard 1, from a single root cause across 5 call sites (one is a `[Theory]` with
four cases):

```
FixtureNotUsableException : The fixture does not compile, so no analyzer diagnostic can be trusted.
  /FixInput/Input.cs(1,7): error CS0246: The type or namespace name 'FluentAssertions' could not be found
```

These are **fixture hygiene, not latent defects.** `FluentAssertionsMigrationAnalyzer` registers exactly one
callback — `RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective)` — and never touches a
semantic model, so an unresolved namespace cannot change its outcome. The fixtures name a package the test
project deliberately does not reference, which is the point: the analyzer exists to migrate code *away from*
FluentAssertions.

Neither branch of the decision rule fits cleanly, so the guard is kept strong and the exception made
explicit rather than the guard weakened everywhere:

- Both overloads take `bool requireCompilableFixture = true`. The default protects every fixture.
- The 5 migration call sites pass `requireCompilableFixture: false` with a comment stating why the semantic
  model cannot matter there. Turning it off is a claim a reviewer can check, made where the claim applies.

R5.4 as originally written — opt-in on the new overload only — is therefore **not** taken: it would have left
the 12 SourceGenerators code-fix tests and the 9 Assertions ones running under the old silent semantics,
which the 242-test pass shows is unnecessary. The package bump stays `10.1.0`; the new parameter is optional
and defaulted, so no existing call site changes meaning except the 5 that opt out deliberately.

**Also delivered here, beyond the spike's question.** `CodeFixResult.Errors` / `ErrorText`: the fixed
solution is compiled across every project after the fix applies, so a test can assert that the *output*
compiles. Acceptance criterion 5 (get-only properties must not produce CS0535) cannot be written without it,
and a fix that breaks the class in another project is invisible to a single-project fixture.

### S3 — Does the properties bag survive the analyzer-driver round trip? *(gates R1.3, R1.5, 1h)*

**Question.** No analyzer in this repo has ever used `Diagnostic.Properties`. The whole of R2.5 depends on the
bag arriving intact at `RegisterCodeFixesAsync` — through the analyzer driver, through the test harness's
`WithAnalyzers(...).GetAnalyzerDiagnosticsAsync`, and through `BatchFixer`.

**Do.** Add the two keys to `INTF001`, assert them in a harness test, and read them back in a throwaway
branch of the fix. Check a `FixAll` invocation as well.

**Decision rule.** If the bag round-trips in all three paths → R1.3/R1.4 as written. If it survives the
analyzer but not the harness → fix the harness (it is ours) and proceed. If `FixAll` drops it → keep the bag
for the single-fix path and have `BatchFixer` fall back to a deterministic recompute, documented.

**Why it is a spike, not a task.** It is a one-hour empirical check whose failure would invalidate the design
of R2.4 and R2.5 — cheap to run, expensive to discover late.

**RESULT — run 2026-09-10. Clean pass. Proceed with R1.4 as written.**

A two-key bag (`TargetInterface`, `TargetMember`) survived `WithAnalyzers(...).GetAnalyzerDiagnosticsAsync`
intact — both keys present and correct on the diagnostic reaching the consumer. On the `cross-project`
fixture the full R2.5/R2.6 chain was then walked by hand and worked end to end:

```
through GetAnalyzerDiagnosticsAsync: 2 propert(ies)
    TargetInterface = global::IThing
    TargetMember    = Extra
    resolved interface symbol from property: IThing        ← fully-qualified match against classSymbol.Interfaces
    GetDocumentId -> Contracts/IThing.cs                   ← the OTHER project's document, O(1)
    FilePath match -> Contracts/IThing.cs
```

Two things worth recording. First, `Solution.GetDocumentId(SyntaxTree)` resolves a document in a *referenced*
project, which is the whole of R2.6 — confirmed, not assumed. Second, the `FilePath` comparison also
succeeded here, **because the rig sets `filePath:` on every document**. That is exactly D4's masking
mechanism reproduced from the other direction: the shipped comparison works right up until a document has no
path, and then fails silently. R5.1's "every document gets a real `filePath`" must therefore not be the only
thing standing between the suite and a vacuous pass — hence R5.3.

Variant A drops the `TargetInterface` key (see [S1](#s1--what-does-intf001-mean-for-a-class-implementing-several-interfaces-gates-r1-r2-2h)),
so only `TargetMember` ships. The round trip is proven for both.

`BatchFixer` was not exercised here; it consumes the same `Diagnostic` objects, so the bag cannot be lost in
transit, but the multi-action grouping under R2.5 is verified by test in M5 rather than assumed.

### S4 — Does narrowing the Workspaces reference change the packed output? *(gates R7, 1h)*

**Question.** R7.1 swaps the `Microsoft.CodeAnalysis` metapackage for an explicit
`Microsoft.CodeAnalysis.CSharp.Workspaces` + `ExcludeAssets="runtime"`. `SourceGenerators/CLAUDE.md` warns
that pack assets are collected in a `<Target>` at execution time and that evaluation-time globs over them are
a trap — so a reference change is not obviously inert for the nupkg.

**Do.** `dotnet pack -c Release` before and after, and diff the contents of both nupkgs — the file list under
`analyzers/dotnet/{cs,roslyn4.0/cs,roslyn4.9/cs}` and the `.nuspec` dependency group.

**Decision rule.** If both are byte-identical apart from the version → R7.1 as written, no release note. If
the *dependency group* changes but no assemblies move → still proceed; `PrivateAssets="all"` means consumers
never saw those dependencies. If any assembly is added or removed from `analyzers/` → stop, R7.1 is out of
scope for this PR and the metapackage stays; a packaging change does not belong in a correctness fix.

**Why it is a spike, not a task.** It touches packaging on a package with real consumers, and this repo has a
documented history of pack-asset globs behaving differently than they read. One hour of diffing settles it.

**RESULT — run 2026-09-10. Clean pass. Proceed with R7 as written.**

`dotnet pack -c Release` before and after, both producing `MintPlayer.SourceGenerators.10.21.1.nupkg`:

- **File list: byte-identical.** 18 entries, 11 under `analyzers/`, `diff` reports no differences.
- **`.nuspec`: identical**, dependency groups included.

The packed `analyzers/` tree carries no Workspaces assembly before or after — the only assembly
`AddAnalysisTimeDependencies` adds is `Microsoft.Extensions.DependencyInjection.Abstractions`, exactly as
intended. So today's output was already correct; R7 changes how that outcome is *guaranteed*, not what ships.

Implemented as `<PackageReference Remove="Microsoft.CodeAnalysis" />` against the metapackage inherited from
`eng/sourcegenerator.targets`, plus an explicit
`Microsoft.CodeAnalysis.CSharp.Workspaces` with `PrivateAssets="all" ExcludeAssets="runtime"`. Scoping the
removal to this csproj rather than editing the shared targets keeps the blast radius to the one project that
actually contains a `CodeFixProvider`.

## Migration / Backward Compatibility

- **`INTF001` becomes quieter, not louder.** N3's removal means multi-interface classes stop reporting false
  positives. A consumer suppressing `INTF001` wholesale to work around that can stop.
- **The code action changes shape.** "Add missing members to interface" becomes one action per member. Users
  wanting the old behaviour use *Fix all occurrences in…*, which now produces correct output rather than
  whichever interface came first.
- **`MintPlayer.SourceGenerators.Testing` is additive.** The existing `ApplyCodeFixAsync(string, string, string)`
  keeps its signature and semantics. `CodeFixResult` gains an optional trailing parameter; positional
  construction with the existing four arguments still compiles.
- **No breaking changes** to any generator, attribute, or generated output.

## Acceptance Criteria

1. [ ] S1, S2, S3, S4 run and their **RESULT** blocks written back into this document.
2. [ ] A test places the interface in a referenced project, exercises the fix, and asserts the *other*
       project's document changed — failing on master, passing after.
3. [ ] A test has a class implementing two interfaces, with the diagnostic on the second, and asserts the
       member lands on the second.
4. [ ] `record`, public field, public event and public nested type fixtures apply the fix without an
       exception from any path.
5. [ ] Get-only, init-only and set-only property fixtures produce output that **compiles**
       (`run.Errors.Should().BeEmpty(run.ErrorText)`).
6. [ ] A base-interface member is not re-declared on the derived interface.
7. [ ] The harness fails loudly on a non-compiling fixture and on a fix that changed no document.
8. [ ] No `NotImplementedException`, no `.First()`, and no `FilePath` string comparison remains in
       `InterfaceImplementationAnalyzer.CodeFix.cs`.
9. [ ] `MintPlayer.Spark` builds against the updated analyzer without `INTF001` on `OnDeleteRowAsync`, or the
       remaining report is confirmed correct.
10. [ ] Coverage for `Diagnostics/` does not regress against the Phase-2 baseline.
11. [ ] The packed `analyzers/dotnet/**` file list is unchanged by R7, per S4.
12. [ ] Documentation updated: package README for the new harness overload, rule description if S1 changes
        `INTF001`'s meaning.

## Out of scope

Genuinely not being done — not a parking lot:

- Porting the properties-bag pattern to the five `MintPlayer.Assertions` analyzers. They have no cross-project
  fix and no reason to carry one.
- A `.editorconfig` or `Directory.Build.props` for the repo, notwithstanding that their absence was noticed
  here.
- `RunGeneratorFixAsync` (Spark's second harness entry point, for diagnostics emitted by a generator rather
  than a `DiagnosticAnalyzer`). No `INTF001` need; adding it speculatively widens a published API.

## Version

`MintPlayer.SourceGenerators` **10.22.0** (from 10.21.1 — behaviour change to a shipped analyzer and code
fix). `MintPlayer.SourceGenerators.Testing` **10.1.0** (from 10.0.0 — additive API surface), subject to
[S2](#s2--can-the-loud-guards-be-turned-on-for-the-existing-overload-gates-r54-2h). Both are set inline in
their csprojs; merging to master packs and pushes them.
