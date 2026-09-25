# PRD: ValueComparerGenerator 12.0.1 downstream findings, and a version leak in every release since 10.20.2

Issue [#187](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/187), reported while moving
MintPlayer.AspNetCore.Tools' Endpoints generator to `MintPlayer.SourceGenerators.Tools` and
`MintPlayer.ValueComparerGenerator` 12.0.1 (MintPlayer/MintPlayer.AspNetCore.Tools#35). Branch
`fix/issue-187-downstream-findings`, from master `0c8191c`. Paths are relative to `SourceGenerators/` unless stated
otherwise.

## Overview

The issue reports three findings. An investigation checked each one against the code, with a throwaway repro for each,
and did not take the stated mechanism on trust. All three are confirmed. Two of them turned out to be wider than
reported, and one unrelated problem was found along the way. That problem is the most serious of the four.

| # | Finding | Verdict |
| --- | --- | --- |
| F1 | `IncludeRuntimeDependency="true"` breaks `GenerateDepsFile` (MSB4018) for a plain `ProjectReference` to a consuming generator | **Confirmed and reproduced.** The same line is in **eight** places, not one. |
| F2 | `JoinMethods.g.cs` puts an empty public type in `Microsoft.CodeAnalysis` of the consumer's assembly (CS1591) | **Confirmed.** Also, the Join overloads it is meant to hold have the **wrong signature**, generated equality members raise the same CS1591, and public copies are ambiguous across assemblies (CS0121). |
| F3 | The docs claim the Attributes dll is needed when the generator loads | **Confirmed wrong.** Roslyn loads and runs the generator without it. A host that finds generators by reflection does fail without it. |
| F4 | *(found along the way)* Every published package from 10.20.2 to 12.0.1 ships assemblies versioned **99.9.9.0** | **Confirmed on nuget.org.** The likely mechanism is the packaging tests overwriting the Release build that CI then packs. Spike V1 confirms it. |

**Decision:** fix all four in one PR.

## Problem statement: what the investigation found

### F1: the runtime-dependency flag breaks `GenerateDepsFile`

**Where it is.** Every `GetDependencyTargetPaths` target that adds an attributes dll to
`TargetPathWithTargetPlatformMoniker` sets `IncludeRuntimeDependency="true"`:

- The packaged `build/` targets:
  - `ValueComparerGenerator/MintPlayer.ValueComparerGenerator/build/MintPlayer.ValueComparerGenerator.targets:16`, conditioned on `IsRoslynComponent`. It is byte-identical to the 12.0.1 nupkg apart from line endings.
  - `Mapper/.../build/MintPlayer.Mapper.targets:16`
  - `SourceGenerators/.../build/MintPlayer.SourceGenerators.targets:15`
  - `Cli/.../build/MintPlayer.CliGenerator.targets:9`
- The in-repo csprojs:
  - `MintPlayer.ValueComparerGenerator.csproj:10`
  - `MintPlayer.Mapper.csproj:9`
  - `MintPlayer.SourceGenerators.csproj:10`
  - `MintPlayer.CliGenerator.csproj:10`

`eng/sourcegenerator.targets:139`, `eng/newtonsoftjson.targets:57-58` and `sourcegenerator_tools.targets:21,27` already
use `false`, as the Roslyn cookbook does. Git history shows no reason for `true`: it came in with #66 and was copied
into `build/` by #124. No PRD mentions it.

**How it fails.** A consuming generator A returns two `TargetPathWithTargetPlatformMoniker` items: `A.dll` and
`Attributes.dll`. Both carry `MSBuildSourceProjectFile=A.csproj`, and both become `ReferencePath` items in a test project
B that references A normally. `_ComputeUserRuntimeAssemblies` (SDK 10.0.401 `Microsoft.NET.Sdk.targets:1109-1122`)
marks both as user runtime assemblies: `A.dll` through CopyLocal, and `Attributes.dll` through the explicit `true`.
`SingleProjectInfo.CreateProjectReferenceInfos` then calls `Dictionary.Add` twice with the key `A.csproj`. The SDK
supports only one runtime assembly per project reference.

**Repro** (SDK 11 rc1; A package-references ValueComparerGenerator 12.0.1; B is a net10.0 exe with a normal
`ProjectReference` to A; every run was a clean build):

| Case | Build | Runtime |
| --- | --- | --- |
| Today | **MSB4018**, duplicate key `A.csproj` | — |
| `IncludeRuntimeDependency="false"` | passes | `Model.Equals` and an in-process `CSharpGeneratorDriver` run work. `typeof(Model).GetCustomAttributes()` throws `FileNotFoundException`: the dll is copied to bin but is missing from `B.deps.json`. |
| B also package-references `MintPlayer.ValueComparerGenerator.Attributes` | passes, with or without the fix | full reflection works |
| The fix, with C referencing A as `OutputItemType="Analyzer"` | passes | `Attributes.dll` is still passed as an analyzer, and the generator runs |

**Why this repo never hit it.** `MintPlayer.SourceGenerators.Tests.csproj:59-66` and
`Assertions.SourceGenerator.Tests.csproj:49` reference the generators with `ReferenceOutputAssembly="false"`. In-repo
generators get ValueComparerGenerator as an Analyzer `ProjectReference`, so the packaged `build/` targets are never
imported.

**Trap:** an incremental build can hide the bug, because a leftover `B.deps.json` let a "today" build succeed. Every
check here must start from a clean build.

### F2: `JoinMethods.g.cs`, and undocumented public generated members

**Where it is.** `ValueComparerGenerator/MintPlayer.ValueComparerGenerator/Generators/JoinMethodGenerator.Producer.cs`:

- line 10 names the file `JoinMethods.g.cs`;
- line 23 opens `namespace Microsoft.CodeAnalysis`;
- line 27 opens `public static class IncrementalValueProviderAdditionalEx`.

The class is meant to hold `Join` overloads for arity 6 to n. `MintPlayer.SourceGenerators.Tools` already ships arity 2
to 5 in `Extensions/IncrementalValueProviderExtensions.cs`. That is a library type, not generated into consumers, and
is not in scope.

**What controls it** (`JoinMethodGenerator.cs:17-33`):

- n comes from `[assembly: GenerateJoinMethods(n)]` and defaults to 5.
- `hasCodeAnalysisReference` says whether the project references Roslyn.
- The loop runs from 6 to n.

So every generator project **without** the attribute gets an empty public class. A project without a Roslyn reference
still gets an empty `namespace Microsoft.CodeAnalysis { }`.

**Measured (spike in scratchpad `f2`):**

1. **CS1591.** A Release build with `GenerateDocumentationFile` warns `JoinMethods.g.cs(14,25): CS1591
   'IncrementalValueProviderAdditionalEx'`. The `<auto-generated>` header doesn't suppress it. Neither
   `SuppressMessage` nor `.editorconfig` do either, as the reporter measured.
2. **The same warning from the equality output.** `GeneratedEquality.g.cs` raises CS1591 for `Equals(T)`,
   `Equals(object)`, `GetHashCode`, `EqualsCore` and `HashCore` on every **public** `[GenerateEquality]` model. The issue
   doesn't mention this, but it is the same class of problem, and the same downstream `DiagnosticSuppressor` is hiding
   it.
3. **The overloads have the wrong signature.** Line 35 emits parameters `previous, p2 … p{i}`, but the body uses only
   `previous` and `p{i}`. So the natural call `a.Join(b)` does not compile, and `a.Join(b, b, b, b, b)` is needed
   instead. The `i == 2` branch (line 40) is dead code, because the loop starts at 6.
4. **Public copies are ambiguous.** When Lib and App both have `[GenerateJoinMethods(7)]` and App references Lib, the
   App's call fails with **CS0121**. So being public is actively harmful.

Git history: public since f570fdf (#101), with no reason recorded. Nothing in the repo relies on it being public.

### F3: the Attributes dll is not needed when the generator loads

**Where the claim is made:**

- `eng/valuecomparergenerator.targets:13-17` ("Roslyn resolves it when LOADING the generator") and `:42` (the
  `<Error>` text says "a generator Roslyn cannot load").
- `CLAUDE.md:33-34`, `:64-68`, and the "Mistakes made" bullet about removing the payload entry as stale residue.
- `../Assertions/MintPlayer.Assertions/MintPlayer.Assertions.csproj:78-80`.
- `../docs/PRD-TestCoverage-Phase2.md:644-645`.
- `PackagingTests.cs:95,129,134`, which pin the dll in the package layout. That is correct, and stays.

**What really references the assembly.** The generated equality code references only `System.*` and
`global::MintPlayer.SourceGenerators.Tools.ValueEquality`, which is in Tools.dll (`ValueComparerGenerator.Discovery.cs:249`).
`[GenerateEquality]` has no `[Conditional]`, so it stays in metadata as a custom-attribute blob on internal model types.
The CLR resolves that blob only when something reflects over the type's attributes.

The requirement used to be real, but for a different reason than the docs give. Before #185,
`Tools/ValueComparers/ValueComparer.Registry.cs:137` called `type.GetCustomAttribute<…>(inherit: true)` on model types.
That resolved every attribute on the type, so a missing dll failed at **comparison** time, not at load time. #185
deleted that code.

**Measured (spike in scratchpad `f3`):**

- **Consumer build.** Mapper in Release, one consumer built against a full analyzer folder and one against a folder
  without the dll, `UseSharedCompilation=false`. Both exit 0 with no CS8032/CS8784, generate identical files
  (`diff -r`), and run.
- **IDE-style harness.** The generator loaded into a custom ALC and ran three times: the initial run, an unrelated edit,
  and a model edit that exercises the generated `Equals`. The stripped folder behaved exactly like the full one, with
  the same Cached/Modified step counts.
- **Reflection over the stripped assembly.** `GetCustomAttributes()`, `IsDefined(typeof(GeneratorAttribute))` and
  `GetCustomAttributesData()` over all types throw `FileNotFoundException` on 6 of 31 types. **A host that finds
  generators by reflection breaks.** The repo's own `GeneratorHarness.cs:229` does this, with the dll present. Roslyn's
  `AnalyzerFileReference` reads metadata, so it is unaffected.

**Not tested:** the Visual Studio, Rider/ReSharper and OmniSharp analyzer hosts (spike S3).

### F4: every release since 10.20.2 ships AssemblyVersion 99.9.9.0 (found along the way)

Measured directly on nuget.org: `mintplayer.valuecomparergenerator.attributes.12.0.1.nupkg`, downloaded fresh, holds
`MintPlayer.ValueComparerGenerator.Attributes.dll` with **AssemblyVersion 99.9.9.0**. The NuGet cache shows the same for
11.0.0, 12.0.0 and 12.0.1. 10.13.0 through 10.20.1 carry their own versions (`10.20.1.0` and so on). The investigation
also saw 10.20.2 affected.

`99.9.9` is `PackedFeed.Version = "99.9.9-packtest"`
(`MintPlayer.SourceGenerators.Tests/Packaging/PackedFeed.cs:26`). PackedFeed was added on 2026-09-03 in #172, which is
consistent with the first affected release.

**Suspected mechanism, to be confirmed by spike V1:**

1. `PackedFeed` runs `dotnet pack "<project>" -c Release -o <feed> -p:Version=99.9.9-packtest` **in the repo's own
   project directories** (`PackedFeed.cs:48`). That rebuilds the projects' Release `bin/` and `obj/` with version
   99.9.9.
2. `dotnet-build-master.yml` then runs `dotnet test --no-build` (line 52), which runs PackedFeed.
3. It then runs `dotnet pack --no-build --configuration Release` (line 75). That packs the rebuilt dlls under the nuspec
   version from the csproj (12.0.1), because `--no-build` doesn't rebuild.

**Why it matters:**

- The assembly version no longer identifies the release. Two packages carrying different code both claim 99.9.9.0,
  which defeats the analyzer loader's version checks and any binding redirect.
- Fixing it **lowers** the AssemblyVersion from 99.9.9.0 to 12.0.x. Something compiled against 99.9.9.0 then sees an
  older assembly. The main case is a downstream generator package built against Tools 12.0.1 and loaded next to a
  consumer's Tools 12.0.2 (spike V2).

## Goals

1. A plain `ProjectReference` to a generator that uses any of our generator packages builds. There is no MSB4018.
2. A consumer with `GenerateDocumentationFile` gets **no CS1591 from any generated file**, from this generator or from
   any other generator in the repo.
3. No generator adds a public type to a namespace the consumer doesn't own. `JoinMethods.g.cs` isn't emitted unless it
   has something in it.
4. The generated `Join` overloads take the two arguments their names promise.
5. Every doc, comment and error message states the **measured** reason the Attributes dll ships.
6. A published assembly's version matches its package version, and CI fails a release that doesn't.

## Non-goals

- **Dropping the Attributes dll from the packages.** The owner decided it keeps shipping under `analyzers/dotnet/cs`
  (open question 2).
- **The Tools package's own public `Microsoft.CodeAnalysis.IncrementalValueProviderExtensions`.** That is a library type
  the consumer references on purpose. It is not generated into the consumer's assembly.
- **Republishing or unlisting 10.20.2 to 12.0.1.** The fix ships as 12.1.0. The old versions are **deprecated**, not
  unlisted (M7).

## Design

### D1: `IncludeRuntimeDependency="false"` in all eight places (F1)

- Flip the four `build/*.targets` and the four csprojs to `false`, which matches `eng/sourcegenerator.targets`.
- In the README (packaging section), document the one side effect: a test project that runs a consuming generator
  in-process **and reflects over its model attributes** should package-reference
  `MintPlayer.ValueComparerGenerator.Attributes`. Running the generator itself doesn't need that.
- Spike S1 decides whether an xunit testhost needs the reference at all. It might resolve the dll from bin without a
  `deps.json` entry.

### D2: `JoinMethods.g.cs` (F2)

- In `ProduceSource`, return before writing anything when `!hasCodeAnalysisReference || n <= 5`.
  `Producer.Produce` already skips empty output (`Producer.cs:61-63`), so no file is emitted.
- Otherwise emit `internal static partial class IncrementalValueProviderAdditionalEx`. Internal alone ends the CS1591.
  `file` scope isn't possible, because consumer code in other files calls these methods.
- **The namespace stays `Microsoft.CodeAnalysis`.** Internal takes it out of the consumer's public surface, and this
  namespace is what makes the overloads show up next to Tools' arity 2–5 overloads without an extra `using`.
- Change the signature to
  `Join<T1..Ti>(this IncrementalValueProvider<(T1..T{i-1})> previous, IncrementalValueProvider<Ti> next)`.
  Delete the dead `i == 2` branch.

### D3: documented generated members (F2 extra)

- `GeneratedEquality.g.cs` emits `/// <inheritdoc/>` on `Equals(T)`, `Equals(object)` and `GetHashCode`. `EqualsCore`
  and `HashCore` get a one-line `/// <summary>`.
- Add the operators too, if they are emitted on public models.

### D4: guard test, "no generated file raises CS1591"

Modelled on `Guards/FixedFileSetGuardTests.cs`, and reusing its per-generator corpora:

- For every generator, make the corpus types **public**.
- Compile with `DocumentationMode.Diagnose` and assert no CS1591 whose location is in a generated tree.
- Keep a test-local generator that emits an undocumented public member as the negative control, so the guard is shown
  to be able to fail.
- Do the same in the Assertions generator's test project.

Any other generator it catches gets fixed in this PR, as in #186's "Bugs found along the way".

### D5: the load-time claim (F3)

- Rewrite every location listed in F3 to the measured statement: Roslyn does not need the dll to load or run the
  generator. It ships as a hedge against hosts that find analyzers **by reflection**, where it is measured to be
  required, and against any future reflection over model attributes.
- The `<Error>` in `eng/valuecomparergenerator.targets:42` stays an error, as the owner decided, reworded to that
  reason. The rule itself doesn't change: every generator package ships its attributes dlls directly under
  `analyzers/dotnet/cs`.
- Update the "Mistakes made" bullet in `CLAUDE.md` so the lesson survives with the right reason.

### D6: the 99.9.9.0 leak (F4)

1. **Isolate the packaging tests.** `PackedFeed` packs with its own `-p:BaseOutputPath=<Root>/bin/` and
   `-p:BaseIntermediateOutputPath=<Root>/obj/`, or into a copy of the tree, so it never writes the repo's Release
   output. Spike V1 picks the variant that restores and builds cleanly. `BaseIntermediateOutputPath` must be set before
   the SDK props are imported, so it may need to be passed as a global property, or `MSBuildProjectExtensionsPath` may
   be needed as well.
2. **Guard the release.** After `dotnet pack` in `dotnet-build-master.yml` and `pull-request.yml`, a step (a small
   script under `eng/`) opens every `*.nupkg` and fails when any `lib/`, `analyzers/` or `tools/` assembly's
   AssemblyVersion or FileVersion doesn't match the package version. PRs run it too, so a regression is caught before
   master.
3. **Pin it in a test.** `PackagingTests` asserts the packed dll's version equals `PackedFeed.Version`'s numeric part.
   That proves the stamp reaches the dll, while the CI guard proves the release doesn't get the test stamp.

### D7: docs and version

- CHANGELOG entry covering all four fixes, with a clear note for F2 and F4:
  - the Join class becomes internal, and its signature changes;
  - the AssemblyVersion drops from 99.9.9.0 to the real version.
- **12.1.0** for every SourceGenerators package, in lockstep.

## Tests to change

- `IncrementalOutputCachingTests.cs:291`. The JoinMethodGenerator fixture has no attribute, so after D2 its "must emit
  something" guard fails. Add `[assembly: MintPlayer.ValueComparerGenerator.Attributes.GenerateJoinMethods(7)]`.
- Snapshot tests whose output includes `JoinMethods.g.cs` or `GeneratedEquality.g.cs`. Regenerate them, and review the
  diffs: only `internal`, the signature and doc comments should change.
- `FixedFileSetGuardTests.cs:229` and `OtherGeneratorTests.cs:671` already use n ≥ 6. They're expected to stay green.

**New tests:**

- No attribute emits no `JoinMethods.g.cs`, and neither does a project without a Roslyn reference.
- `n = 7` emits an `internal` class, and `a.Join(b)` compiles for arity 6 and 7.
- The D4 guard.
- The D6 version assertion in `PackagingTests`.
- A packaging test for F1. A scratch generator project package-references the packed ValueComparerGenerator, and a
  test project `ProjectReference`s it normally. The test does a **clean** build and asserts no MSB4018. It fails
  against today's `true` (verify that).

## Spikes

| # | Question | Decides | Status |
| --- | --- | --- | --- |
| **R1** | Does F1 reproduce, and does `false` fix it? | D1 | **Done.** See F1. |
| **R2** | Does F2 reproduce? Are there more problems in the same producer? | D2, D3 | **Done.** See F2: CS1591, CS0121, wrong signature, CS1591 in equality output. |
| **R3** | Does Roslyn need the Attributes dll to load the generator? | D5 | **Done: no.** Hosts that reflect do need it. See F3. |
| **S1** | With D1 and without an Attributes `PackageReference`, does an **xunit** test project that reflects over a consuming generator's model attributes still work? The testhost might probe bin. | D1 README wording | Open. Turn repro B into an xunit project, apply `false`, add a test calling `typeof(A.Model).GetCustomAttributes()`, then do a clean `dotnet test`. **Pass:** the README note says "only for non-test hosts". **Fail:** the note says "add the PackageReference". |
| **S2** | After D2, is an App on the internal version still ambiguous against a Lib built with 12.0.1's public class? | D7 CHANGELOG note | Open. Reuse the `f2` Lib and App: App gets an internal copy by hand, Lib keeps the public one. Build, and look for CS0121/CS0436. **Still ambiguous:** the CHANGELOG says both sides must upgrade. |
| **S3** | Do Visual Studio and Rider load and run a generator whose analyzer folder lacks the Attributes dll? | — | **Dropped.** The owner decided the dll ships regardless (open question 2). |
| **V1** | Is the 99.9.9.0 leak caused by PackedFeed overwriting Release `bin/` before `pack --no-build`? Which isolation keeps PackedFeed working? | D6.1 | Open. On a clean clone: `dotnet build -c Release`, then check a dll's version; run only the `PackagingTests` and check again; then `dotnet pack --no-build -c Release` and check the nupkg. **Confirmed** if it is 99.9.9.0 only after the tests. Then try the `BaseOutputPath`/`BaseIntermediateOutputPath` variant and the copy-the-tree variant, and keep the one after which the repo's Release bin keeps its real version and PackagingTests still pass. |
| **V2** | Does lowering the AssemblyVersion from 99.9.9.0 to 12.0.x break a consumer that mixes generator packages built at different times? | D7 note, open question 1 | Open. Consumer X references two generator packages: G1, built against Tools 12.0.1 (asm 99.9.9.0), and G2, built against Tools packed from this branch (asm 12.0.x). Build X with `UseSharedCompilation=false`, then again on the build server. Look for CS8032, CS8784 and missing generated files, and note which Tools copy each generator loads. **Clean:** a patch or minor bump is fine. **Broken:** the CHANGELOG says so and the version bump is major. |

## Milestones

Tests are batched to the end, per the house rule. Verify each milestone by reading the code and type-checking.

1. **M0: spikes V1, S1, S2, V2.** Record each result in the table above and change the design if one contradicts it.
2. **M1: F1.** D1 in all eight places, plus the README note in the wording S1 settles.
3. **M2: F2.** D2 (Join), D3 (doc comments) and the D4 guard with its negative control. Fix any other generator the
   guard catches.
4. **M3: F3.** D5 in every location listed.
5. **M4: F4.** D6: PackedFeed isolation, the CI version guard script in both workflows, and the PackagingTests
   assertion.
6. **M5: tests, docs and version.**
   - Update the listed tests and regenerate the snapshots.
   - Add the new tests.
   - D7 CHANGELOG entry and version bump, in lockstep.
7. **M6: verification and PR.**
   - A full, **clean** Release build and test run.
   - Check that the packed nupkgs carry the right AssemblyVersion.
   - CI is green, including `sourcegenerators-benchmark`. Expect no change: D2 removes an empty file, and D3 adds only
     trivia.
8. **M7: after the 12.1.0 release (owner).**
   - Check that the nuget.org 12.1.0 dlls report AssemblyVersion 12.1.0.0.
   - Deprecate 10.20.2 through 12.0.1 of every affected package on nuget.org, with reason "critical bugs" and the
     message "Assemblies carry AssemblyVersion 99.9.9.0; use 12.1.0 or later".
   - Deprecation is done in the nuget.org UI or through its API, not with the `dotnet` CLI. So this step needs the
     owner's nuget.org account.

## Acceptance criteria

1. The F1 packaging test builds a plain `ProjectReference` to a consuming generator cleanly, and it fails against
   `true`.
2. No generator emits CS1591 into a consumer with documentation enabled (D4 guard, with a negative control).
3. `JoinMethods.g.cs` is emitted only for n ≥ 6 with a Roslyn reference, is `internal`, and `a.Join(b)` compiles.
4. No doc, comment or error message says the Attributes dll is needed when the generator loads.
5. Every packed assembly's AssemblyVersion and FileVersion match its package version. The CI guard enforces it on PRs
   and on master, and repo Release output is unchanged after the packaging tests run.
6. The full clean Release suite passes, and CI is green.

## Open questions for the owner

All four were resolved by the owner.

1. **Version.** **Resolved: 12.1.0**, for every SourceGenerators package in lockstep. V2 now only decides the wording
   of the CHANGELOG note.
2. **The Attributes dll.** **Resolved: keep shipping it,** directly under `analyzers/dotnet/cs`, beside the generator.
   That is the owner's convention for every generator package. The dll stays even if S3 passes, so S3 is dropped.
3. **The `<Error>` in `eng/valuecomparergenerator.targets`.** **Resolved: it stays an error, and its reason is
   corrected** (D5).
4. **The affected releases on nuget.org (10.20.2 to 12.0.1).** **Resolved: deprecate them** once 12.1.0 is
   published, with a message pointing at 12.1.0 (M7).

**About the Join overloads (F2):** the owner confirmed they should become internal. Tools ships the arity 2–5
overloads, and `[GenerateJoinMethods(n)]` generates arity 6 to n. The signature fix in D2 is still needed. Every Tools
overload chains as `previous.Join(next)`, with two arguments, and all 17 `.Join(` calls in the repo use that form. The
generated overloads for arity 6 and up instead take `previous` plus five more providers, and ignore all but the last.
No code in the repo calls arity 6 or higher: the only `[GenerateJoinMethods]` use outside the tests is commented out,
in `TestProjects/ValueComparerDebugging/Program.cs:3`. So the mismatch never surfaced. D2 makes arity 6+ chain exactly
like arity 2–5.
