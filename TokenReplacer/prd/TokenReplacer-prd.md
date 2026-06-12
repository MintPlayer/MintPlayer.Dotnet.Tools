# TokenReplacer PRD

## Overview

`MintPlayer.TokenReplacer` is a generic MSBuild token-replacement toolset, shipped as a NuGet package with `buildTransitive` targets. During build it substitutes tokens such as `$version$` in designated files with values extracted from the MSBuild/NuGet pipeline (resolved package versions, MSBuild properties, custom values) and writes the result to an output file.

### Origin / Inspiration

The design generalizes an internal 2sky package (`TwoSky.WebComponents.FileUpload`, shipping `TwoSky.WebComponents.FileUpload.targets`) that extracts its own package version at build time and replaces `$version$` tags in content files — e.g. so a `<script>` tag or component loader references the exact published asset version. That package is single-purpose and branded; this project extracts the mechanism into a generic, reusable MintPlayer package:

- **No hardcoded package name** — any package or project can declare which files contain tokens.
- **No hardcoded token** — `$version$` is just the default built-in; arbitrary tokens are supported.
- **Two consumption modes** — app projects use it directly; package authors re-ship it so *their* consumers get token replacement transparently.

### Primary Use Cases

1. **Package author mode (the TwoSky scenario):** A package (e.g. `MintPlayer.WebComponents.FileUpload`) ships web-component/JS/CSS assets plus a thin `.targets` that declares "replace `$version$` in this template file with *my own resolved version*". The consuming app's build then materializes a file pointing at the correct CDN/asset version — automatically, transitively, with zero consumer configuration.
2. **App project mode:** A project declares `TokenReplaceFile` items in its own `.csproj` to stamp versions, build metadata, or resolved dependency versions into content files (HTML templates, JS, JSON manifests, …) at build time.
3. **Dependency version stamping:** Replace a token with the *resolved* version of any `PackageReference` (read from `project.assets.json`), not just the requested version — e.g. emit `<script src="https://cdn.example/lib@13.2.1/loader.js">` where `13.2.1` is whatever NuGet actually resolved.

---

## Requirements

### Core Task (`ReplaceTokensTask`)

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Token replacement** | Replace `$name$`-style tokens in a source file, write result to an output file | ❌ Planned |
| **Multiple tokens per file** | Any number of token/value pairs applied in one pass | ❌ Planned |
| **Configurable delimiters** | Default `$...$`; start/end delimiters overridable (e.g. `{{...}}`) | ❌ Planned |
| **Write-if-changed** | Only rewrite the output file when content differs (avoids dirtying up-to-date checks / file watchers) | ❌ Planned |
| **Encoding preservation** | Read/write UTF-8 (no BOM) by default; preserve BOM when the source has one; encoding overridable | ❌ Planned |
| **Missing-token policy** | Unmatched tokens in the source: configurable `Warn` (default) / `Error` / `Ignore` | ❌ Planned |
| **Output item** | Emit `ReplacedFiles` output items so targets can chain them into `Content`/`None` | ❌ Planned |
| **netstandard2.0** | Task assembly targets `netstandard2.0` so it loads in both `dotnet` CLI and Visual Studio (full-framework) MSBuild | ❌ Planned |

> Note: `MintPlayer.FolderHasher.Targets` compiles its task at `net10.0`, which only loads under `dotnet build`. `MintPlayer.MSBuild.Tasks` (`netstandard2.0`) is the precedent to follow here so Visual Studio (full-framework MSBuild) works too.

### Version Extraction (`GetPackageVersionTask` + props-time derivation)

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Own-version derivation (no task)** | When imported from a NuGet package, derive the package's own version from the folder layout (`<root>/<id>/<version>/buildTransitive/`) at *props evaluation time* — available to all property functions, no task execution needed | ❌ Planned |
| **ProjectReference fallback** | Property override (`<TokenReplacerOwnVersion>`) for local/dev scenarios where the folder-derivation doesn't apply (ProjectReference, repo-internal testing) | ❌ Planned |
| **Resolved version of any package** | `GetPackageVersionTask` reads `$(ProjectAssetsFile)` (project.assets.json) and returns the resolved version for a given package id | ❌ Planned |
| **Graceful failure** | Clear MSBuild error (with code, e.g. `MPTR001`) when a requested package isn't in the assets file | ❌ Planned |

