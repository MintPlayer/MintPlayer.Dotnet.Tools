# TokenReplacer Implementation Plan

Companion to [TokenReplacer-prd.md](TokenReplacer-prd.md). Phases are ordered so each ends in a buildable, testable state; CI (`dotnet build` → `dotnet test` → `dotnet pack`, see `.github/workflows/dotnet-build-master.yml`) stays green after every phase.

Structure: **one shipping project** (`MintPlayer.TokenReplacer.Targets` — tasks + props/targets in a single csproj, same shape as the restructured `MintPlayer.FolderHasher.Targets`) plus one test project.

## Phase 1 — Single Targets project: engine, tasks, unit tests ✅ COMPLETED

1. Create `TokenReplacer/MintPlayer.TokenReplacer.Targets/` (`netstandard2.0`):
   - `TokenReplacementEngine` (pure class, no MSBuild types): `Replace(string content, IReadOnlyDictionary<string,string> tokens, TokenDelimiters delims, MissingTokenPolicy policy)` returning content + diagnostics (replaced tokens, unmatched tokens).
   - `AssetsFileVersionReader` (pure class): parse `project.assets.json` `libraries` keys (`<id>/<version>`) → resolved version per package id. Use `System.Text.Json` (netstandard2.0-compatible package).
   - `ReplaceTokensTask : Microsoft.Build.Utilities.Task` — thin wrapper: read source (detect BOM), call engine, write-if-changed, emit `ReplacedFiles` output items, log unmatched-token warnings/errors with codes `MPTR00x`.
   - `GetPackageVersionTask : Task` — thin wrapper over `AssetsFileVersionReader`; error `MPTR001` when a package id isn't found.
   - `Microsoft.Build.Utilities.Core` PackageReference (compile-time only; the host provides it at run time — do not pack MSBuild assemblies).
2. Create `TokenReplacer/MintPlayer.TokenReplacer.Tests/` (xUnit + `Microsoft.NET.Test.Sdk` + coverlet, matching `FolderHasher/MintPlayer.FolderHasher.Tests`), `ProjectReference` to the Targets project:
   - Engine: single/multiple tokens, custom delimiters, missing-token policies, token value containing the delimiter, empty file, idempotency.
   - Encoding: UTF-8 no-BOM in → no-BOM out; BOM in → BOM out.
   - Write-if-changed: unchanged content leaves file timestamp untouched.
   - `AssetsFileVersionReader` against fixture `project.assets.json` files (direct dep, transitive dep, missing package).
3. Add both projects to `MintPlayer.Dotnet.Tools.sln` under a `TokenReplacer` solution folder.

**Done when:** `dotnet test` green locally; no packaging yet.

## Phase 2 — Props/targets + packaging (same project) ✅ COMPLETED

4. Add the MSBuild surface to the same project and wire up packing (modeled on `FolderHasher/MintPlayer.FolderHasher.Targets/MintPlayer.FolderHasher.Targets.csproj`: `IncludeBuildOutput=false`, `SuppressDependenciesWhenPacking=true`, `DevelopmentDependency=true`, `IncludeSymbols=false`, own output DLL packed via `<None Include="bin\$(Configuration)\$(TargetFramework)\MintPlayer.TokenReplacer.Targets.dll" Pack="true" />`), but packing into **both** `build/` and `buildTransitive/`:
   - `MintPlayer.TokenReplacer.Targets.props` — own-version derivation property (guarded by `'$(TokenReplacerOwnVersion)' == ''`), default item metadata.
   - `MintPlayer.TokenReplacer.Targets.targets` — `UsingTask` registrations (`TaskName="MintPlayer.TokenReplacer.Targets.ReplaceTokensTask"`, `AssemblyFile="$(TokenReplacerTasksAssembly)"`, overridable via `$(TokenReplacerTasksAssembly)` for tests), `MintPlayerResolvePackageVersionTokens` target (`DependsOnTargets="ResolvePackageAssets"`, condition on `@(TokenReplacePackageVersion)`), `MintPlayerReplaceTokens` target (`BeforeTargets="AssignTargetPaths"`, `Inputs`/`Outputs` incremental, `FileWrites` for Clean, opt-in `Content` inclusion).
   - `build/` variants are one-line `<Import>` of the `buildTransitive/` files (pack the real files once into `buildTransitive/`, thin importers into `build/`).
   - `System.Text.Json` + its dependency closure packed next to the `.targets` only if assembly loading actually requires it (verify in Phase 4; trim to what fails to load).
5. Version `1.0.0`, Apache-2.0, README — copy the metadata block style from FolderHasher.Targets.

**Done when:** `dotnet pack MintPlayer.TokenReplacer.Targets` produces a nupkg whose layout (inspected with zip listing) matches the design.

## Phase 3 — Integration & E2E tests ✅ COMPLETED

6. Test infrastructure in `MintPlayer.TokenReplacer.Tests` (new — the repo has no precedent for this; keep it self-contained in this family):
   - `MsBuildFixture` helper: runs `dotnet` CLI via `Process` in a temp dir, captures stdout/stderr, asserts exit code with output attached to the failure message. Pass `-tl:off` so target/skip messages are parseable.
   - Fixture projects as content under `Tests/Fixtures/` (template `.csproj` + template content files; **not** added to the solution), copied to temp per test.
