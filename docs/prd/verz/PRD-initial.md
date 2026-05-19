# Verz

A .NET global tool that derives library versions from git tags, stamps them at build time, and publishes the resulting packages to NuGet, npm, and GitHub Packages from a single repo-local config.

> **Companion document.** End-user setup instructions ship with the tool package at [`Verz/MintPlayer.Verz/README.md`](../../../Verz/MintPlayer.Verz/README.md). Any change in this PRD that affects user-visible behavior — subcommand surface, flags, `verz.json` schema, exit codes, CI workflow shape, or error messages — must be reflected in that README in the same commit. Drift between the two is a defect.

## Problem statement

Maintainers of multi-library .NET and JavaScript repositories repeatedly solve the same release problems by hand. The `<Version>` element ends up hardcoded in every `.csproj`, requiring a commit (and a code review) every time a package needs a new version. Release pipelines accumulate one-off bash and PowerShell scripts that parse tags, compute deltas, and call `dotnet pack` and `dotnet nuget push` per project. When a repo grows to include NodeJS workspaces alongside .NET, the language-specific tooling forks again: `npm version`, `lerna`, custom workspace scripts. The result is fragile, opaque, and difficult to onboard contributors to.

Three concrete questions become hard to answer in such repos:

1. *What commit produced this published package?* Without a tag-per-package convention, the answer is buried in CI logs.
2. *Did the public API actually change between two versions?* Without a stored fingerprint, reviewers guess.
3. *Why was this a minor and not a patch?* Without a deterministic rule, the answer is "because the release engineer said so."

Verz fixes these by making the git tag the canonical source of a package's version, computing the next version deterministically from the package's public API surface and target framework, and stamping versions into projects at CI time only, never in committed sources.

## Goals & non-goals

### Goals

- **Single source of truth.** Every published version corresponds to exactly one git tag of the form `{PackageId}/v{semver}`.
- **No committed version strings.** `<Version>` and `package.json#version` are placeholders in the repo; their values are written only on tag-push CI runs.
- **Deterministic version bumps.** Given the prior tag and the current commit, the next version is a pure function: no human input, no commit-message parsing.
- **Pluggable by language.** New project types (Rust, Python, Go) can be added by shipping an `IDevelopmentSdk` NuGet package and listing it in `verz.json`.
- **Pluggable by destination.** New registries can be added by shipping an `IPackageRegistry` NuGet package; the host orchestrates auth via the registry's standard credential file.
- **Polyglot-aware.** A single `verz.json` in a repo root covers .NET libraries, NodeJS libraries, and any other language whose plugin is listed.
- **Skip unchanged libraries.** A library whose source tree is unchanged since its prior tag, and whose in-repo dependencies are likewise unchanged, produces no new tag and no new package. The registry is not polluted with duplicate-content versions.
- **Honor the in-repo dependency graph.** When an in-repo dependency bumps, downstream consumers in the same repo bump too — at a level that matches the dependency's bump. A patch in A means at least a patch in B; a major in A means a major in B. The graph is computed from `<ProjectReference>` and `<PackageReference>` (.NET) and from npm/yarn/pnpm workspaces plus `dependencies` / `peerDependencies` (NodeJS).

### Non-goals

- **Not a build system.** Verz does not replace `dotnet build`, `dotnet pack`, `npm ci`, or `npm pack`. It invokes them.
- **Not a commit-message linter.** Verz does not read commit messages, PR titles, or labels. Conventional Commits are out of scope.
- **No pre-release suffixes in v1.** No `-beta.3`, no `-rc.1`, no `+build.meta`. Only stable `MAJOR.MINOR.PATCH`.
- **No build ordering.** Verz does not topologically sort inter-package references for the purposes of `dotnet build` or `npm run build`. Solution-level build order is the caller's responsibility (`dotnet build` of the solution file handles it implicitly). Verz does walk the dependency graph, but only to decide *which* packages bump and *to which level*, not in what order they compile.
- **No version-from-thin-air.** If the prior tag's package cannot be retrieved from any configured registry, Verz fails closed (see open questions).

## Personas & user stories

**Repo maintainer / CI author.** Owns `.github/workflows/`. Wants a release to be one CLI invocation per stage.