### MSBuild Surface (buildTransitive .props/.targets)

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Item-driven declaration** | `<TokenReplaceFile Include="src.template" Output="src.out">` items declare work; token values via item metadata or companion `<TokenReplaceValue Include="version" Value="..." />` items | ❌ Planned |
| **Package-version tokens** | `<TokenReplacePackageVersion Include="Some.Package" TokenName="someVersion" />` resolves via assets file and registers the token | ❌ Planned |
| **Runs before build, after restore** | Main target hooks `BeforeTargets="AssignTargetPaths"` (so outputs can participate in `Content`) and depends on `ResolvePackageAssets` when package-version tokens are used | ❌ Planned |
| **Incremental** | Target declares `Inputs`/`Outputs` so unchanged builds skip the task entirely | ❌ Planned |
| **Auto-include output (opt-in)** | Metadata `IncludeAs="Content"` (+ `CopyToOutputDirectory`) adds the generated file to the build automatically | ❌ Planned |
| **Output under obj/ by default** | When `Output` metadata is omitted, write to `$(IntermediateOutputPath)tokenreplacer/%(Filename)%(Extension)` — never silently overwrite source-tree files | ❌ Planned |
| **buildTransitive** | Ship `.props`/`.targets` in `buildTransitive/` **and** `build/` (older NuGet clients), `build/` importing the same files | ❌ Planned |
| **Clean integration** | Generated files registered with `FileWrites` so `Clean` removes them | ❌ Planned |

