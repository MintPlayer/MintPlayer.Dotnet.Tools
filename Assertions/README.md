# Assertions

Family folder for **MintPlayer.Assertions** — a fluent assertion library for .NET, created after
FluentAssertions v8 went commercial (January 2025).

> ### 📖 [Read the documentation →](MintPlayer.Assertions/README.md)
>
> This page is only an index of the projects in this folder. The full documentation — every
> assertion, the equivalency options, the analyzers, migration from FluentAssertions — lives in
> the package README, which is also what ships to NuGet.

## Version info

| License | Build status |
|---------|--------------|
| [![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0) | ![publish-release](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/actions/workflows/dotnet-build-master.yml/badge.svg) |

| Package | Release | Preview | Downloads |
|---------|---------|---------|-----------|
| MintPlayer.Assertions | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.Assertions.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.Assertions) |

> The NuGet badges read "not found" until the package is first published; CI pushes it on merge to
> `master`. Only `MintPlayer.Assertions` is published — the generator and analyzers ship inside it.

| Project | Purpose |
|---|---|
| `MintPlayer.Assertions` | The library (net8.0–net10.0). The only NuGet package; it embeds the generator and analyzers. |
| `MintPlayer.Assertions.SourceGenerator` | All compiler extensions: `Generators/` emits reflection-free equivalency accessors and `[GenerateAssertion]` assertions; `Diagnostics/` holds the analyzers and code fixes (MPA0001 un-awaited assertion, MPA0002/0003, and the FluentAssertions migration fix). Mirrors the layout of `MintPlayer.SourceGenerators`. |
| `MintPlayer.Assertions.Tests` | xUnit test suite (self-hosted: written with the library itself). `ReadmeSamplesTests.cs` compiles and runs every sample from the package README, so the docs cannot silently rot. |
| `MintPlayer.Assertions.Benchmarks` | BenchmarkDotNet comparison vs FluentAssertions 7. |

See [prd/Assertions-prd.md](prd/Assertions-prd.md) and [prd/Assertions-plan.md](prd/Assertions-plan.md).