**Library author.** Adds or edits public types in a library. Wants to know, before merging, what version the next release will be — without having to remember to bump a number.

**Library consumer.** Downstream developer who depends on the published packages. Wants confidence that a minor-version bump cannot break their build, and that a major-version bump is signalled clearly.

Stories:

1. *As a repo maintainer*, I want the act of merging a PR to master to produce one git tag per affected package, so that the tag-push workflow can deterministically build and publish that exact set.
2. *As a library author*, I want adding a new public method to result in a minor bump rather than a patch, so that semantic-versioning expectations hold without me editing any file.
3. *As a library author working on a .NET 10 upgrade*, I want the major version to bump automatically when the target framework's major changes, so that NET9 consumers are not silently broken.
4. *As a repo maintainer adopting NodeJS support*, I want to add NodeJS publishing by editing only `verz.json`, so that I don't have to learn or write a second pipeline.
5. *As a library consumer*, I want every published package to carry a public-API fingerprint, so that I can diff two versions and see whether a patch release is in fact a patch.

## End-to-end workflow

The release loop is split across two GitHub Actions workflows:

```
+--------------------+      +---------------------+      +---------------------+
| Pull request opens | ---> | PR merged to master | ---> | Tag push to refs/   |
|                    |      | (workflow A runs)   |      | tags/{Pkg}/v{ver}   |
+--------------------+      +----------+----------+      +----------+----------+
                                       |                            |
                                       v                            v
                         +-------------------------+   +---------------------------+
                         | verz create-tag --push  |   | actions/checkout @ tag    |
                         | - per package:          |   | verz set-versions         |
                         |   - read prior tag      |   | dotnet build / npm ci     |
                         |   - compute next semver |   | verz publish              |
                         |   - git tag + git push  |   +---------------------------+
                         +-------------------------+
```

### First release (no prior tag)

For a package `MintPlayer.Foo` with no prior `MintPlayer.Foo/v*` tag, the .NET SDK reports its detected framework major (e.g., 10 for `net10.0`). `create-tag` produces version `10.0.0`. The public-API-hash is computed from the current commit and stored when the package is later published by the tag-push workflow.

### Cross-major framework bump (NET9 to NET10)

Prior tag `MintPlayer.Foo/v9.4.2` exists; the package pulled from the registry reports `<PublicApiHash>abc...</PublicApiHash>` and framework major 9. The current commit's TFM is now `net10.0`. Major bumps unconditionally: next version is `10.0.0`. The public-API-hash diff is not consulted because the framework-major rule fires first.

### No-change case (skip)

Prior tag `MintPlayer.Foo/v10.0.4` exists. `git diff MintPlayer.Foo/v10.0.4..HEAD -- <project-dir>` reports zero changed files. No in-repo dependency of `MintPlayer.Foo` has bumped on this commit either. `create-tag` emits no tag for `MintPlayer.Foo`, and the package is not republished by workflow B. Consumers continue to resolve `10.0.4`. This is the common case for any commit that touches some libraries but not others.

### Internal-refactor case (patch bump)

Prior tag `MintPlayer.Foo/v10.0.4` exists with public-API-hash `def...`. The source tree did change (some files in the project dir are modified), but the recomputed public-API-hash is still `def...`. Next version is `10.0.5`. Internal refactors and test-only changes land here.

### Transitive-bump case (in-repo dependency moved)

`MintPlayer.Foo` had no direct source changes since its prior tag, but it has a `<ProjectReference>` to `MintPlayer.Core`, and `MintPlayer.Core` is bumping from `10.2.3` to `10.3.0` (minor) on this commit. `MintPlayer.Foo` therefore bumps at least patch. If its own public-API-hash is unchanged, it goes `10.0.4 → 10.0.5`. If the bump of `MintPlayer.Core` happens to leak into `MintPlayer.Foo`'s public surface (e.g., a re-exported type) and its hash *does* change, it bumps minor instead. A major bump in `MintPlayer.Core` forces a major in `MintPlayer.Foo`.

## Subcommand specification

### Summary

