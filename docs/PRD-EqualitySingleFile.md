# PRD: the equality generator emits one fixed file, not one file per model

Follow-up to [#185](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/pull/185), the generated `IEquatable<T>` work
([`PRD-GeneratedEquality.md`](PRD-GeneratedEquality.md)). Branch `feat/equality-single-file`, from master `72d3dbe`.
Paths are relative to `SourceGenerators/` unless stated otherwise.

## Overview

`ValueComparerGenerator` writes one source file **per model**, named after the model's fully qualified type
(`HintNameOf`, `ValueComparerGenerator/MintPlayer.ValueComparerGenerator/Generators/ValueComparerGenerator.Discovery.cs:247`):

```
MintPlayer.Spark.SourceGenerators.Models.TranslationsAssemblyInfo.Equality.g.cs
```

The repo convention is that a generator emits a **fixed set of files**, not one per class or per resource. That
keeps both the file count and each file name bounded. The equality generator breaks that convention: its file
count grows with the number of models, and each name grows with namespace depth, nesting and generic arity.

**Decision:** emit one file per compilation, `GeneratedEquality.g.cs`, holding the equality members of every
model. The generated code stays the same, and so do equality behaviour and the public API. Only the file layout
changes.

## Problem statement: what the investigation found

### P1: a long enough file name is a hard build error (measured, spike L1)

With `EmitCompilerGeneratedFiles=true`, generated files are written to disk at:

```
<ProjectDir>\obj\<Config>\<TFM>\generated\<GeneratorAssembly>\<GeneratorFullTypeName>\<hintName>
```

For a real Spark model that is **299 characters**. Spike L1 built a consumer at increasing lengths on this machine.
`LongPathsEnabled` is `1` here.

| Toolchain | 304-char path (110-char file name) | 449-char path (255-char file name) | 256+-char file name |
| --- | --- | --- | --- |
| `dotnet build` (SDK 11 rc) | OK | OK | **fails** |
| MSBuild 18.11 (its bundled .NET `csc`) | OK | — | **fails** |
| MSBuild 18.11, .NET Framework `csc` | OK | — | — |

- **The failure is not a warning.** It is `CS0016: Could not write to output file '…\<hint>'` with the OS error
  `ERROR_INVALID_NAME`, and the build fails.
- **What fails is a single path component over 255 characters,** not the total path. A per-model name has no
  upper bound: a deep namespace, nested types and generic arity each lengthen it. So some model will eventually
  cross 255.
- **With `LongPathsEnabled=0`,** which is the Windows default on machines and images that never opted in, the total
  path limit of 260 applies as well. That case is **untested**, because it needs a registry change. It is expected
  to fail the same way, at a much shorter length.
- **Without `EmitCompilerGeneratedFiles`** nothing is written, and the build is unaffected at any length. So the
  failure hits whoever turns emission on to debug a generator. Four TestProjects in this repo have it on.

### P2: the file count grows with the models

Spark has 25 models, so 25 files. Visual Studio's *Analyzers* node, the `obj/generated` folder and every snapshot
test grow with them. The repo's other generators emit a fixed set, so their output stays predictable.

### P3: other generators, surveyed (context, not in scope)

- **Fixed names everywhere else.** Every other generator in this repo and in Spark emits a fixed set of names.
- **One other growing name.** AspNetCore.Tools' `EndpointClientGenerator` emits one file per referenced server
  assembly (`<LastSegment>Client.g.cs`). Those names stay short (at most about 50 characters), so none comes near
  the 255 component limit.
- **Some fixed-name paths already pass 260 in total.** That comes from long consumer folders and long generator type
  names, not from the file name:

  | Generator | Consumer | Total path |
  | --- | --- | --- |
  | Spark `SubscriptionWorkerRegistrationGenerator` | `libs\identity_provider\MintPlayer.Spark.IdentityProvider` | 271 |
  | Assertions `EquivalencyRegistrationGenerator` | `Assertions\MintPlayer.Assertions.Benchmarks` | 264 |
  | Spark `CustomActionsRegistrationGenerator` | Spark | 261 |

  With long paths enabled they build (L1). With them disabled they would probably fail (untested). A file name
  cannot fix those, so they are listed under non-goals.

## Goals

1. **One fixed name.** `ValueComparerGenerator` emits exactly one equality file, `GeneratedEquality.g.cs`, per
   compilation, whatever the number, depth or shape of the models. It emits no file when there are no models.
2. **No behaviour change.** Every model gets byte-for-byte the same members as today, apart from where they sit
   in the file. The equality, runtime and diagnostics tests pass unchanged.
3. **Caching holds.** An unrelated edit leaves every step Cached. A model edit re-runs only that model's transform
   and then rebuilds the single file.
4. **Deterministic output.** Models are ordered by fully qualified name, compared ordinally, so the file is
   identical across machines and runs.
5. **A guard.** A test enforces the fixed-set convention for every generator in this repo, so a per-input file
   name can't creep back in.

## Non-goals

- **Total path length of fixed-name generators (P3).** It comes from the consumer's folder depth and the generator's
  type name, and it is only an issue with long paths disabled, which is untested. Shortening generator type names
  would be a breaking rename, so it is not in scope.
- **AspNetCore.Tools' `EndpointClientGenerator`.** Its per-assembly names are bounded and short. If it ever needs
  changing, that belongs to that repository.
- **The generated members themselves.** Their content doesn't change.

## Design

### D1: pipeline

The shape follows the Assertions `EquivalencyRegistrationGenerator`
(`Assertions/MintPlayer.Assertions.SourceGenerator/Generators/EquivalencyRegistrationGenerator.cs:70-80`), which
already combines two providers into one file:

```csharp
var models = Models(rootsProvider).Collect()
    .Combine(Models(derivedProvider).Collect())
    .Select(static (p, ct) => p.Left.Concat(p.Right)
        .DistinctBy(m => m.FullName)                          // defensive: the providers don't overlap today
        .OrderBy(m => m.FullName, StringComparer.Ordinal)
        .ToEquatableArray());                                 // S2: a plain ImmutableArray would report Modified

context.ProduceCode(models.Select(static (m, ct) => (Producer)new EqualityProducer(m)));
```

- **`Models(...)` keeps the per-item step.** It does today's `Select(t => t.Model).WithTrackingName(ModelsStep)` and
  `Where(m is not null)`. The per-model caching assertions on `ValueComparerGenerator.Models` keep their meaning.
- **The combined step returns `EquatableArray<ClassDeclaration>`.** Spike S2 of the previous PRD showed that a step
  building a new collection reports Modified unless its result has sequence equality. L2 confirmed this for this
  shape: an unrelated edit leaves the sorted step and the output Cached.
- **The diagnostics pipeline** (`ValueComparerGenerator.cs:62-67`) stays as it is. It already collects per type.

### D2: producer

- **`EqualityProducer` takes the whole sorted array.** It writes `#nullable enable` and the header once, then one
  block per model.
- **Namespaces follow `CliCommandProducer`** (`Cli/MintPlayer.CliGenerator/Generators/CliCommandSourceGenerator.Producer.cs:13-60`):
  models are grouped by their declared `Namespace`, and each group gets a `namespace X { }` block. Global-namespace
  models are written with no block. The output never uses a file-scoped namespace, so blocks and top-level types
  can coexist.
- **The per-model writer is today's `ProduceSource` body.** That covers reopening the containing types, the
  `partial <keyword>` declaration and the members. So the text generated for each model doesn't change.
- **Concatenation is safe:**
  - The generated code has no `using` lines, and every name is `global::`-qualified.
  - Cached comparer fields (`s_valueEquality{N}`) are members of each type, not file-level.
  - The same containing type may be reopened several times in one file; partial declarations allow that.
- **No models means no output.** The filename is `string.Empty`, so `Producer.Produce` emits nothing, as
  `CliCommandProducer` does. Tests already expect `GeneratedSources` to be empty then.
- **`ClassDeclaration.HintName` and `HintNameOf` are deleted.** The hint name is also dropped from the model's
  `Equals`/`GetHashCode`.

**Trade-off, accepted:** `Producer.Produce` turns an exception into one `MPSG001` for the file. One model that
throws now drops every model's equality, not only its own. A throw there is a generator bug either way, and the
compile errors that follow point straight at it. Per-model isolation would need the producer to write each
model into a buffer and report per model, which is more code than the risk is worth.

### D3: the file name

`GeneratedEquality.g.cs`:
- It is 23 characters, against today's unbounded names.
- It doesn't collide with the same generator's `JoinMethods.g.cs`. Hint names must be unique per generator only.
- It says what the file holds.

### D4: guard test, "every generator emits a fixed set of files"

- **What it does:** a theory over every generator in this repo (the harness already enumerates them for
  `IncrementalOutputCachingTests`). It runs each generator over the same corpus twice, once with 1 decorated type
  and once with 5 (models, services, mappers, commands and so on, per generator), and asserts that **the set of
  hint names is identical**.
- **What it catches:** file names that depend on type names, and file counts that grow with inputs. It doesn't
  hard-code any name.
- **Scope:** it runs against this repo's generators only. Spark and AspNetCore.Tools can adopt the same test.

Spike S1 decides whether the corpus can be shared or has to be per generator.

### D5: docs and version

- **Docs:**
  - `ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md:39` ("one `<Type>.Equality.g.cs` per
    model") → one `GeneratedEquality.g.cs`.
  - `docs/PRD-GeneratedEquality.md` D1 (line 133): add a note pointing here. The rest stays as the historical
    record.
  - Code comments: `ValueComparerGenerator.cs:10` and `ValueComparerGenerator.Producer.cs:7`.
  - `SourceGenerators/CLAUDE.md`: a short rule under the payload section, *"a generator emits a fixed set of
    files; never derive a hint name from a type or resource name"*, pointing at the D4 guard.
- **`SourceGenerators/CHANGELOG.md`:** a `12.0.1` entry. File names are not API, so this is not a breaking change.
  Record it as *Changed*, with the reason (`CS0016` at 256+ characters).
- **Version:** 12.0.1, lockstep across the SourceGenerators packages, following the convention from #183/#185.
  See the open question below.

## Tests to change

Found by the investigation (file:line refs are against master `72d3dbe`):

- **`Snapshots/ProducerSnapshotTests.cs`:** all 14 `Equality*.verified.txt` files, plus
  `ProducerSnapshotTests.ValueComparers.verified.txt`, are regenerated. Seven of them hold 2 to 4 per-model files
  that become one. The per-model text inside must be identical, and the M2 check verifies that.
- **`Generators/OtherGeneratorTests.cs`:**
  - `:387` and `:455-457` assert per-model hint names. They become: exactly one source named
    `GeneratedEquality.g.cs`, containing each expected declaration.
  - `:390`, `:434`, `:458-459` and `:476` use `SourceFor("Demo.X.Equality.g.cs")`. They switch to
    `SourceFor("GeneratedEquality.g.cs")`.
  - `:490` (a global model gets no `namespace`) needs a model-scoped assertion instead of a whole-file one.
  - `:504` (no models means no output) is unchanged.
- **`Diagnostics/ValueComparerGeneratorDiagnosticsTests.cs`:**
  - `:29` and `:43` switch to the single file.
  - `:65-66` ("Leaf has no file, Node does") become content assertions: the file contains `partial class Node` and
    not `partial class Leaf`.
  - `:82`, `:219`, `:249` and `:263` are unchanged, because `ModelsStep` stays per-item.
- **`Generators/IncrementalOutputCachingTests.cs`:** the ValueComparer case (`:270-289`) and its relevant edit
  (`:375-381`) assert no file names, so they are expected to pass unchanged (L2).
- **Unaffected** (no file-name dependence): `EqualityCompileMatrixTests`, `GeneratedEqualityBehaviourTests`,
  `RoslynTypeInModelAnalyzerTests`, `IncrementalityTests`, and the benchmark project.
- **New:**
  - The D4 guard.
  - A test that 0 models emits no file.
  - A test that the order is deterministic: the same models declared in shuffled files give an identical text.
  - A long-name regression: a model with a 300-character namespace still emits `GeneratedEquality.g.cs`.

## Spikes

| # | Question | Status |
| --- | --- | --- |
| **L1** | Does a long generated path break the build, and on which toolchain? | **Done.** A file name over 255 characters fails with `CS0016` on `dotnet build` and MSBuild; the total path is fine to at least 449 with long paths enabled. See P1. |
| **L2** | Does one collected file keep caching, and what does it cost? | **Done.** See the results below. |
| **H** | Which other generators have growing or long file names? | **Done.** See P3. |
| **P** | Can the per-model output be concatenated safely, and what depends on per-model names? | **Done.** See D2 and *Tests to change*. |
| **S1** | Can the D4 guard use one shared corpus, or does it need a small corpus per generator? | **Done: per generator.** Each generator triggers on a different attribute or shape, so each gets its own item template, repeated 1 and 5 times, one file and one namespace (`Demo.N{i}`) per item. The guard also asserts the 5-run output names all five items, so a corpus the generator ignores can't pass vacuously. `JoinMethodGenerator` has no per-type input (one assembly attribute), so its corpus varies the join count and the number of classes, without the item-name check. **Detection:** the guard copied into a scratch worktree of master (`72d3dbe`) fails exactly the `ValueComparerGenerator` case (`Demo.N1.Item1.Equality.g.cs` vs 5 names) and passes the other 8; a test-local `PerTypeHintNameGenerator` is kept as a permanent negative control. On this branch it passes on all 9 generators here and on both Assertions generators (their own test project). |
| **S2** | After the change, is each model's text in the single file byte-identical to today's per-model file? | Planned, in M2. Method: for every snapshot fixture, split master's per-model outputs and the new single file into per-model blocks, and diff them. Pass: 0 differing lines apart from header and namespace framing. |
| **S3** | What does `LongPathsEnabled=0` do to today's fixed-name paths over 260 (P3)? | **Not planned.** It needs a registry change on a dev machine or a CI image with long paths off. It only informs the non-goal, and the decision doesn't depend on it. |

### L2 results (measured)

Setup: 50 value-equal models, `CSharpGeneratorDriver`, SDK 11 rc, Roslyn 5.9.0. The two shapes compared are per-item
output (today) and `Collect` → sorted equatable array → one output.

| Scenario | Per-item | One file |
| --- | --- | --- |
| Unrelated edit | Models Unchanged, output Cached, 0 files changed | Models Unchanged, sorted step Cached, output Cached, 0 files changed |
| One model edited | 1 of 50 files changed (935 chars, reparse 0.11 ms) | the one file changed (44,888 chars, reparse 0.92 ms) |
| Model added | 1 file New | the one file Modified |
| Allocated, one-model edit | ~210 KB | ~390–400 KB |
| Time | within run-to-run noise of each other | same |

**What it costs:** under 1 ms and about 200 KB per model edit at 50 models, for a bounded name and a fixed file
count. Unrelated edits, the common case while typing, cost nothing extra.

## Milestones

Tests are batched to the end, per the house rule.

1. **M1: the pipeline (D1), the producer (D2) and the file name (D3).**
   - Delete `HintName`/`HintNameOf`.
   - Write the D4 guard, and run S1.
   - *Status: done.* One `EqualityProducer` over the sorted models writes `GeneratedEquality.g.cs`; `HintName`
     and `HintNameOf` are gone. Guard in `MintPlayer.SourceGenerators.Tests/Guards/FixedFileSetGuardTests.cs` and
     `Assertions/MintPlayer.Assertions.SourceGenerator.Tests/Generators/FixedFileSetGuardTests.cs`; S1 passed.
2. **M2: tests.**
   - Update the tests listed above and regenerate the snapshots.
   - Run S2, the byte-identical check per model.
   - Add the new tests.
3. **M3: docs, changelog and version (D5).**
4. **M4: verification and PR.**
   - Full Release build and test run.
   - The PR's `sourcegenerators-benchmark` job compares against master, the first real head-vs-base run. The gate
     applies: B1 is unaffected, and B2's "All 8" set includes this generator. Its allocation must stay within 5%.
     L2 predicts a small increase only on model edits, and B2's edits aren't model edits.

## Acceptance criteria

1. **The file set.** For any input, `ValueComparerGenerator` emits `GeneratedEquality.g.cs` (and `JoinMethods.g.cs`
   when configured) and nothing else. It emits nothing when there are no models.
2. **Identical members.** S2 shows each model's members are identical to master's.
3. **Caching.** An unrelated edit leaves every step Cached (existing caching tests), and a model edit is Modified.
4. **Determinism.** The file text is independent of declaration order and file order.
5. **The guard.** The D4 guard passes for every generator, and demonstrably fails on a per-type name (S1).
6. **The full suite.** It passes in Release, and CI is green, including the benchmark gate.
7. **Docs.** The README, CHANGELOG, `CLAUDE.md` and code comments name the single file.

## Open questions for the owner

Both resolved by the owner.

1. **Version.** Bump all SourceGenerators packages in lockstep to 12.0.1, as in #183/#185, or only
   `MintPlayer.ValueComparerGenerator`? **Resolved: lockstep, 12.0.1 for every SourceGenerators package.**
2. **File name.** `GeneratedEquality.g.cs`, matching the `[GenerateEquality]` attribute, or `Equality.g.cs`?
   **Resolved: `GeneratedEquality.g.cs`.**
