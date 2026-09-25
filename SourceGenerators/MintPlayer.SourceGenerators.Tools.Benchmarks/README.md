# MintPlayer.SourceGenerators.Tools.Benchmarks

The benchmarks B1 and B2 of [PRD-GeneratedEquality](../../docs/PRD-GeneratedEquality.md). The results are in
that PRD, under "Spike results". This project is not packed and is not a test project.

- **B1, per-call equality.** It compares **Legacy** with **Generated**:
  - Legacy is master's value-comparer runtime, vendored under `Legacy/Runtime` with a renamed namespace.
    It is paired with hand-written copies of what master's generator emitted (`Legacy/Models.cs`).
  - Generated is the real `[AutoValueComparer]` output of this branch's generator (`Generated/Models.cs`).
  - It covers six shapes. Each shape has an equal pair, a pair differing in the last property, and
    `GetHashCode`. Legacy is the baseline, so Ratio means Generated / Legacy.
- **B2, pipeline cost.** It measures one `CSharpGeneratorDriver.RunGenerators` on a warm driver, after one
  edit, with step tracking off.
  - The corpus is synthetic: 500 files (`Pipeline/Corpus.cs`).
  - There are two edits: an unrelated method-body edit, and a relevant `[Register]` lifetime toggle.
  - The generators load from their own `bin` folders into an `AssemblyLoadContext`.
  - **SG5** is the 5 generators of MintPlayer.SourceGenerators over spike S1's corpus.
  - **All** adds Mapper, ValueComparerGenerator and JoinMethodGenerator, over a corpus that gives them inputs.

Before any benchmark runs, `Verification.Run()` checks that both sides do the same work, and the run stops
if a check fails:
- Legacy and Generated give the same answers (equal, unequal, symmetric, null, equal hashes) on every shape.
- The generator really produced `IEquatable<T>`.
- Every pipeline scenario loads its generators.
- An unrelated edit leaves the generated output unchanged, and a relevant edit changes it.

## Running

Log the raw output to a file, and filter it afterwards.

```sh
# Everything: B1 on net11.0 (+ net481 on Windows), B2 on net11.0.
dotnet run -c Release -f net11.0 --project SourceGenerators/MintPlayer.SourceGenerators.Tools.Benchmarks \
  -- --filter '*' --artifacts <dir> > bench.log 2>&1

# Only the fairness checks.
dotnet run -c Release -f net11.0 --project SourceGenerators/MintPlayer.SourceGenerators.Tools.Benchmarks -- --verify-only

# One group.
... -- --filter '*ImmutableArray50*'
... -- --filter '*PipelineBenchmarks*'
```

- **`-f net11.0` is required on Windows.** There the project also targets net481, for B1's net481 job,
  because Visual Studio runs analyzers on .NET Framework. Set `BENCH_NET481=0` to skip that job.
- **Master in B2.** B2 measures this checkout's generators. To add master's generators to the same run, build a
  master checkout's generator projects in Release and point `BENCH_MASTER_ROOT` at its repository root:

  ```sh
  git archive --format=tar -o master.tar master SourceGenerators nuget.config
  mkdir master-src && tar -xf master.tar -C master-src
  dotnet build -c Release master-src/SourceGenerators/SourceGenerators/MintPlayer.SourceGenerators/MintPlayer.SourceGenerators.csproj
  dotnet build -c Release master-src/SourceGenerators/Mapper/MintPlayer.Mapper/MintPlayer.Mapper.csproj
  dotnet build -c Release master-src/SourceGenerators/ValueComparerGenerator/MintPlayer.ValueComparerGenerator/MintPlayer.ValueComparerGenerator.csproj
  BENCH_MASTER_ROOT=$PWD/master-src dotnet run -c Release -f net11.0 --project ... -- --filter '*Pipeline*'
  ```

- **Which metric to trust.** In B2, trust allocated bytes. Wall time on a warm driver varies by about ±30%
  from run to run.