| Subcommand | Purpose | Reads | Writes |
|---|---|---|---|
| `verz init` | Scaffold `verz.json` and optionally stamp placeholders | repo CWD | `verz.json`, optionally `.csproj` / `package.json` |
| `verz set-versions` | Apply tag-derived versions at CI time | git tags at HEAD | in-place project files (uncommitted) |
| `verz create-tag` | Compute next version(s) and tag | prior tags, prior packages, current sources | git tags (local, optionally pushed) |
| `verz publish` | Pack and push every library to every registry | built outputs, `verz.json` | nothing local; remote package registries |

### `verz init`

```
verz init [--stamp-placeholders] [--registry <id>=<url>]...
```

| Flag | Description |
|---|---|
| `--stamp-placeholders` | Insert `<Version>0.0.0-placeholder</Version>` into every detected `.csproj` and set `"version": "0.0.0-placeholder"` in every detected `package.json`. |
| `--registry id=url` | Add a registry entry. Repeatable. Defaults to `nuget.org` if none provided. |

Exit codes: `0` success, `2` `verz.json` already exists.

Sample stdout:

```
Created verz.json (2 registries, 0 tools).
Detected 14 .NET projects. Stamped <Version>0.0.0-placeholder</Version>.
Next: edit verz.json to add SDK and registry plugins, then commit.
```

### `verz set-versions`

```
verz set-versions [--ref <ref>] [--dry-run]
```

| Flag | Description |
|---|---|
| `--ref` | Git ref to read tags from. Defaults to `HEAD`. |
| `--dry-run` | Print what would change without modifying files. |

Reads every tag pointing at the resolved ref, filters to `{PackageId}/v{semver}` shapes, and for each loaded SDK plugin asks the plugin to write that version into the matching project file. For .NET this sets `<Version>` in the `.csproj`; for NodeJS, the `"version"` field in `package.json`. Packages whose `{PackageId}/v*` tag is not at HEAD are left untouched — they were skipped by `create-tag` and continue to resolve at their prior version.

Exit codes: `0` success, `3` no matching tags found at ref, `4` tag references a package the loaded SDKs cannot locate.

Sample stdout:

```
HEAD = 7a1c2e9 (tags: MintPlayer.Foo/v10.0.5, MintPlayer.Bar/v3.1.0)
[dotnet] MintPlayer.Foo -> src/MintPlayer.Foo/MintPlayer.Foo.csproj: 10.0.5
[dotnet] MintPlayer.Bar -> src/MintPlayer.Bar/MintPlayer.Bar.csproj: 3.1.0
2 projects updated.
```

### `verz create-tag`

```
verz create-tag [--push] [--package <id>]... [--remote <name>]
```

| Flag | Description |
|---|---|
| `--push` | After creating local tags, `git push <remote> <tag>` for each. |
| `--package` | Limit computation to listed package IDs. Repeatable. Default: all discovered. |
| `--remote` | Git remote name for `--push`. Default `origin`. |

For each package, the tool builds the in-repo dependency graph (see *Project graph & affected*), computes the *affected* set since each package's prior tag, and for every affected package: resolves the most recent tag of the form `{PackageId}/v*` reachable from HEAD, downloads that package from the first registry that lists it, reads the stored public-API-hash and framework-major from the package metadata, computes the current commit's hash and framework-major, and applies the version algorithm (see below). Packages outside the affected set are skipped entirely — no tag is created. It creates `{PackageId}/v{newVersion}` as a lightweight annotated tag for each bumped package.

Exit codes: `0` success (zero or more tags created), `5` prior tag exists but prior package is unretrievable from any configured registry (cold-start failure), `6` framework major decreased (refusal).

Sample stdout:

```
Graph: 5 .NET projects, 1 NodeJS workspace, 7 edges.
MintPlayer.Foo: prior 10.0.4 (hash def...), source changed, current hash def..., fx 10 -> 10. PATCH -> 10.0.5
MintPlayer.Bar: prior 3.0.7 (hash 111...), source changed, current hash 222..., fx 3 -> 3. MINOR -> 3.1.0
MintPlayer.Baz: no prior tag, fx 10. INITIAL -> 10.0.0
MintPlayer.Qux: prior 10.1.2, source unchanged, dep MintPlayer.Foo bumped PATCH. TRANSITIVE PATCH -> 10.1.3
MintPlayer.Quux: prior 10.0.9, source unchanged, no dep changes. SKIP
Created 4 tags. Pushed to origin.
```

