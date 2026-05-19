# Verz — Implementation plan

A concrete build plan that converts the v1 product spec for the Verz global tool (`docs/prd/verz/PRD-initial.md`) into a project layout, plugin contract, and milestone-ordered task list ready for implementation.

## Scope of this document

This plan covers the *how* of v1 Verz: solution structure, plugin-loader internals, CLI wiring, per-subcommand control flow, the public-API-hash MSBuild pipeline, the project-graph and affected algorithm, testing layers, and an ordered milestone list with acceptance gates. User-visible behavior (subcommand names, flags, exit codes, `verz.json` schema, error messages, CI workflow shape) is owned by `docs/prd/verz/PRD-initial.md` and mirrored in `Verz/MintPlayer.Verz/README.md`. **Any divergence between this plan and the PRD must be resolved by updating the PRD first.** Purely-internal decisions (git library, MSBuild target hooks, in-process vs shell-out) live only here. Out-of-scope items match the PRD's list and are referenced at the end.

## Solution structure

All projects live under `Verz/` (existing prototype root). Each shippable assembly gets a sibling `*.Tests` xUnit project under `Verz/Tests/`. Integration tests live in `Verz/Tests/MintPlayer.Verz.IntegrationTests/`.

| Project | Target framework(s) | Purpose | Key dependencies |
|---|---|---|---|
| `MintPlayer.Verz` | `net10.0`, `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>verz</ToolCommandName>` | The CLI host. Builds plugin loader, owns subcommand handlers, owns the generic host. | `Microsoft.Extensions.Hosting`, `System.CommandLine` v2.x, `NuGet.Protocol`, `LibGit2Sharp` *(see open questions; current recommendation: shell out to `git`)*. References `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Abstractions` | `net8.0;net9.0;net10.0` (multitarget) | `IDevelopmentSdk`, `IPackageRegistry`, supporting POCOs (`ProjectIdentity`, `Artifact`, `PriorPackageInfo`, `BumpLevel`, `SemverTag`), exception types. No third-party references. | none |
| `MintPlayer.Verz.Sdks.Dotnet` | `net10.0` | The .NET `IDevelopmentSdk` plugin. Discovers `.csproj`, computes graph edges, computes public-API hashes, stamps versions, packs, and injects hash + framework-major into the produced nuspec. | `Microsoft.Build` (eval-only API), `PublicApiGenerator` 11.x (with its transitive `Mono.Cecil`), `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Sdks.NodeJS` | `net10.0` | The Node `IDevelopmentSdk` plugin. Discovers workspaces, computes graph edges, stamps `package.json`, packs. | `YamlDotNet` (for `pnpm-workspace.yaml`), `Microsoft.Extensions.FileSystemGlobbing`, `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Registries.NugetOrg` | `net10.0` | NuGet v3 registry plugin for nuget.org and any v3 feed addressable without credentials by default. Lookup via `FindPackageByIdResource`; nuspec parsing via `PackageArchiveReader`. Push via `dotnet nuget push`. | `NuGet.Protocol`, `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Registries.GithubPackageRegistry` | `net10.0` | NuGet v3 registry plugin specialized for `nuget.pkg.github.com`. Same protocol surface as NugetOrg, but with quirks (per-org index URL, `Authorization: Bearer` header, opinionated package-listing). | `NuGet.Protocol`, `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Registries.NpmJS` | `net10.0` | npm registry plugin. Lookup via `https://registry.npmjs.org/{pkg}/{version}` JSON. Push via `npm publish`. | `System.Net.Http`, `System.Text.Json`, `MintPlayer.Verz.Abstractions`. |
| `MintPlayer.Verz.Tests` | `net10.0` | xUnit tests for tool internals (graph, version algorithm, plugin loader, `verz.json` parser). | `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`. |
| `MintPlayer.Verz.Sdks.Dotnet.Tests`, `MintPlayer.Verz.Sdks.NodeJS.Tests`, `MintPlayer.Verz.Registries.*.Tests` | `net10.0` | Per-plugin unit tests. | xUnit + project under test. |
| `MintPlayer.Verz.IntegrationTests` | `net10.0` | Spins up real temp git repos, calls the packed `verz` tool via `Process.Start`, asserts on tags/files. | `xunit`, `LibGit2Sharp` *for assertions only, not tool runtime*. |

### NugetOrg vs. GithubPackageRegistry: two assemblies

Two separate plugin packages. Protocol differences justify it: nuget.org accepts API-key pushes against the v3 index, while GitHub Packages needs a per-org index URL and `<packageSourceCredentials>` (username + token-as-password); listing semantics also diverge (GitHub Packages returns 403 on unauthenticated private-feed calls, nuget.org returns an empty list). Two assemblies map 1:1 to `verz.json` `id` entries and avoid `if (Url.Contains("github"))` branches inside a shared assembly.

## Salvage assessment

**Keep** = ships into v1 with cosmetic changes only. **Adapt** = shape is right, implementation needs non-trivial work. **Discard** = delete; v1 supersedes.

