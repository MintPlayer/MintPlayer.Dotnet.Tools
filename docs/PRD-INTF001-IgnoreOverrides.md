# PRD: INTF001 ignores `override` members

## Overview

`InterfaceImplementationAnalyzer` (`INTF001`) reports public members of a class that are absent from every
interface the class implements, and `InterfaceCodeFixProvider` offers to add them to the interface. Issue
[#191](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/191) asks it to ignore `override` members.

An override doesn't add any public surface. It replaces the implementation of a member that a base class already
declares, and that base class owns the contract. Reporting it is a false positive, and the code fix makes it worse
by copying `ToString()` or `Work()` onto an interface that has no business declaring it.

Line numbers are against `SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/Diagnostics/` unless stated
otherwise. All findings below were verified against master `19e810c`.

## Problem Statement

Today, in `SourceGenerators/TestProjects/InterfaceImplementationDebugging/Overrides.cs` (added for this issue),
every one of these lines warns:

```csharp
public interface IWorker { void Declared(); }

public abstract class WorkerBase
{
    public abstract void Work();
    public virtual string Name { get; set; } = string.Empty;
    public virtual void Reset() { }
}

public class Worker : WorkerBase, IWorker
{
    public void Declared() { }
    public override string ToString() => nameof(Worker);   // INTF001
    public override bool Equals(object? obj) => ...;       // INTF001
    public override int GetHashCode() => 0;                // INTF001
    public override void Work() { }                        // INTF001
    public override string Name { get => base.Name; }      // INTF001
    public sealed override void Reset() { }                // INTF001
    public void Extra() { }                                // INTF001 — correct
}
```

A build of that project emits 11 INTF001 warnings, and 9 of them are overrides. Any class implementing an interface
that overrides `ToString` is flagged. So is any class that implements an interface and also derives from an
abstract base. Every one of these warnings has to be suppressed by hand with `[NoInterfaceMember]`.

The code fix makes it actively harmful. For each of these the fix is offered, applies cleanly, and adds the member
to the interface (`string ToString();`, `void Work();`, `string Name { get; }`). That compiles, so nothing stops it.

### In the wild

- **MintPlayer.AspNetCore.SpaServices:** `SpaRouteItem : ISpaRouteItem, ISpaRouteBuilder` overrides `ToString()`
  (`MintPlayer.AspNetCore.SpaServices.Routing/Data/SpaRouteItem.cs:73`). A real build log from 2026-09-29 shows
  `warning INTF001: Public member 'ToString' is not defined in any implemented interface (ISpaRouteItem)`.
- **MintPlayer.Spark (likely, not confirmed in an IDE):** actions classes derive from
  `DefaultPersistentObjectActions<T>` and also declare the marker interface `ISparkOwnsRowSecurity`. Their overrides
  (`OnNewAsync`, `OnLoadAsync`, `GetRowFilterAsync`, …) would light up wherever that interface has source locations.
  A command-line build sees the interface as metadata only, so nothing is reported there.
- **No suppressions were found.** There is no `#pragma`, `NoWarn`, `.editorconfig` entry, or `[NoInterfaceMember]`
  on an override in any repo that consumes the package. People live with the warnings.

## Root cause

The analyzer and the code fix choose their candidate members through one shared function,
`InterfaceMemberCandidates.In` (`InterfaceImplementationAnalyzer.Members.cs:31-42`). It keeps a member if it is
public, non-static, referenceable by name, not implicitly declared, a method or property, not a constructor, and not
marked `[NoInterfaceMember]`. **No filter looks at `IsOverride`**. `IsOverride`, `OverriddenMethod` and
`OverriddenProperty` appear nowhere in `Diagnostics/`.

`type.GetMembers()` returns only declared members, so inherited members are already skipped. An override, though, is
a declaration on the derived type, so it gets through.

The code fix calls the same `In()` at `CodeFix.cs:73-75` and `:122-124`, so **a single filter fixes both**.

## Verified behaviour inventory

A temporary probe test was run through the real analyzer (`GeneratorHarness.RunAnalyzerAsync`) and the real code fix
(`CodeFixHarness.ApplyAsync`). In every scenario `IFoo` declares only `void Declared();`.

| # | Scenario | Today | Fix offered today | After this PRD |
|---|---|---|---|---|
| a | `public override` ToString / Equals / GetHashCode | 3 × INTF001 | yes, adds `string ToString();` | silent |
| b | override of abstract `Base.Work()` | INTF001 | yes | silent |
| c1 | `protected override SendAsync` (`DelegatingHandler`) | silent (not public) | no | silent |
| c2 | `public override` of a `public virtual` base method | INTF001 | yes | silent |
| d | `public sealed override` | INTF001 | yes | silent |
| e | property override of the getter only | INTF001 | yes, adds `string Name { get; }` | silent |
| f | `public new void Work()` (hiding) | INTF001 | yes | **still reported** |
| g | `record Thing(string Name) : IFoo` | INTF001 on `Name` | yes | **still reported** (see Non-Goals) |
| g2 | record plus hand-written `override ToString()` | INTF001 on `ToString` | yes | silent |
| h | `Base : IBar { abstract Work }`, `Thing : Base, IFoo` overrides `Work` | INTF001 | yes, adds to IFoo | silent |
| — | control: `public void Extra()` | INTF001 | yes | **still reported** |

Records' compiler-synthesized `ToString`, `Equals`, `GetHashCode` and `PrintMembers` are already excluded by
`!IsImplicitlyDeclared`. Only an override that someone writes by hand gets through today.

## Goals

1. INTF001 never reports a member declared with `override`, including `sealed override`, whatever its base class.
2. Because the code fix shares the candidate filter, it never offers to copy an override onto an interface.
3. Hiding (`new`) members and every other genuinely new public member are still reported, exactly as today.
4. Each scenario in the table above has a regression test, and the debugging test project has a live example of
   each one.

## Non-Goals

1. **Overrides whose base member is not on any interface are not reported either.** That includes
   `BackgroundService.ExecuteAsync`, `Controller` actions and `JsonConverter.Read`. The issue asks to ignore
   overrides wholesale. The base class defines the contract whether or not an interface also declares it. This also
   agrees with Non-Goal 3 of `PRD-INTF001-CodeFixCorrectness.md`, which leaves interfaces reached through a base
   class out of scope.
2. **Positional record properties (scenario g) stay reported. This is a decision, not a deferral.** Overrides are
   excluded because they add no public surface; they re-implement a member the base class already declares. A
   positional parameter is the opposite case. `record Thing(string Name) : IFoo` declares a new public `Name`, and
   it means exactly the same as `public string Name { get; init; }` written in the body, which INTF001 reports. If
   the two syntaxes gave different verdicts, identical records would be treated differently. Users who don't want
   the property on the interface opt out per member with `[property: NoInterfaceMember]`, as for any other
   property.
3. **`SatisfiedNames` keeps reading `type.Interfaces`, not `AllInterfaces`.** Scenario h is fixed by the override
   filter, since a non-override member can't re-declare a base-class interface member without `new`. Widening
   the set is a separate semantic change with no reported need.
4. **No new rule ID, and no severity or category change.**

## Requirements

### R1 — Exclude overrides from the candidate set

In `InterfaceMemberCandidates.In` (`Members.cs:31-42`), add `&& !m.IsOverride` to the first `Where`.

- `ISymbol.IsOverride` is true for `override` and `sealed override` methods and properties, so `sealed` needs no
  special case.
- A property that overrides only one accessor is an override as a whole (`IPropertySymbol.IsOverride`), and is
  excluded as a whole.
- `new` members have `IsOverride == false` and stay candidates.
- The filter lives in the shared function, so the analyzer (`InterfaceImplementationAnalyzer.cs`) and the code fix
  (`CodeFix.cs:73-75`, `:122-124`) both pick it up with no further change. Add a one-line comment on the clause
  saying why: the overridden base member owns the contract.

### R2 — Analyzer tests

In `SourceGenerators/MintPlayer.SourceGenerators.Tests/Diagnostics/AnalyzerTests.cs`, class
`InterfaceImplementationAnalyzerTests`, add `[Fact]` tests in the existing style
(`GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", [source])` with FluentAssertions):

- `ItIgnoresOverridesOfObjectMembers` covers scenario a.
- `ItIgnoresOverridesOfAbstractAndVirtualBaseMembers` covers b and c2.
- `ItIgnoresSealedOverrides` covers d.
- `ItIgnoresPropertyOverridesOfASingleAccessor` covers e.
- `ItIgnoresOverridesInRecords` covers g2.
- `ItIgnoresOverridesOfMembersDeclaredOnABaseClassInterface` covers h.
- `ItStillReportsHidingMembers` covers f: exactly one INTF001, for the `new` member.
- `ItReportsOnlyTheNonOverrideMember` mixes overrides with one `public void Extra()` and expects exactly one
  diagnostic, naming `Extra`. This guards against the filter being too broad.

### R3 — Code-fix tests

In `Diagnostics/CodeFixTests.cs`, class `InterfaceImplementationCodeFixTests`, use
`CodeFixHarness.ApplyAsync("InterfaceImplementationAnalyzer", "InterfaceCodeFixProvider", source)`:

- `ItOffersNoFixForAnOverride`: when the only public non-interface member is `public override string ToString()`,
  `result.Applied` is false.
- `ItAddsTheNonOverrideMemberOnly`: given an override and `public void Extra()`, the fixed interface contains
  `void Extra();` and does not contain `ToString`. The harness applies the fix for the first diagnostic, so this
  also proves the override produced none.

### R4 — Live example in the test project

`SourceGenerators/TestProjects/InterfaceImplementationDebugging/Overrides.cs` has already been added during this
investigation. It covers scenarios a, b, d, e, f, g2 and h, with a comment on each member saying whether INTF001
should fire. Building that project after R1 must leave exactly **two** INTF001 warnings from that file:
`Worker.Extra` (line 37) and `Hider.Reset`, the `new` member (line 63). Baseline before R1: 11.

### R5 — Release bookkeeping

- Bump every package under `SourceGenerators/` in lockstep from `12.1.0` to **`12.1.1`**. This is a patch: it removes
  false positives and adds no API.
- Add a `## 12.1.1` entry to `SourceGenerators/CHANGELOG.md` under `### Fixed`: "INTF001 no longer reports
  `override` members, and its code fix no longer offers to copy them onto the interface (#191)."
- `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` need no change, because the rule ID, category
  and severity are all unchanged.
- No README documents INTF001's member filter (checked with a grep for `INTF001` / `NoInterfaceMember` across the
  READMEs), so no documentation change is needed. The one README that pins a version,
  `ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md`, follows the bump.

## Technical Design

The change is one predicate in one function. The design question is *which* predicate:

| Option | Excludes | Verdict |
|---|---|---|
| `!m.IsOverride` | every `override` / `sealed override` | **Chosen.** It matches the issue and is simple and total. |
| Exclude only overrides of `System.Object` members | a, g2 | Rejected. b, c2, d, e and h remain false positives. |
| Exclude overrides whose overridden member is declared on some interface | h only | Rejected. The base class owns the contract either way. It costs more and is wrong for a and b. |
| `!m.IsOverride && !m.IsNew`-style hiding check | f too | Rejected. A `new` member is genuinely new surface, and reporting it is correct. |

Users who *want* an overridden member on the interface can still add it by hand. INTF001 only stops nagging about
it.

**The one accepted loss.** Suppose `class Foo : FooBase, IFoo`, where `FooBase` is in source and implements no
interface, and its `virtual Work()` is not on `IFoo`. Before this change, the override in `Foo` was the only thing
that surfaced "`Work` can't be reached through `IFoo`". The case is narrow, and the base class owns that contract.
A middle ground would skip only overrides whose root definition is in metadata or on `System.Object`, but it was
rejected: it costs a walk up the override chain, and it keeps the abstract-base case (b), which is the common one,
as a false positive.

## Milestones

1. **M1:** R1, the filter, plus R2 and R3, the tests.
2. **M2:** R4, verifying the debugging project build shows exactly the two expected warnings, and R5, the version
   bump and changelog.

Per the repo's convention, run the test suite once at the end, after M2:
`dotnet test SourceGenerators/MintPlayer.SourceGenerators.Tests/MintPlayer.SourceGenerators.Tests.csproj`.

## Migration / Backward Compatibility

The change only narrows behaviour, and only for overrides. Code that suppressed override warnings with
`[NoInterfaceMember]` keeps compiling, and the attribute becomes redundant on those members (harmless).
Projects with `TreatWarningsAsErrors` can only get fewer errors. Nothing that was silent before starts warning.

## Acceptance Criteria

- [ ] `InterfaceMemberCandidates.In` excludes `IsOverride` members, with a comment saying why.
- [ ] The analyzer tests in R2 pass, including `ItStillReportsHidingMembers` and `ItReportsOnlyTheNonOverrideMember`.
- [ ] The code-fix tests in R3 pass.
- [ ] Building `InterfaceImplementationDebugging` reports INTF001 only for `Worker.Extra` and `Hider.Reset` in
      `Overrides.cs`.
- [ ] Every package under `SourceGenerators/` is at `12.1.1`, and the CHANGELOG has the entry.
- [ ] The full `MintPlayer.SourceGenerators.Tests` suite is green.