### `verz publish`

```
verz publish [--configuration <cfg>] [--skip-build] [--registry <id>]...
```

| Flag | Description |
|---|---|
| `--configuration` | Build configuration. Default `Release`. |
| `--skip-build` | Use existing build outputs instead of invoking the SDK plugin's pack step. |
| `--registry` | Limit publishing to listed registry IDs. Repeatable. |

For each SDK plugin, packs every project it owns (calling `dotnet pack` or `npm pack` internally). For each `IPackageRegistry` plugin, pushes every produced artifact whose project type matches the registry's accepted artifact kinds (a NuGet registry rejects `.tgz`, an npm registry rejects `.nupkg`).

Exit codes: `0` all pushes succeeded, `7` at least one registry rejected at least one package, `8` no artifacts produced.

## verz.json schema

### Single flat `Plugins` list

`verz.json` declares two top-level lists: `Registries` (publish destinations and plugin sources) and `Plugins` (NuGet package IDs to load). The `Plugins` list is flat — SDK plugins and registry plugins share the same array. Verz inspects each loaded assembly via reflection and registers concrete types implementing `IDevelopmentSdk` or `IPackageRegistry` accordingly. With the small number of plugins per repo (typically 3–6) the reflection cost is negligible, and keeping the list flat removes a class of misconfiguration where a user puts an SDK plugin under the registries section or vice versa.

### Field reference

| Field | Type | Required | Description |
|---|---|---|---|
| `Registries` | array of object | yes | Package registries. Used both for plugin resolution and as publish destinations. |
| `Registries[].id` | string | yes | Stable local identifier; used in `--registry` flags and CLI output. |
| `Registries[].url` | string (URL) | yes | NuGet v3 index URL or npm registry URL. The plugin chooses how to interpret it. |
| `Registries[].kind` | `"nuget" \| "npm"` | no | Hint for plugin-resolution order. If omitted, Verz infers from URL shape. |
| `Plugins` | array of string \| object | yes | NuGet package IDs of plugins (SDK or registry). Each entry is either a bare string `"MintPlayer.Verz.Sdks.Dotnet"` or an object `{ "id": "...", "version": "..." }`. |

Full shape:

```json
{
  "$schema": "https://mintplayer.com/verz/v1/schema.json",
  "Registries": [
    {
      "id": "nuget.org",
      "kind": "nuget",
      "url": "https://api.nuget.org/v3/index.json"
    },
    {
      "id": "mintplayer-github",
      "kind": "nuget",
      "url": "https://nuget.pkg.github.com/mintplayer/index.json"
    },
    {
      "id": "npmjs",
      "kind": "npm",
      "url": "https://registry.npmjs.org/"
    }
  ],
  "Plugins": [
    "MintPlayer.Verz.Sdks.Dotnet",
    "MintPlayer.Verz.Sdks.NodeJS",
    "MintPlayer.Verz.Registries.NugetOrg",
    "MintPlayer.Verz.Registries.GithubPackageRegistry",
    "MintPlayer.Verz.Registries.NpmJS"
  ]
}
```

Plugin packages are themselves resolved from the entries in `Registries` (in declared order), which means a repo can self-host its plugin packages on its own GitHub Packages feed.

## Plugin contracts

### `IDevelopmentSdk`

A development-SDK plugin owns one project type. Its responsibilities:

- **Discovery.** Given the repo CWD, enumerate every project of its kind. The .NET SDK walks for `.csproj` files that produce packable libraries (`IsPackable` not false, `OutputType` is `Library`). The NodeJS SDK walks for `package.json` files that have `"private": false` (or no `"private"` key) and are not the workspace root.
- **Identity.** For each discovered project, report a stable `PackageId`. For .NET this is `<PackageId>` (or `<AssemblyName>`). For NodeJS this is `package.json#name`.
- **Framework detection.** Report a framework-major integer or null if undetectable. .NET reads `<TargetFramework>` and parses `netN.M`. NodeJS inspects `dependencies` and `peerDependencies` for `@angular/core`, `react`, or `vue` and parses their major from semver ranges (caret/tilde stripped).
- **Public-API hashing.** Given a built artifact path, compute the public-API-hash. The host calls this only when a fresh hash is needed.
- **Version stamping.** Given a project and a target version string, write it into the project file in-place. No commit; the file is dirty in the CI workspace only.
- **Packing.** Produce one or more artifact files. Return their paths and a kind tag (`nuget` or `npm`).

