# MintPlayer.SourceGenerators.Tools.Benchmarks

The benchmarks B1 and B2 of [PRD-GeneratedEquality](../../docs/PRD-GeneratedEquality.md). This project is not
packed and is not a test project.

It measures the **current implementation only**. The before/after comparison against master's value-comparer
runtime was measured once and is recorded in that PRD, under "B1/B2: benchmarks (measured)"; it is not
re-measured here.

- **B1, per-call equality.** It measures the real `[GenerateEquality]` output of this checkout's generator
  (`Generated/Models.cs`), called through `EqualityComparer<T>.Default`.
  - It covers six shapes. Each shape has an equal pair, a pair differing in the last property, and
    `GetHashCode`.
- **B2, pipeline cost.** It measures one `CSharpGeneratorDriver.RunGenerators` on a warm driver, after one
  edit, with step tracking off.
  - The corpus is synthetic: 500 files (`Pipeline/Corpus.cs`).
  - There are two edits: an unrelated method-body edit, and a relevant `[Register]` lifetime toggle.
  - The generators load from their own `bin` folders.
  - **SG5** is the 5 generators of MintPlayer.SourceGenerators over spike S1's corpus.
  - **All** adds Mapper, ValueComparerGenerator and JoinMethodGenerator, over a corpus that gives them inputs.

Before any benchmark runs, `Verification.Run()` checks the code under measurement, and the run stops if a
check fails:
- Every model implements `IEquatable<itself>`, which proves the generator ran.
- On every shape: equal pairs are equal with equal hashes, last-differs pairs are unequal, equality is
  symmetric, and nulls are handled.
- Every pipeline scenario loads its generators.
- An unrelated edit leaves the generated output unchanged, and a relevant edit changes it.

## Running

Log the raw output to a file, and filter it afterwards.

```sh
# Everything: B1 on net11.0 (+ net481 on Windows), B2 on net11.0.
dotnet run -c Release -f net11.0 --project SourceGenerators/MintPlayer.SourceGenerators.Tools.Benchmarks \
  -- --filter '*' --artifacts <dir> > bench.log 2>&1

# Only the verification checks.
dotnet run -c Release -f net11.0 --project SourceGenerators/MintPlayer.SourceGenerators.Tools.Benchmarks -- --verify-only

# One group.
... -- --filter '*ImmutableArray50*'
... -- --filter '*PipelineBenchmarks*'
```

- **`-f net11.0` is required on Windows.** There the project also targets net481, for B1's net481 job,
  because Visual Studio runs analyzers on .NET Framework. Set `BENCH_NET481=0` to skip that job.
- **Which metric to trust.** In B2, trust allocated bytes. Wall time on a warm driver varies by about ±30%
  from run to run.

## In CI

`.github/workflows/sourcegenerators-benchmark.yml` runs on every pull request that touches `SourceGenerators/`.
It runs these benchmarks on the pull request's head and on its base commit, on the same runner, and
compares the two with `.github/scripts/compare-benchmarks.sh`:

- **Only allocated bytes are gated.** A row that allocated 0 B on the base must stay 0 B, and any other
  row may grow by at most 5% (`BYTES_TOLERANCE_PERCENT`). Time goes in the job summary but never fails
  the job.
- **A benchmark that produced no result fails the job.** BenchmarkDotNet exits 0 even then.
- **When the base commit has no benchmark project,** which is the case for the pull request that added it,
  every row reports as new. The head is then only checked for missing results.