| File | Verdict | Notes |
|---|---|---|
| `MintPlayer.Verz.Core/IDevelopmentSdk.cs` | **Adapt** | The v1 contract differs: an SDK plugin *discovers* its projects (`Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(string repoRoot, CT)`) rather than being asked per-path, and adds `EnumerateInRepoDependencies`, `StampVersion`, `PackAsync`. Method intent maps cleanly from the existing shape. |
| `MintPlayer.Verz.Core/IPackageRegistry.cs` | **Adapt** | v1 needs `LookupAsync(packageId, version)` returning `PriorPackageInfo { string? PublicApiHash; int? FrameworkMajor; }`, plus `PushAsync(Artifact)` and `AcceptedKinds`. Existing `GetAllVersionsAsync`/`DownloadPackageAsync` can stay as lower-level helpers. |
| `MintPlayer.Verz/Program.cs` (`LoadToolsAsync`) | **Adapt** | The `PackageArchiveReader` / `PackageExtractor.ExtractPackageAsync` flow moves into `PluginLoader.LoadAsync`. The hard-coded `lib/net10.0` path must search `lib/net10.0`, `lib/net9.0`, `lib/net8.0` in order with `runtimes/` fallback. `Assembly.LoadFrom` must become `AssemblyLoadContext.LoadFromAssemblyPath` against a per-plugin context. The hard-coded `nuget.org` index URL becomes iteration over `verz.json.Registries`. |
| `MintPlayer.Verz/Helpers/ToolCatalog.cs` | **Adapt** | Lazy-init `SemaphoreSlim` cache is fine. Rename to `PluginCatalog`, split the tuple into `Sdks`/`Registries` indexed by `id`. `[Inject] VerzConfig` stays. |
| `MintPlayer.Verz/Helpers/VersionPackagePathResolver.cs` | **Keep** | Mirrors `~/.nuget/packages/{id}/{version}/` correctly. |
| `MintPlayer.Verz/VerzCommand.cs` + `.DotnetVersion.cs` + `.InitDotnet.cs` | **Discard** | The `dotnet-version`/`init-dotnet` command surface does not match the PRD's `init`/`set-versions`/`create-tag`/`publish`. The version-decision logic inside `DotnetVersionCommand` seeds the body of `ComputeNextVersion`, but the command class itself is replaced. |
| `MintPlayer.Verz/VerzConfig.cs` | **Discard** | `{ Tools: string[] }` does not match the PRD schema. Replace with `VerzConfig { List<RegistryEntry> Registries; List<PluginEntry> Plugins; }`. |
| `MintPlayer.Verz.Targets/GeneratePublicApiHashTask.cs` | **Discard (logic ported)** | The MSBuild task is dropped (see plan note). The `Assembly.Load(path)` bug is moot because the SDK plugin runs in its own .NET 10 process and can use `Assembly.LoadFrom(path)` directly. The SHA-256 + `Convert.ToHexString` pipeline ports into `DotnetSdk.ComputePublicApiHashAsync`. |
| `MintPlayer.Verz.Targets/InjectPublicApiHashTask.cs` | **Discard (logic ported)** | Post-pack nuspec rewrite moves into `DotnetSdk.PackAsync`. The `<PublicApiHash>` / `<FrameworkMajor>` element-upsert logic ports verbatim. |
| `MintPlayer.Verz.Targets/PublicApiHash.targets` | **Discard** | Consumers no longer import a `.targets` file. |
| `Sdks/MintPlayer.Verz.Sdks.Dotnet/DotnetSdk.cs` | **Adapt** | `GetMajorVersionAsync` keep. `GetPackageIdAsync` keep (add `<AssemblyName>` fallback between `<PackageId>` and the filename). `ComputeCurrentPublicApiHashAsync` adapt — `Assembly.LoadFrom` pins the file; switch to `MetadataLoadContext`. `ComputePackagePublicApiHashAsync` moves to the NuGet registry plugin's `Lookup`. TFM helpers (`IsNetTfm`, `ParseNetMajor`, `ParseNetMinor`) keep. |
| `Registries/.../NugetOrgRegistry.cs` | **Adapt** | The `new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3())` + `new PackageSource(...)` shape is right. New `LookupAsync` adds a nuspec-only round-trip via `RegistrationResourceV3`. Push is new. Rename namespace from `.Registry.` to `.Registries.` to match the PRD. |
| `MintPlayer.Verz/verz.json` (prototype) | **Discard** | Schema mismatch; regenerated by `verz init`. |

Net salvage verdict: 3 files **keep**, 7 files **adapt**, 4 files **discard**.

## Plugin loading

### Resolution

The `verz.json` parser accepts both string and object entries via a custom `JsonConverter`:

```csharp
public sealed class PluginEntry
{
    public string Id { get; init; } = default!;
    public string? Version { get; init; }   // null => latest stable, warn
}
```

`PluginLoader` walks `verzConfig.Plugins`. For each entry, it iterates `verzConfig.Registries` in declared order, opens a `SourceRepository` per registry, and calls `FindPackageByIdResource.GetAllVersionsAsync`. Pinned `Version` → exact match; null → highest stable (`!v.IsPrerelease`) with a warning. Extraction targets `~/.nuget/packages/{id}/{version}/` (existing `VersionPackagePathResolver` layout) and is reused on subsequent invocations.