### `IPackageRegistry`

- **Capability declaration.** Report which artifact kinds it accepts.
- **Lookup.** Given a `PackageId` and version, return the package's metadata if listed, including any embedded public-API-hash and framework-major. Verz calls this during `create-tag` to read the prior fingerprint.
- **Download.** Given a `PackageId` and version, write the package file to a temp path. Used when `Lookup` is insufficient (NuGet metadata is queryable without download; npm tarball metadata requires fetching the tarball).
- **Push.** Upload a single artifact. Auth flows through the host's native credential file: `~/.nuget/NuGet.config` for nuget plugins, `~/.npmrc` for npm. The plugin must not prompt; CI runs are non-interactive.

### Host guarantees

- CWD is the repo root for the lifetime of the command.
- A `CancellationToken` is passed to every async method and must be honored.
- Diagnostic logging is via a host-provided `ILogger`; plugins do not write to stdout directly.
- Plugins target `net10.0`. The core interfaces library `MintPlayer.Verz.Abstractions` multitargets `net8.0; net9.0; net10.0` so a plugin author who prefers an LTS TFM is not blocked.

### Authoring a plugin

A new plugin is a NuGet package containing a single assembly that references `MintPlayer.Verz.Abstractions` and exports one or more types implementing `IDevelopmentSdk` or `IPackageRegistry`. The package is published to any feed listed in the consuming repo's `verz.json` `Registries`. Verz discovers types by reflection; no `[Plugin]` attribute is required.

## Project graph & affected

Before computing any versions, `create-tag` builds the in-repo project graph and the affected set.

### Building the graph

Each loaded `IDevelopmentSdk` plugin enumerates the projects it owns and reports each project's `PackageId` plus the list of *in-repo* dependencies. A dependency is in-repo if its identifier resolves to another project in the same `verz.json`-rooted workspace.

For the .NET SDK:

- Every `<ProjectReference>` is an in-repo edge. The target csproj's `PackageId` (or `AssemblyName`) is the edge head.
- Every `<PackageReference Include="X">` where `X` matches the `PackageId` of another in-repo project is also an in-repo edge. This catches the pattern where libraries reference each other via NuGet rather than ProjectReference.

For the NodeJS SDK:

- The repo root's `package.json` is inspected for `workspaces` (npm/yarn), `pnpm-workspace.yaml` (pnpm), or `nx.json` `projects` (Nx). Members of the workspace are the candidate in-repo set.
- Each member's `dependencies`, `devDependencies`, and `peerDependencies` are scanned. Entries whose `name` matches another workspace member's `package.json#name` are in-repo edges. Version range semantics are ignored at this stage; the edge exists regardless of caret/tilde.

The combined graph is a single directed graph keyed by `PackageId` (.NET) or `package.json#name` (NodeJS), with no cross-language edges in v1 — a .NET project cannot in-repo-depend on a NodeJS package.

### Computing affected

For each project `P`, define `Changed(P)` as true if `git diff {P.PriorTag}..HEAD -- <P.ProjectDir>` reports any modified path, or if `P` has no prior tag at all (it is unversioned). The `<P.ProjectDir>` is the directory containing the `.csproj` or `package.json`; subprojects nested under it but with their own project file are excluded.

The affected set is the transitive closure: `P` is affected if `Changed(P)` is true, or if any project `Q` with an in-repo edge `Q ← P` (i.e., `P` depends on `Q`) is affected. The graph is traversed bottom-up so each project is visited once.

Packages outside the affected set produce no tag and no publish. They are reported in `--dry-run` output but no action is taken.

### Transitive bump rule

When project `P` is affected only because an in-repo dependency `Q` bumped (i.e., `Changed(P)` is false but at least one dep is affected), the version bump for `P` matches the highest bump level of any of its affected dependencies:

