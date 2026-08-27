# Assertions

Family folder for **MintPlayer.Assertions** — a fluent assertion library for .NET, created after
FluentAssertions v8 went commercial (January 2025).

| Project | Purpose |
|---|---|
| `MintPlayer.Assertions` | The library (net8.0–net10.0). The only NuGet package; it embeds the generator and analyzers. |
| `MintPlayer.Assertions.SourceGenerator` | Emits reflection-free equivalency member accessors + `[GenerateAssertion]` custom assertions. |
| `MintPlayer.Assertions.Analyzers` | MPA0001 un-awaited assertion (error) and friends, plus the FluentAssertions migration code fix. |
| `MintPlayer.Assertions.Tests` | xUnit test suite (self-hosted: written with the library itself). |
| `MintPlayer.Assertions.Benchmarks` | BenchmarkDotNet comparison vs FluentAssertions 7. |

See [prd/Assertions-prd.md](prd/Assertions-prd.md) and [prd/Assertions-plan.md](prd/Assertions-plan.md).