### Package-Author Experience

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Thin re-ship pattern** | A documented recipe: package author adds `<PackageReference Include="MintPlayer.TokenReplacer.Targets" />` + a 10-line own `.targets` in `buildTransitive/` declaring their `TokenReplaceFile` items with `$version$` bound to their own derived version | ❌ Planned |
| **No flow-through dependency surprises** | Document `PrivateAssets`/`buildTransitive` semantics so the author controls whether TokenReplacer flows to their consumers (it must — that's the point — so default guidance is *no* `PrivateAssets="all"` on the targets package) | ❌ Planned |
| **Reference sample** | A fixture package under the test project (`Fixtures/SamplePackage/`) demonstrating the full TwoSky-style scenario end-to-end; packed and consumed by the E2E test, doubles as the documentation example | ❌ Planned |

### Testing

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Unit tests (xUnit)** | `MintPlayer.TokenReplacer.Tests`: token engine (delimiters, multiple tokens, missing-token policies, encoding/BOM, write-if-changed), assets-file version lookup against fixture JSON | ❌ Planned |
| **Targets integration tests** | Tests that run real `dotnet build` (via `Process`) on fixture projects importing the `.targets` directly with the locally built task DLL — verifies wiring, incrementality, Clean | ❌ Planned |
| **Pack-and-consume E2E test** | Test that runs `dotnet pack` on the Targets project into a temp local feed, then restores+builds a fixture consumer with a temp `nuget.config` — verifies the real NuGet folder-layout version derivation and `buildTransitive` flow | ❌ Planned |
| **CI compatibility** | All tests must self-contain (pack what they need themselves) because CI runs `dotnet test` *before* `dotnet pack` | ❌ Planned |

### Documentation

| Requirement | Description | Status |
|-------------|-------------|--------|
| **README per package** | Usage examples for both consumption modes (repo convention, cf. FolderHasher READMEs) | ❌ Planned |
| **XML documentation** | IntelliSense docs on public task properties | ❌ Planned |

---

## Architecture

### Projects

One shipping project, one test project — same shape as the (restructured) `MintPlayer.FolderHasher.Targets`, which compiles its MSBuild task and ships the `.targets` from a single csproj.

| Project | TFM | Description |
|---------|-----|-------------|
| `MintPlayer.TokenReplacer.Targets` | netstandard2.0 | Single package project: task implementations (`ReplaceTokensTask`, `GetPackageVersionTask`) with pure logic in plain classes (`TokenReplacementEngine`, `AssetsFileVersionReader`) **plus** `MintPlayer.TokenReplacer.props`/`.targets`. Packs its own output DLL next to the props/targets into `buildTransitive/` and `build/`; `IncludeBuildOutput=false`, `DevelopmentDependency=true`, `SuppressDependenciesWhenPacking=true` |
| `MintPlayer.TokenReplacer.Tests` | net10.0 | xUnit unit + integration + E2E tests; references the Targets project directly for engine unit tests. Fixture projects (consumer + TwoSky-style `SamplePackage`) live under `Tests/Fixtures/`, not in the solution |

### Mechanism 1 — Own-version derivation (props evaluation time)

When the `.props` is imported from the NuGet cache, its location is
`<packagesRoot>/<package.id>/<version>/buildTransitive/MintPlayer.TokenReplacer.props`, so:

```xml
<PropertyGroup Condition="'$(TokenReplacerOwnVersion)' == ''">
  <!-- .../<id>/<version>/buildTransitive/ → "<version>" -->
  <TokenReplacerOwnVersion>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory.TrimEnd('\/'))))))</TokenReplacerOwnVersion>
</PropertyGroup>
```

The same snippet is part of the documented package-author recipe (each re-shipping package derives *its own* version from *its own* `.targets` location). For ProjectReference/dev scenarios the property is simply pre-set.

### Mechanism 2 — Resolved dependency versions (task, target time)

`GetPackageVersionTask(AssetsFile, PackageIds[]) → Versions[]` parses `project.assets.json` (`libraries` section, `<id>/<version>` keys). Runs in a target with `DependsOnTargets="ResolvePackageAssets"` so the assets file exists. Chosen over `@(PackageReference)` metadata because it returns the *resolved* version (floating versions, central package management, transitive pins).

### Mechanism 3 — Replacement target

```xml
<Target Name="MintPlayerReplaceTokens"
        BeforeTargets="AssignTargetPaths"
        Condition="'@(TokenReplaceFile)' != ''"
        Inputs="@(TokenReplaceFile);$(MSBuildAllProjects)"
        Outputs="@(TokenReplaceFile->'%(Output)')">
  <ReplaceTokensTask SourceFiles="@(TokenReplaceFile)" Tokens="@(TokenReplaceValue)" ...>
    <Output TaskParameter="ReplacedFiles" ItemName="_MintPlayerReplacedFiles" />
  </ReplaceTokensTask>
  <ItemGroup>
    <FileWrites Include="@(_MintPlayerReplacedFiles)" />
    <Content Include="@(_MintPlayerReplacedFiles)" Condition="'%(_MintPlayerReplacedFiles.IncludeAs)' == 'Content'" ... />
  </ItemGroup>
</Target>
```

### Usage Examples (target state)

**App project mode:**

```xml
<ItemGroup>
  <PackageReference Include="MintPlayer.TokenReplacer.Targets" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>

<ItemGroup>
  <TokenReplacePackageVersion Include="Some.Cdn.Library" TokenName="cdnVersion" />
  <TokenReplaceValue Include="buildConfig" Value="$(Configuration)" />
  <TokenReplaceFile Include="wwwroot\index.template.html"
                    Output="wwwroot\index.html"
                    IncludeAs="Content" />
</ItemGroup>
```

`index.template.html` containing `https://cdn.example/lib@$cdnVersion$/loader.js` builds into `index.html` with the resolved version inlined.

**Package author mode (generic TwoSky replacement):** `MintPlayer.WebComponents.FileUpload.targets` shipped in that package's `buildTransitive/`:

```xml
<Project>
  <PropertyGroup>
    <_FileUploadVersion>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory.TrimEnd('\/'))))))</_FileUploadVersion>
  </PropertyGroup>
  <ItemGroup>
    <TokenReplaceValue Include="version" Value="$(_FileUploadVersion)" />
    <TokenReplaceFile Include="$(MSBuildThisFileDirectory)..\content\file-upload.template.js"
                      Output="$(IntermediateOutputPath)file-upload.js"
                      IncludeAs="Content" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

---

## Non-Goals

- Replacing NuGet's own nuspec `$token$` substitution at *pack* time (that already exists; this is *build/consume*-time replacement).
- Source-code generation (use the repo's source generators for C# — this targets content/asset files).
- Globbing/transform pipelines beyond find-and-replace (no templating language, no conditionals inside templates).
- npm/JS-ecosystem version stamping (only what's visible to MSBuild/NuGet).

## Open Questions

| Question | Recommendation |
|----------|----------------|
| Package family name: `TokenReplacer` vs `BuildTokens` vs `TokenReplacement`? | `TokenReplacer` (`MintPlayer.TokenReplacer.*`) — matches repo's noun-agent naming (FolderHasher) |
| Fold the task into existing `MintPlayer.MSBuild.Tasks`? | No — keep a dedicated family so the Targets package stays dependency-free and independently versioned; `MintPlayer.MSBuild.Tasks` stays a grab-bag for one-off helpers |
| Publish the TwoSky-style sample package to NuGet? | No — it's a test fixture under `Tests/Fixtures/`, packed on the fly by the E2E test. Revisit if `MintPlayer.WebComponents.*` becomes real |
| Token syntax also support `__name__` (Azure DevOps style)? | Defer; delimiters are configurable, which covers it |