7. **Direct-import integration tests** (fast path, no pack): fixture csproj does `<Import Project="...MintPlayer.TokenReplacer.targets" />` with `TokenReplacerTasksAssembly` overridden to the freshly built task DLL, `TokenReplacerOwnVersion` preset. Verify:
   - tokens replaced in output file; `Content` inclusion lands the file in `bin/`;
   - second build skips the target (parse `-v:n` log for the up-to-date skip);
   - `dotnet clean` removes generated file (FileWrites);
   - missing-token policy surfaces as warning/error in build output;
   - `TokenReplacePackageVersion` resolves a real restored package's version from the fixture's assets file.
8. **Pack-and-consume E2E test** (one test, `[Trait("Category","E2E")]`): `dotnet pack` the Targets project and the `Fixtures/SamplePackage/` fixture (TwoSky-style: content template with `$version$` + thin `buildTransitive/*.targets` per the PRD recipe) into a temp folder with explicit `-p:Version` values; write a temp `nuget.config` (local feed + nuget.org); `dotnet restore`+`build` a fixture consumer referencing the sample package. Asserts the folder-layout version derivation yields the packed version and the `buildTransitive` flow works with zero consumer config. Self-contained because CI runs tests *before* pack.

**Done when:** full `dotnet test` green locally **and** in a run mimicking CI order (`restore → build -c Release → test --no-restore`).

## Phase 4 — Hardening & cross-host verification ⏳ PARTIAL

9. Verify on Windows under **Visual Studio MSBuild** (`MSBuild.exe` from VS 2022+, .NET Framework host): task assembly + any JSON dependency closure loads. Adjust packed dependency set if assembly-load errors occur; fall back to a vendored minimal JSON parser if the closure gets ugly.
10. Path handling: tokens/outputs with spaces, relative vs absolute `Output`, forward/back slashes (CI is ubuntu-latest — all fixture paths must be slash-agnostic).
11. Run `dotnet pack` of the whole solution to confirm no NU* warnings introduced.

## Phase 5 — Docs & release ⏳ PARTIAL (README + PRD done; release happens on merge to master)

12. `MintPlayer.TokenReplacer.Targets/README.md` (consumer modes, all items/metadata/properties reference table, package-author recipe based on the SamplePackage fixture). Pack via `PackageReadmeFile`.
13. Update PRD status columns ❌ → ✅; add Version History table.
14. Merge to `master` → CI publishes `MintPlayer.TokenReplacer.Targets` (the only published package) to nuget.org + GitHub Packages automatically.

## Risks / Watch-outs

| Risk | Mitigation |
|------|------------|
| Task DLL fails to load under VS (netfx) due to System.Text.Json binding | Phase 4 explicit verification; vendored minimal JSON parser as fallback (assets file shape is simple) |
| CI test order (test before pack) breaks E2E test assumptions | E2E test packs what it needs itself (Phase 3.8) |
| `ubuntu-latest` CI vs Windows dev: path separators, BOM, line endings in fixtures | Fixtures normalized; assertions ignore EOL; run tests on both at least once before release |
| Folder-layout version derivation breaks with non-standard package roots (fallback folders behave the same; future NuGet layout changes) | `TokenReplacerOwnVersion` override property is the documented escape hatch; E2E test guards the mechanism |
| `BeforeTargets="AssignTargetPaths"` too late/early for some asset pipelines (e.g. StaticWebAssets) | Expose `$(MintPlayerReplaceTokensBeforeTargets)` property so consumers can re-hook without forking the targets |
| Fixture csproj files under the test project confuse `dotnet build`/`dotnet test` of the solution | Fixtures kept out of the solution and named `*.csproj.template` (renamed on copy to temp) |

## Implementation notes (deviations discovered while building)

- **`Output` is a reserved MSBuild item metadata name** (MSB4118) — the metadata is called `OutputFile` instead.
- **Packed props/targets file names must equal the package id** (`MintPlayer.TokenReplacer.Targets.props/.targets`), otherwise NuGet silently ignores them; this was caught by the E2E test (transitive package restored with zero build assets).
- **Attribute-form item metadata requires xmlns-less project files** — the legacy `xmlns="...msbuild/2003"` namespace rejects it (MSB4066); all shipped props/targets omit the xmlns.
- **Clean only tracks FileWrites under bin/obj** — generated files elsewhere are not removed by `dotnet clean` (documented in the README).
- **Content/None/FileWrites registration happens in the always-running prep target**, not the incremental replacement target — otherwise the copy-to-bin stops happening on up-to-date builds.
- Fixture projects are generated in code (raw strings) into temp workspaces instead of a checked-in `Tests/Fixtures/` folder.
- Phase 4 step 9 (Visual Studio full-framework MSBuild verification) is still outstanding: no VS MSBuild on the dev machine. Risk is low — the task assembly is netstandard2.0 with no dependencies beyond MSBuild itself (vendored JSON scanner instead of System.Text.Json).