### Isolation

Each plugin assembly is loaded into its own collectible `AssemblyLoadContext`:

```csharp
internal sealed class PluginLoadContext(string mainAssemblyPath, AssemblyDependencyResolver resolver)
    : AssemblyLoadContext(name: Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: true)
{
    protected override Assembly? Load(AssemblyName name)
    {
        // Hand shared types back to the default context to avoid type-identity drift.
        if (name.Name is "MintPlayer.Verz.Abstractions"
            or "Microsoft.Extensions.Logging.Abstractions"
            or "NuGet.Versioning")
            return null; // fall through to Default

        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
```

Any type that crosses the plugin/host boundary must resolve to the default context, otherwise a `cast IDevelopmentSdk` fails with the classic "type X is not assignable to type X" load-context error. `NuGet.Versioning` is on the list because `Lookup` returns `NuGetVersion`. `AssemblyDependencyResolver` is constructed from the plugin's primary DLL path; it uses the package's `.deps.json` to resolve in-package transitives.

### Reflection-based discovery

After load, the loader scans `assembly.GetExportedTypes()` for non-abstract types with a public constructor that implement either `IDevelopmentSdk` or `IPackageRegistry`. A single assembly may contribute both; the flat `Plugins` list explicitly allows this (rare in v1 but the design permits it).

```csharp
foreach (var t in assembly.GetExportedTypes())
{
    if (t.IsAbstract || !t.IsClass || t.GetConstructors().All(c => !c.IsPublic)) continue;
    if (typeof(IDevelopmentSdk).IsAssignableFrom(t))
        sdkRegistrations.Add(t);
    if (typeof(IPackageRegistry).IsAssignableFrom(t))
        registryRegistrations.Add(t);
}
```

### Instantiation

Plugins are constructed via `ActivatorUtilities.CreateInstance` with a fixed host-service set: `ILogger<T>` (writes funneled to `InvocationContext.Console`), `IHttpClientFactory` (for plugins like NpmJS that make raw HTTP calls), and `IFileSystem` (thin `System.IO` abstraction for unit tests). Plugins cannot register their own services in v1 — the host owns the DI graph (see open questions).

### Version pinning

Per the PRD, unversioned entries are latest-stable with a warning. The warning text lives in `Resources/Messages.resx` as `Plugin_FloatingVersion`.

## CLI framework

### Choice of library

**Vanilla System.CommandLine v2.x**. The prototype's `MintPlayer.CliGenerator` source generator is discarded for v1: four subcommands are small enough that hand-written wiring is simpler to read and easier to evolve while System.CommandLine's v2 API stabilizes. Future re-introduction (after System.CommandLine v2 ships GA) is a mechanical migration.

### Wiring

`Program.Main` builds an `IHost` via `Host.CreateApplicationBuilder(args)` (kept from the prototype), then constructs a `System.CommandLine.RootCommand` and binds subcommand classes:

```csharp
internal sealed class CreateTagCommand(
    ProjectGraphBuilder graphBuilder, VersionPlanner planner,
    GitTagger tagger, ILogger<CreateTagCommand> logger)
{
    public async Task<int> HandleAsync(CreateTagOptions opts, CancellationToken ct) { /* ... */ }
}
```

Each command's `Action` resolves the handler class from `host.Services` and calls `HandleAsync`. `verz.json` is bound to `VerzConfig` as a singleton. The plugin loader is a singleton; it lazy-loads on first request.

## Subcommand implementations

The four handlers share a common pre-flight: load `verz.json`, instantiate plugins, log the discovered plugin set. Subcommand-specific logic follows.

### `init`

1. If `verz.json` exists in CWD → exit 2 with stderr `verz.json already exists at {path}; refusing to overwrite`.
2. Build a `VerzConfig` pre-populated with at least one registry (default `nuget.org` if `--registry` not given) and an empty `Plugins` list.
3. Serialize with `JsonSerializerOptions { WriteIndented = true }` and the `$schema` field.
4. If `--stamp-placeholders`: bootstrap-scan `**/*.csproj` and `**/package.json` directly (no plugins required since this is the first run), and write `<Version>0.0.0-placeholder</Version>` / `"version": "0.0.0-placeholder"`.
5. Print summary; exit 0.

### `set-versions`

1. Resolve `--ref` (default `HEAD`) to a commit via `git rev-parse {ref}`.
2. Read tags pointing at that commit: `git tag --points-at {ref}`.
3. Filter to the `{PackageId}/v{semver}` shape using a single regex `^(?<id>.+)/v(?<ver>\d+\.\d+\.\d+)$`. Reject pre-release shapes (v1 explicitly disallows them).
4. Group by `id`; reject any group with more than one tag (exit 4 with `multiple tags for {id} at {ref}: {list}`).
5. For each tag, ask every loaded SDK plugin's `DiscoverAsync` for its project list; find the project whose `PackageId` matches the tag's id.
6. If no project matches → exit 4.
7. Call `StampVersionAsync(project, version, ct)` on the owning SDK. For `--dry-run`, log the diff and skip the write.
8. If zero parseable tags at ref → exit 3.

