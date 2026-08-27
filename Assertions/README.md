# Assertions

Family folder for **MintPlayer.Assertions** — a fluent assertion library for .NET, created after
FluentAssertions v8 went commercial (January 2025).

| Project | Purpose |
|---|---|
| `MintPlayer.Assertions` | The library (net8.0–net10.0). The only NuGet package; it embeds the generator and analyzers. |
| `MintPlayer.Assertions.SourceGenerator` | All compiler extensions: `Generators/` emits reflection-free equivalency accessors and `[GenerateAssertion]` assertions; `Diagnostics/` holds the analyzers and code fixes (MPA0001 un-awaited assertion, MPA0002/0003, and the FluentAssertions migration fix). Mirrors the layout of `MintPlayer.SourceGenerators`. |
| `MintPlayer.Assertions.Tests` | xUnit test suite (self-hosted: written with the library itself). `ReadmeSamplesTests.cs` compiles and runs every sample from the package README, so the docs cannot silently rot. |
| `MintPlayer.Assertions.Benchmarks` | BenchmarkDotNet comparison vs FluentAssertions 7. |

See [prd/Assertions-prd.md](prd/Assertions-prd.md) and [prd/Assertions-plan.md](prd/Assertions-plan.md).