- Any dep bumped major → `P` bumps major.
- Else any dep bumped minor → `P` bumps at least patch. If `P`'s own recomputed public-API-hash also differs from its prior tag's, `P` bumps minor instead.
- Else any dep bumped patch → `P` bumps patch.
- Else (all deps skipped) → `P` is also skipped.

When `Changed(P)` is true *and* a dep also bumped, the rules combine: the dep-driven bump becomes a lower bound, and the algorithm in *Versioning algorithm* below computes the source-driven bump; the actual bump is the higher of the two.

## Versioning algorithm

The algorithm runs per package, in graph topological order (deepest dependencies first, so by the time `P` is evaluated, every `Q` it depends on has been decided).

```text
ComputeNextVersion(package):
  priorTag = MostRecentTagReachable(HEAD, "{package.Id}/v*")
  sourceChanged = Changed(package)                # see "Computing affected"
  depBumps = [d.BumpLevel for d in InRepoDeps(package) if d.BumpLevel != SKIP]

  if not sourceChanged and depBumps is empty:
    return SKIP                                   # no new tag, no publish

  if priorTag is null:
    return Version(package.CurrentFrameworkMajor ?? 0, 0, 0), BumpLevel = INITIAL

  priorPkg = DownloadFromFirstRegistry(package.Id, priorTag.Version)
  if priorPkg is null:
    error E5  # cold start: cannot reconstruct fingerprint

  if package.CurrentFrameworkMajor != null
     and priorPkg.FrameworkMajor != null
     and package.CurrentFrameworkMajor < priorPkg.FrameworkMajor:
    error E6  # downgrade refused

  # Source-driven bump
  if package.CurrentFrameworkMajor != null
     and priorPkg.FrameworkMajor != null
     and package.CurrentFrameworkMajor > priorPkg.FrameworkMajor:
    sourceBump = MAJOR
  else if sourceChanged and package.CurrentPublicApiHash != priorPkg.PublicApiHash:
    sourceBump = MINOR
  else if sourceChanged:
    sourceBump = PATCH
  else:
    sourceBump = NONE

  # Dep-driven bump
  depBump = MaxLevel(depBumps)                    # MAJOR > MINOR > PATCH > NONE
  if depBump == MINOR and package.CurrentPublicApiHash == priorPkg.PublicApiHash:
    depBump = PATCH                               # demote: dep bumped minor but
                                                  # our own API didn't move

  effectiveBump = MaxLevel(sourceBump, depBump)

  return ApplyBump(priorTag, effectiveBump), effectiveBump
```

`ApplyBump` is the standard semver step: MAJOR → `(M+1).0.0`, MINOR → `(M).(N+1).0`, PATCH → `(M).(N).(P+1)`.

### Edge cases

- **No prior tag.** Initial version is `{frameworkMajor}.0.0`. If framework-major is undetectable (NodeJS plugin returns null), initial version is `0.0.0`. Initial releases always tag, even if source changes since "empty" are technically trivial.
- **Skip case in CI.** If every package skips on a given commit, `verz create-tag` exits `0` with the message `No tags created (0 packages affected).` Workflow B never fires because no tag is pushed.
- **Prior tag, prior package missing from every registry.** Refusal (exit 5). The maintainer must either publish the missing version out-of-band or delete the orphan tag.
- **Framework major decreased.** Refusal (exit 6). Recommendation: error, not warn. A silent major-downgrade is a category of mistake that semver cannot recover from.
- **Multiple tags at HEAD.** Expected, one per affected package. `set-versions` processes each independently. `create-tag` will not produce more than one tag per package per invocation.
- **No detectable framework (NodeJS plain library).** The major is pinned to whatever the prior tag's major was; only minor and patch can move. Initial release in this case is `0.0.0`, and the only way to reach `1.0.0` is to add a recognized framework dependency or to override with a future explicit-bump flag (out of scope for v1).
- **Cycles in the dependency graph.** A cycle blocks topological ordering. `verz create-tag` exits with a dedicated error (exit `9`) listing the cycle. Cycles are an existing-codebase bug and should be fixed there.

## Tag format

Tags use `{PackageId}/v{semver}`, e.g., `MintPlayer.Foo/v10.0.5`. Rationale:

- **vs. `v{semver}`.** Insufficient: a monorepo with 15 packages cannot share a single version line. `v10.0.5` is ambiguous.
- **vs. `{PackageId}@{semver}`.** The `@` character is legal in git refs but uncommon and visually similar to the npm scope syntax (`@scope/pkg`), which is needlessly confusing for NodeJS packages. The `/` separator also produces a clean grouping in `git tag --list` and most git UIs render it as a folder.
- **vs. `{PackageId}-v{semver}`.** Hyphens appear inside many real package IDs, making the parser ambiguous.

The `/v` infix is a strong, unique boundary. The tool parses tags by splitting on the last `/v` and validating that the right side is a strict `MAJOR.MINOR.PATCH` (no pre-release in v1).

### Disambiguating multiple tags at HEAD

`set-versions` collects every tag at the resolved ref, filters to the parseable shape, groups by `PackageId`, and rejects any group with more than one tag (a package having two versions at one commit is illegal). The per-tag work is parallelizable across packages but not across versions of the same package.

## Public-API-hash design

### .NET

The `MintPlayer.Verz.Targets` NuGet package adds an MSBuild task that runs `AfterTargets="Build"` and again `AfterTargets="GenerateNuspec"`. The task loads the just-built primary assembly via `MetadataLoadContext`, calls `PublicApiGenerator.ApiGenerator.GeneratePublicApi(assembly)`, computes SHA256 over the UTF-8 bytes of the resulting text, and writes the hash plus the framework-major into the nuspec as custom metadata:

```xml
<package>
  <metadata>
    <id>MintPlayer.Foo</id>
    <version>10.0.5</version>
    <repository type="git" url="https://github.com/..." commit="7a1c2e9" />
    <PublicApiHash>def0123...</PublicApiHash>
    <FrameworkMajor>10</FrameworkMajor>
  </metadata>
</package>
```

The targets package multitargets `netstandard2.0` so it loads in every MSBuild host, following the precedent set by the in-repo `MintPlayer.MSBuild.Tasks`. The repo opts in by adding a single `<PackageReference Include="MintPlayer.Verz.Targets" PrivateAssets="all" />` in `Directory.Build.props`.

### NodeJS

For TypeScript-emitting libraries: `tsc --declaration --emitDeclarationOnly --outDir .verz-types` produces a tree of `.d.ts` files. The hash input is the concatenation of every `.d.ts` file in lexical path order, each normalized by stripping trailing whitespace and collapsing runs of blank lines. The hash is SHA256 over the UTF-8 bytes. The result is written into the published `package.json`:

```json
{
  "name": "@mintplayer/foo",
  "version": "10.0.5",
  "publicApiHash": "def0123...",
  "frameworkMajor": 10
}
```

For pure-JavaScript libraries the SDK falls back to hashing the contents of every file declared in `files` and `main`/`module`/`exports`. The hash is necessarily coarser there, which the user accepts in exchange for not requiring TypeScript in every repo.

### Retrieval during `create-tag`