### `create-tag`

1. Discover projects via every loaded SDK in parallel: `var allProjects = await Task.WhenAll(sdks.Select(s => s.DiscoverAsync(repoRoot, ct)));`.
2. Build the in-repo dependency graph (see "Project graph + affected algorithm").
3. For each project, find its prior tag: `git tag --list "{PackageId}/v*" --merged HEAD`, parse, sort `NuGetVersion` desc, take head. Cache the result keyed by `PackageId` for the duration of the run.
4. Compute `Changed(P)` by shelling out to `git diff --quiet {priorTag}..HEAD -- {projectDir}` and inspecting exit code (0 = no diff, 1 = diff present). When `priorTag` is null, `Changed(P)` is true by definition.
5. Compute the affected set via reverse-BFS from `Changed` (see graph section).
6. Topologically order the affected set (Kahn's algorithm).
7. For each project in topo order, run `VersionPlanner.ComputeNextVersion(project)` (algorithm body lives in `VersionPlanner.cs`, body mirrors PRD pseudocode).
8. Collect the results into a `List<TagPlan> { PackageId, Version, BumpLevel }`.
9. Print the summary lines (same shape as PRD's sample stdout).
10. For each `TagPlan`: `git tag {id}/v{version}`. If `--push`, batch into a single `git push {remote} --tags --follow-tags` at the end (one network round-trip).
11. Exit 0.

#### `git` vs `LibGit2Sharp`

Shell out to `git`. Every CI runner has it; LibGit2Sharp's native libgit2 dep is a chronic source of cross-platform tool-packaging pain (`dotnet tool install -g` does not handle native runtimes well across glibc versions). `Process.Start` cost per `git diff` is negligible at typical-monorepo scale. A `GitClient` wrapper centralizes the boilerplate.

### `publish`

1. Discover projects via every SDK as in `create-tag`.
2. For each project at HEAD that has a tag matching `{PackageId}/v*` at HEAD (i.e., `git tag --points-at HEAD`), invoke `IDevelopmentSdk.PackAsync(project, configuration, ct)` to produce one or more `Artifact { Path, Kind }`. Skip the build step if `--skip-build`.
3. If no artifacts at all → exit 8.
4. Build a routing table: each registry plugin reports `AcceptedKinds` (`["nuget"]`, `["npm"]`, etc.); each artifact's `Kind` matches one or more registries.
5. For each `(artifact, registry)` pair in the routing table, call `registry.PushAsync(artifact, ct)`. Collect failures.
6. If any failure → exit 7 with a per-failure stderr line. Otherwise exit 0.

## SDK plugin: MintPlayer.Verz.Sdks.Dotnet

### Project discovery

`DiscoverAsync(repoRoot, ct)` walks `**/*.csproj` using `Microsoft.Extensions.FileSystemGlobbing`. For each csproj, load via `Microsoft.Build.Evaluation.Project` (eval-only API — no MSBuild build attempted). Skip projects where:
- `IsPackable` evaluates to `false`, or
- `OutputType` is `Exe` or `WinExe` (Library and unspecified are kept; `PackAsTool=true` projects are kept).

Return a `DiscoveredProject { PackageId, ProjectDir, ProjectFile, FrameworkMajor, OwnerSdkId = "dotnet" }`.

### `PackageId` extraction

Precedence: `<PackageId>` → `<AssemblyName>` → `Path.GetFileNameWithoutExtension(projectFile)`. The existing prototype handles the first and third; add the middle layer.

### Framework-major detection

Parse `<TargetFramework>` if single-target; else parse `<TargetFrameworks>` as semicolon-delimited. Apply the existing helper `IsNetTfm` + `ParseNetMajor`. Take `max` across the list (multi-target packages get the highest framework's major as their canonical major). Return `null` if no `netN.M` TFM is found (e.g., `netstandard2.0`-only libraries); `null` triggers the patch-only mode in `ComputeNextVersion`.

### In-repo edges

For each project, yield:
- Every `<ProjectReference Include="..\Other\Other.csproj" />`: resolve target's path, ask `DiscoverAsync`'s cache for its `PackageId`. Edge: `this → that.PackageId`.
- Every `<PackageReference Include="X" />` where `X` matches another in-repo project's `PackageId`. Edge: `this → X`.

The edges are emitted as a `IReadOnlyList<string>` of dependency PackageIds per project; the host assembles them into the global graph.

### Public-API-hash (current)

`create-tag` has not yet packed, so the SDK shells out to `dotnet build -c Release {csproj}`, then loads `bin/{Configuration}/{tfm}/{AssemblyName}.dll` via `Assembly.LoadFrom`, calls `PublicApiGenerator.ApiGenerator.GeneratePublicApi(assembly)`, SHA-256 over the UTF-8 bytes, hex-encode. Results are cached for the run keyed by `(projectFile, source-tree-sha)`. For `publish`, the SDK plugin runs `dotnet pack` then post-processes the produced `.nupkg`: it opens the embedded nuspec, upserts `<PublicApiHash>` and `<FrameworkMajor>` into `<metadata>`, and re-archives. The same `LoadFrom` pinning is fine because the SDK plugin process exits at the end of the invocation.

### Version stamping

Edit `<Version>` in the csproj via `XDocument` round-trip with `LoadOptions.PreserveWhitespace`:

```csharp
var doc = XDocument.Load(projectFile, LoadOptions.PreserveWhitespace);
var ns = doc.Root!.Name.Namespace;
var versionElem = doc.Descendants(ns + "Version").FirstOrDefault();
if (versionElem is null) /* insert into first PropertyGroup */;
else versionElem.Value = version;
doc.Save(projectFile, SaveOptions.DisableFormatting);
```

Preserves comments and indentation. The prototype's regex-based approach in `InitDotnetCommand` is replaced — regex breaks on `<Version Condition="...">` and entity-escaped values.

### Packing

Shell out to `dotnet pack -c Release {csproj} --output {tempDir}` and glob the produced `*.nupkg` (and `*.snupkg` if present) from `tempDir`. Return them as `Artifact { Path, Kind = "nuget" }`. Symbols packages are emitted with `Kind = "nuget-symbols"` so registries can opt into accepting them.

## SDK plugin: MintPlayer.Verz.Sdks.NodeJS

### Workspace discovery

Read in order:
1. Repo-root `package.json#workspaces`: either an array of globs (`["packages/*"]`) or an object with `packages` (yarn classic). Glob via `Microsoft.Extensions.FileSystemGlobbing`.
2. Else `pnpm-workspace.yaml` (parsed via `YamlDotNet`).
3. Else `nx.json#projects` (an object whose keys are workspace member paths).

Resolve globs into a list of absolute directories, each containing a `package.json`. Each is a candidate workspace member.

Filter: `package.json#private == true` are skipped (they are intended to be unpublished). The workspace root itself is also skipped — its `private: true` is the usual convention.

### `PackageId` extraction

`package.json#name`. Scoped names (`@mintplayer/foo`) are kept verbatim; the tag format `{name}/v{ver}` accepts them despite the `@`.

### Framework-major detection

Inspect `dependencies` and `peerDependencies` for keys in fixed precedence: `@angular/core`, `react`, `vue`. First match wins; parse the range:

```csharp
static int? ParseMajor(string range)
{
    var trimmed = range.TrimStart('^', '~', '>', '=', ' ');
    var dot = trimmed.IndexOf('.');
    return dot > 0 && int.TryParse(trimmed[..dot], out var m) ? m : null;
}
```

No match → `null` (patch-only mode). Multiple framework deps (Angular + React microfrontend) → take Angular and warn. Precedence reflects each framework's opinion about cross-package major sync: Angular synchronizes `@angular/*` on a fixed major cadence, React's churn lives in supporting packages, Vue is loosest.

### In-repo edges

For each workspace member, scan `dependencies`, `devDependencies`, and `peerDependencies`. For each entry whose name matches another workspace member's `package.json#name`, emit an edge. Version specifiers are ignored at the edge-extraction stage; the version-bump algorithm reads them only when relevant.

### Public-API-hash (current)

TypeScript projects: shell out to `npx tsc --declaration --emitDeclarationOnly --outDir .verz-types --rootDir {projectDir}`. Enumerate produced `.d.ts` files (`SearchOption.AllDirectories`), sort by relative path (ordinal), normalize line endings to `\n`, strip trailing whitespace, concatenate with `\n`, SHA-256 over UTF-8. Pure-JS (no `tsconfig.json`): hash `package.json#files` plus resolved `main`/`module`/`exports` entry points, concatenated in lexical order.

### Version stamping

Edit `package.json#version` via `System.Text.Json.Nodes.JsonNode`:

```csharp
var node = JsonNode.Parse(File.ReadAllText(path))!;
node["version"] = version;
File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
```

This reformats the file. Acceptance: the CI workspace is throwaway, so reformatting is harmless; the file is not committed.

### Packing

Detect package manager: presence of `pnpm-lock.yaml` → `pnpm pack`, `yarn.lock` → `yarn pack`, else `npm pack`. Shell out, glob the produced `*.tgz` from the package directory, return as `Artifact { Path, Kind = "npm" }`.

## Registry plugins

| Plugin | Accepted kinds | Lookup | Hash retrieval | Push | Auth |
|---|---|---|---|---|---|
| `MintPlayer.Verz.Registries.NugetOrg` | `nuget`, `nuget-symbols` | `FindPackageByIdResource.GetAllVersionsAsync` + `RegistrationResourceV3` for per-version metadata | Read nuspec only (no `.nupkg` download): fetch the registration JSON for `{packageId}/{version}`, follow its `catalogEntry`, GET the `.nuspec` URL embedded there, parse `<PublicApiHash>` and `<FrameworkMajor>` | `dotnet nuget push {path} --source https://api.nuget.org/v3/index.json --api-key <fromConfig>` | `~/.nuget/NuGet.config` `<apikeys>` |
| `MintPlayer.Verz.Registries.GithubPackageRegistry` | `nuget`, `nuget-symbols` | Same protocol as NugetOrg, against `https://nuget.pkg.github.com/{owner}/index.json` | Same nuspec-only flow | `dotnet nuget push {path} --source github` (relies on a registered source named `github` in `NuGet.config`) | `~/.nuget/NuGet.config` `<packageSourceCredentials>` keyed by source name |
| `MintPlayer.Verz.Registries.NpmJS` | `npm` | `GET https://registry.npmjs.org/{pkg}/{version}` returns the published `package.json` blob directly | Read `publicApiHash` and `frameworkMajor` from the JSON response | `npm publish {path}` (executed in a temp dir; no implicit `package.json` lookup) | `~/.npmrc` with `_authToken` |

Push shells out to `dotnet` / `npm`. Reimplementing the NuGet push protocol via `PushCommandResource` is feasible but out of scope: workflow B already has both binaries on PATH, and shelling out absorbs future NuGet.Protocol behavior changes without a Verz update. Auth is never read by Verz; the host trusts the per-tool credential files, and push failures surface the underlying tool's stderr verbatim.

## Public-API-hash injection (in the .NET SDK plugin)

There is no separate MSBuild task package. The .NET SDK plugin owns both halves of the hash pipeline — computation (used during `create-tag`) and injection (used during `publish`). This eliminates an extra plugin assembly, a consumer-side `Directory.Build.props` setup step, and the AssemblyLoadContext-vs-MSBuild-host friction that comes with shipping a task DLL into the consumer's build process.

Computation runs inside `DotnetSdk.ComputePublicApiHashAsync` (see SDK plugin section). Injection runs inside `DotnetSdk.PackAsync`:

1. Shell out to `dotnet pack -c {Configuration} {csproj} --output {tempDir}`.
2. For each produced `*.nupkg` (excluding `*.snupkg`): open the package as a `ZipArchive` in update mode, locate the embedded `.nuspec`, edit it via `XDocument` to upsert `<PublicApiHash>` and `<FrameworkMajor>` under `<metadata>`, write back. The symbols package is left untouched.
3. Return `Artifact { Path, Kind = "nuget" }` per main package and `Kind = "nuget-symbols"` per symbols package.

The non-fatal failure stance from the original `InjectPublicApiHashTask` carries over: a missing or unparseable nuspec yields a warning, not a hard failure. `verz publish` still succeeds (and the consumer can detect missing fields at lookup time during the next `create-tag`, which surfaces as a cold-start error).

Trade-off: a manual `dotnet pack` outside `verz publish` produces an un-stamped nupkg. This is acceptable because Verz only reads prior hashes from packages it published itself, and a never-published library starts at the `INITIAL` branch of the version algorithm. No prior hash is needed at bootstrap.

## Project graph + affected algorithm

### Data structures

```csharp
public sealed class ProjectNode
{
    public required string PackageId { get; init; }
    public required string ProjectDir { get; init; }
    public required string OwnerSdkId { get; init; }
    public List<string> Dependencies { get; } = new();  // in-repo PackageIds
    public BumpLevel Bump { get; set; } = BumpLevel.None;
    public SemanticVersion? PriorVersion { get; set; }
    public SemanticVersion? NewVersion { get; set; }
}
public sealed class ProjectGraph
{
    public Dictionary<string, ProjectNode> Nodes { get; } = new(StringComparer.Ordinal);
}
```

### Construction

`ProjectGraphBuilder.Build(IEnumerable<DiscoveredProject>)`:
1. Collect every `DiscoveredProject` into `Nodes` keyed by `PackageId`. Duplicate `PackageId` across SDKs is a configuration error (exit 9 with a distinct message; the implementer can decide whether to reuse exit 9 or carve out 10 — keep 9 for v1 to match the PRD's exit-code table).
2. For each project, ask the owning SDK for its raw dependency list, filter to those whose target is `Nodes.ContainsKey`, append to `node.Dependencies`.

### Cycle detection

Depth-first search with three colors (white/gray/black). On finding a back-edge into a gray node, build the cycle path and throw `CycleException(cyclePath)`. The CLI handler catches this at the boundary and exits with code 9, printing the cycle as `A → B → C → A`.

### Topological order

Kahn's algorithm: start with all nodes whose `Dependencies.Count == 0`; emit and remove; repeat. Result is a list ordered such that `node.Dependencies` are all emitted before `node`. This order is what `ComputeNextVersion` walks so that each project's dep bumps are already known by the time it is evaluated.

### Affected closure

`Affected(graph, changedSet)` runs BFS on the *reverse* graph: build `reverseAdj[dep] = [consumer]` once, then start BFS from `changedSet`, visiting every consumer transitively. Result includes the changed set plus all transitive consumers. Time complexity O(V + E); space O(V).

```csharp
var reverse = new Dictionary<string, List<string>>();
foreach (var n in graph.Nodes.Values)
    foreach (var dep in n.Dependencies)
        reverse.GetOrCreate(dep).Add(n.PackageId);

var affected = new HashSet<string>(changedSet);
var queue = new Queue<string>(changedSet);
while (queue.TryDequeue(out var id))
    if (reverse.TryGetValue(id, out var consumers))
        foreach (var c in consumers)
            if (affected.Add(c)) queue.Enqueue(c);
```

## Versioning algorithm — implementation notes

The PRD's pseudocode is the spec; implementation notes only:

- **`priorTag` lookup**: `git tag --list "{PackageId}/v*" --merged HEAD`, parse via the same regex used in `set-versions`, sort by `NuGetVersion` descending, take head. Result cached per-`PackageId` for the duration of the run.
- **`priorPkg` download**: for each registry in declared order, call `IPackageRegistry.LookupAsync(packageId, priorVersion)`. The first non-null result wins. The returned `PriorPackageInfo` is in-memory cached for the run, keyed by `(packageId, version)`, to avoid the (potentially slow) registration-blob round-trip on multi-project repos where the same prior tag is referenced multiple times.
- **Framework-major comparison**: the algorithm compares ints; nullable handling is `if (current is null || prior is null) skip the framework comparison and fall through to hash-based logic`.
- **Hash comparison**: `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` (the existing prototype's choice). Hex casing is normalized to upper by `Convert.ToHexString`, but consumers in the wild may emit lower-case; ordinal-ignore-case absorbs both.
- **Transitive demotion rule (PRD line: minor → patch when consumer hash unchanged)**: implemented after the dep-bump rollup. Distinct from the absolute floor a major dep enforces (major always wins).
- **First release**: `ApplyBump(null, INITIAL)` returns `Version(framework ?? 0, 0, 0)`. Tag is created even if `Changed(P)` is technically vacuous for a never-tagged package.

## Error handling and exit codes

Each error-emitting site throws a typed exception derived from `VerzException(int exitCode, string message)`:

| Exit | Exception | Origin |
|---|---|---|
| 2 | `InitConflictException` | `init` when `verz.json` already exists |
| 3 | `NoTagsAtRefException` | `set-versions` when filtered tag set is empty |
| 4 | `UnmatchedTagException` | `set-versions` when a tag's `PackageId` is not in any SDK's discovery |
| 5 | `ColdStartException` | `VersionPlanner` when every registry returns `null` for `Lookup(priorVersion)` |
| 6 | `FrameworkDowngradeException` | `VersionPlanner` when current major < prior major |
| 7 | `PublishFailureException` | `publish` when any push fails |
| 8 | `NoArtifactsException` | `publish` when zero artifacts produced |
| 9 | `CycleException` | `ProjectGraphBuilder` when DFS finds a back-edge |

The CLI boundary in `Program.cs` wraps every command invocation in a `try/catch (VerzException ex)`, writes `ex.Message` to stderr, and returns `ex.ExitCode`. Stack traces are written only to the verbose log via `ILogger.LogDebug(ex, ...)`; they never reach stderr because automated CI parses stderr for the message.

## Testing strategy

### Unit tests

Per-plugin xUnit projects mock host services (`ILogger<T>`, `IFileSystem`). For the .NET SDK, fixture csprojs under `Tests/Fixtures/` cover single-target, multi-target, no-`<PackageId>`, no-`<AssemblyName>`, `IsPackable=false`, `OutputType=Exe`, `<ProjectReference>` chains, and sibling `<PackageReference>` edges. `VersionPlanner` tests parametrize the PRD-table cases (initial, no-change, internal-refactor, public-API-change, framework-bump, transitive-patch, transitive-minor-with-demotion, transitive-major) as `[Theory]` rows.

### Integration tests

`MintPlayer.Verz.IntegrationTests` packs the `MintPlayer.Verz` nupkg in a fixture, installs it via `dotnet tool install --tool-path {temp} MintPlayer.Verz --add-source {tempFeed}`, then per-test creates a temp git repo, writes a `verz.json` pointing at a `file://` registry pre-loaded with the in-repo SDK + registry plugins, invokes `{temp}/verz` via `Process.Start`, and asserts on the resulting tags (via LibGit2Sharp, test-only dep), the in-place file contents (`<Version>`), and the exit code. The repo's own `MintPlayer.*` libraries serve as multi-package integration fixtures.

### Public-API-hash determinism

Hash the same assembly twice (separate `MetadataLoadContext` instances) and assert equality. Hash two assemblies differing only in a `private` field and assert equality (`PublicApiGenerator` excludes private members). Hash two assemblies differing by one `public` method and assert inequality. Adding `[InternalsVisibleTo]` must not change the hash.

### Plugin-loading test

Pack a minimal `IDevelopmentSdk` implementation into a temp `file://` v3 feed. Point `verz.json.Registries` at it. Assert the tool discovers and instantiates the SDK and that calls round-trip through the load context without `InvalidCastException`.

## Milestones

| # | Deliverable | Acceptance test | Effort |
|---|---|---|---|
| 1 | `MintPlayer.Verz.Abstractions` + `MintPlayer.Verz` skeleton with `init` subcommand and the plugin loader (no SDKs/registries used). | `verz init` in an empty temp dir creates a valid `verz.json`; `verz init` again returns exit 2. | M |
| 2 | `MintPlayer.Verz.Sdks.Dotnet` v1: discovery, framework detection, version stamping. Wire into `set-versions`. | A one-package fixture repo with a `Foo/v1.2.3` tag at HEAD: `verz set-versions` writes `<Version>1.2.3</Version>` into the csproj. | M |
| 3 | `DotnetSdk.ComputePublicApiHashAsync`: load the built assembly, run `PublicApiGenerator.ApiGenerator.GeneratePublicApi`, SHA-256, hex-encode. | Determinism: same source -> same hash across two builds. Sensitivity: adding a public method changes the hash; adding a private member does not. | S |
| 4 | Project graph + affected algorithm + `VersionPlanner` + `create-tag --dry-run`. `MintPlayer.Verz.Registries.NugetOrg` (Lookup only, no Push). | On this repo (`MintPlayer.Dotnet.Tools`) with all libs tagged, `verz create-tag --dry-run` after a no-op commit reports zero affected; after editing one library, reports that library plus its transitive consumers. | L |
| 5 | `NugetOrg.Push` + `verz publish` + workflow B integration. | End-to-end: feature branch → merge to master → workflow A creates `MintPlayer.Foo/v10.0.5` → workflow B publishes that one package to nuget.org. | M |
| 6 | `MintPlayer.Verz.Sdks.NodeJS` + `MintPlayer.Verz.Registries.NpmJS`. | A small Angular workspace fixture: editing one library produces one `@scope/foo/v1.0.1` tag and one publish. Cross-language graph (a .NET lib alongside a Node lib) is correctly partitioned. | L |
| 7 | `MintPlayer.Verz.Registries.GithubPackageRegistry` + dogfooding: Verz publishes itself via Verz. | The `MintPlayer.Verz` package is live on nuget.org with a version that was decided by `create-tag`, not hardcoded. The repo's existing `.github/workflows/dotnet-build-master.yml` is replaced by the two-workflow split documented in `Verz/MintPlayer.Verz/README.md`. | M |

Effort legend: S = ≤1 day, M = 2–4 days, L = 5–10 days. Estimates assume one engineer with prior MSBuild/NuGet.Protocol exposure.

## Risks & open questions (implementation-specific)

- **ALC + NuGet.Protocol shared state.** `SourceRepositoryProvider` builds its own object graph. The loader keeps `NuGet.*` types in the default context and isolates only plugin-author code. The shared-types list must include any `NuGet.*` type that crosses the plugin boundary; in v1 only `NuGet.Versioning.NuGetVersion` does.
- **In-process MSBuild vs subprocess.** `Microsoft.Build.Locator` + `Microsoft.Build.Evaluation` in-process is faster but pins MSBuild version to whatever is on disk. v1 uses the eval API for *reading* csprojs (parse-only) and shells out for *building*.
- **`LibGit2Sharp` vs `git` subprocess.** Shell out (rationale above). ~200ms per `Process.Start` × tens of `git diff` calls is sub-second on any CI runner.
- **NodeJS multi-framework workspace.** Angular + React in one workspace: SDK picks Angular and warns; the warning text instructs the user to split the workspace if framework semantics matter.
- **`InternalsVisibleTo` types.** Not part of the public surface per `PublicApiGenerator`; excluded automatically. A determinism test asserts this so a future `PublicApiGenerator` change does not silently break the contract.
- **Symbols packages (`.snupkg`).** Emitted by the .NET SDK as `Artifact { Kind = "nuget-symbols" }`. Registries opt-in via `AcceptedKinds`. Per the PRD's open question: symbols-push failure fails closed (part of exit 7).
- **Plugin DI extension.** v1 plugins cannot register services. Future need is a side-channel registration callback that does not require changing `IDevelopmentSdk`.
- **Performance ceiling.** For a 100-package monorepo target: plugin load < 5s (one-time), discovery < 2s, graph build < 200ms, version planning < 30s, tag creation < 5s. First measured at Milestone 4.

## Out of scope for this plan

Identical to the PRD's "Out of scope for v1" list — see `docs/prd/verz/PRD-initial.md` for the canonical enumeration (pre-release/build-metadata suffixes, dep-graph-aware build ordering, specialized `PackAsTool` artifact handling, Docker, Conventional Commits, release notes/CHANGELOG, yanking/unlisting, cross-repo coordination). When v2 work starts, the PRD and this plan are updated together.

---

**Spec discipline.** This plan, the PRD at `docs/prd/verz/PRD-initial.md`, and the customer-facing README at `Verz/MintPlayer.Verz/README.md` are a three-document set. Any change to a subcommand surface, flag, exit code, `verz.json` schema, error message, or CI workflow shape is a PRD change first; the README and this plan are updated in the same commit. Drift between the three is a release-blocking defect — reviewers should reject any pull request that touches one without justifying why the others do not need to move.