For the package whose next version is being computed, Verz iterates `Registries` in declared order. For each, it calls `IPackageRegistry.Lookup(packageId, priorVersion)`. The first non-null result wins. NuGet lookups read `PublicApiHash` and `FrameworkMajor` from the nuspec without downloading the `.nupkg`. npm lookups download the tarball (npm has no per-package metadata endpoint comparable to NuGet's), extract `package.json`, and read the same fields.

## CI integration

Two workflows. Auth tokens are provided by repository secrets and read by each tool's native credential mechanism, not by Verz directly.

### Workflow A: on-PR-merge — tag creation

Trigger: `push` to `master` whose head commit is a PR merge commit. Steps:

1. `actions/checkout@v4` with `fetch-depth: 0` so all tags and history are present.
2. `actions/setup-dotnet@v4` to install the .NET 10 SDK.
3. `actions/setup-node@v4` if the repo has NodeJS libraries.
4. `dotnet tool install -g MintPlayer.Verz`.
5. Configure `git` user via `git config user.email` / `user.name` to the bot identity that will own the tags.
6. `verz create-tag --push`. The `GITHUB_TOKEN` provided by GitHub Actions is used by `git push`; the default `actions/checkout` credential helper handles it.
7. The workflow ends. Any pushed tags trigger workflow B.

The plugin packages themselves are resolved from the registries listed in `verz.json`. If one of them is a private GitHub Packages feed, the workflow must first write a `~/.nuget/NuGet.config` containing a `<packageSourceCredentials>` entry that consumes `${{ secrets.GITHUB_TOKEN }}`. Verz invokes NuGet.Protocol, which respects that file natively.

### Workflow B: on-tag-push — publish

Trigger: `push` to `refs/tags/*/v*`. Steps:

1. `actions/checkout@v4` at the pushed ref, with `fetch-depth: 0`.
2. SDK setup as above.
3. `dotnet tool install -g MintPlayer.Verz`.
4. `verz set-versions`. This writes `<Version>` and `package.json#version` for every package whose tag is at HEAD.
5. `dotnet build -c Release` for the .NET solution. `npm ci && npm run build` for the NodeJS workspace, if present.
6. Write `~/.nuget/NuGet.config` with `NUGET_API_KEY` (for nuget.org) and `GITHUB_TOKEN` (for GitHub Packages) as `apikey` and password respectively. Write `~/.npmrc` with `NPM_TOKEN` if NodeJS publishing is enabled.
7. `verz publish`. Each plugin pushes its artifacts to each registry. Verz never sees the tokens; it only relies on the credential files being present.

A single workflow B run typically publishes only one package (the package named in the tag), but a multi-tag push at the same commit will publish every package mentioned in those tags.

## Open questions / risks

- **Pre-release tags.** Excluded from v1. The cleanest future extension is `{PackageId}/v{semver}-{label}.{n}`, with `verz create-tag --pre <label>` driving it. The parser already needs to be tightened to reject pre-release shapes today.
- **Circular package references.** Detected during graph construction in v1; `create-tag` refuses to run with exit `9` and lists the cycle. The maintainer must break the cycle in the source repo before releases can proceed. (Publishing into a cyclic dep graph is also broken at the registry level — neither NuGet nor npm can host two packages that each declare an exact dep on the other.)
- **Multiple artifacts per project.** A `.csproj` can produce a main `.nupkg`, a symbols package, and a content package. The .NET SDK plugin must enumerate all of them and tag each with the right kind; the host then routes to compatible registries. Open: should a symbols-push failure fail the whole publish, or be a warning? Recommendation: fail closed.
- **Unlisted or removed prior package.** A package can be unlisted on nuget.org (still queryable) or removed from GitHub Packages (gone). Recommendation: treat unlisted as present; treat removed as cold-start failure (exit 5). The maintainer's recourse is to publish a replacement at the missing version or to delete the orphan tag.
- **Plugin trust.** Plugins run in-process with full trust. Recommendation: `verz.json` should pin exact plugin versions (`MintPlayer.Verz.Sdks.Dotnet@1.4.2`), not floating ranges. v1 will treat unversioned entries as `latest stable` and emit a warning.

## Success metrics

- **Zero edits to `<Version>` or `package.json#version` in committed sources** across a 90-day window after adoption in `MintPlayer.Dotnet.Tools`.
- **Median time from PR-merge to package-live on nuget.org under five minutes** for a single-package release.
- **Adding NodeJS support to a repo requires only editing `verz.json`** — no new workflow file, no new scripts, no change to existing `.csproj` files.
- **No "what commit produced this package?" Slack questions** after adoption. The tag itself answers; the embedded `<repository commit="...">` confirms.

## Out of scope for v1

- Pre-release and build-metadata semver suffixes (`-beta.1`, `+sha.abc123`).
- Dependency-graph-aware build ordering across packages in the same repo.
- Publishing .NET global tools as a specialized artifact kind — Verz itself is a global tool, but it does not special-case the `<PackAsTool>true</PackAsTool>` workflow beyond what `dotnet pack` already produces.
- Docker image builds and registry pushes.
- Conventional-Commits or commit-message-driven version bumps.
- Auto-generating release notes or CHANGELOG entries.
- Yanking, unlisting, or deprecating prior published versions.
- Cross-repo coordination (a release in repo A that triggers a release in repo B).
