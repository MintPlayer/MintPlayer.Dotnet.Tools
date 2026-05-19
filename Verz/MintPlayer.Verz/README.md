# MintPlayer.Verz

A .NET global tool that derives library versions from git tags, stamps them into projects at build time, and publishes the resulting packages to NuGet, npm, and GitHub Packages from a single repo-local config.

Verz is plugin-based: each project type (.NET, NodeJS) is supported via an `IDevelopmentSdk` plugin; each publish destination via an `IPackageRegistry` plugin. Plugins are NuGet packages listed in a `verz.json` at the root of your repository and loaded dynamically at runtime.

For the full product specification, see [`docs/prd/verz/PRD-initial.md`](../../docs/prd/verz/PRD-initial.md).

## Install

Pick whichever fits your environment.

**Global install (.NET 8+):**

```bash
dotnet tool install -g MintPlayer.Verz
```

**One-shot via `dnx` (.NET 10+):** runs the latest version of the tool without persisting a global install. Useful in CI where the runner is ephemeral anyway:

```bash
dnx MintPlayer.Verz -- create-tag --push
```

`dnx` resolves the tool from `nuget.org` by default; to pull from a different feed, use a tool manifest (`.config/dotnet-tools.json`) and add the feed to your `NuGet.config`.

## Setup in your repository

### 1. Initialize `verz.json`

From the root of your repo:

```bash
verz init --stamp-placeholders
```

This creates a `verz.json` next to the command and, with `--stamp-placeholders`, replaces every detected library's version with a placeholder so committed sources no longer carry hardcoded version numbers. Verz only ever writes real versions in CI, never in checked-in files.

A minimal `verz.json` for a .NET-only monorepo:

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
    }
  ],
  "Plugins": [
    "MintPlayer.Verz.Sdks.Dotnet",
    "MintPlayer.Verz.Registries.NugetOrg",
    "MintPlayer.Verz.Registries.GithubPackageRegistry"
  ]
}
```

To add NodeJS support, append `MintPlayer.Verz.Sdks.NodeJS` and `MintPlayer.Verz.Registries.NpmJS` to `Plugins`, then add an npm registry entry to `Registries`:

```json
{
  "id": "npmjs",
  "kind": "npm",
  "url": "https://registry.npmjs.org/"
}
```

| Field | Description |
|---|---|
| `Registries` | Package feeds. Used both for plugin resolution and as publish destinations. |
| `Plugins` | NuGet package IDs of plugins (both SDK and registry kinds, in a single flat list). Verz inspects each loaded assembly and registers types implementing `IDevelopmentSdk` or `IPackageRegistry` automatically. Entries may be bare strings or `{ "id", "version" }` objects to pin a specific version. |

### 2. Enable the public-API-hash MSBuild task (.NET projects)

Add to your repo's `Directory.Build.props`:

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="MintPlayer.Verz.Targets" Version="1.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

This package adds two MSBuild targets that run on `Build` and `Pack`: it computes a SHA256 over the project's public API surface and stamps the hash plus the framework major into the produced `.nuspec`. Verz reads these back from prior published packages to decide patch-vs-minor bumps.

### 3. Add the two GitHub Actions workflows

Verz expects a two-workflow split: one creates tags after a PR merge, the other publishes when a tag is pushed.

#### `.github/workflows/release-tag.yml` (creates tags)

```yaml
name: Create release tags

on:
  push:
    branches: [master]

jobs:
  tag:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - name: Install Verz
        run: dotnet tool install -g MintPlayer.Verz
      - name: Configure git identity
        run: |
          git config user.email "ci-bot@example.com"
          git config user.name  "Release Bot"
      - name: Create and push tags
        run: verz create-tag --push
```

#### `.github/workflows/release-publish.yml` (publishes packages)

```yaml
name: Publish on tag

on:
  push:
    tags: ['*/v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - name: Install Verz
        run: dotnet tool install -g MintPlayer.Verz
      - name: Apply tag-derived versions
        run: verz set-versions
      - name: Build .NET
        run: dotnet build -c Release
      - name: Build NodeJS
        run: |
          npm ci
          npm run build
        if: hashFiles('package.json') != ''
      - name: Publish
        env:
          NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          NPM_TOKEN: ${{ secrets.NPM_TOKEN }}
        run: verz publish
```

### 4. Configure secrets

| Secret | Used by | Notes |
|---|---|---|
| `NUGET_API_KEY` | NuGet.org publishing | Generate at https://www.nuget.org/account/apikeys |
| `GITHUB_TOKEN` | GitHub Packages publishing | Provided automatically by GitHub Actions |
| `NPM_TOKEN` | npm publishing | Only required if NodeJS libraries are in scope |

Verz never reads these directly. It relies on each tool's native credential mechanism:

- **NuGet**: a `~/.nuget/NuGet.config` containing the API key. The publish workflow writes this before invoking `verz publish`.
- **npm**: `~/.npmrc` with `//registry.npmjs.org/:_authToken=${NPM_TOKEN}`.

A minimal `~/.nuget/NuGet.config` written in CI:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="github" value="https://nuget.pkg.github.com/mintplayer/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="USERNAME" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github>
  </packageSourceCredentials>
  <apikeys>
    <add key="https://api.nuget.org/v3/index.json" value="%NUGET_API_KEY%" />
  </apikeys>
</configuration>
```

## Subcommands

| Command | Purpose |
|---|---|
| `verz init` | Scaffold `verz.json` and (optionally) stamp version placeholders. |
| `verz set-versions` | Apply tag-derived versions at CI time. Run after checkout in the publish workflow. |
| `verz create-tag [--push]` | Compute next version(s), build the in-repo dependency graph, and create `{PackageId}/v{semver}` tags for affected packages. |
| `verz publish` | Pack and push every affected package to every configured registry. |

Add `--dry-run` to `set-versions` or `--package <id>` to `create-tag` for targeted runs.

## How version numbers are decided

For each library in your repo:

1. If the library's source tree hasn't changed since its prior tag, and no in-repo dependency bumped, **the library is skipped**. No new tag, no republish.
2. If the target framework's major version moved (e.g., `net9.0` → `net10.0`), bump **major**.
3. Otherwise, if the public-API surface changed (detected by SHA256 of the generated API text), bump **minor**.
4. Otherwise, bump **patch**.
5. If an in-repo dependency bumped, the consuming library bumps to at least that level (a major in a dep forces a major in the consumer).

Tags are formatted `{PackageId}/v{semver}`, e.g. `MintPlayer.Foo/v10.0.5`. One tag per package per release commit.

## Authoring a plugin

A new SDK or registry plugin is a NuGet package containing a single assembly that references `MintPlayer.Verz.Abstractions` and exports one or more concrete types implementing `IDevelopmentSdk` or `IPackageRegistry`. Publish it to any feed listed in a consuming repo's `verz.json` `Registries`. No `[Plugin]` attribute is required — Verz discovers types by reflection.

The Abstractions assembly multitargets `net8.0`, `net9.0`, and `net10.0`; choose the lowest TFM your plugin can support.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `E5: cold start` | A `{PackageId}/v{prev}` tag exists in git, but no configured registry hosts that version. Either republish the missing version or delete the orphan tag. |
| `E6: framework major decreased` | Target framework moved backwards (e.g., `net10.0` → `net9.0`). Semver does not support this; fix the project or remove the prior tag. |
| `E9: cycle in project graph` | Two libraries in the repo declare each other as dependencies. Break the cycle in source. |
| `No tags created (0 packages affected)` | Expected. The commit modified nothing that any library depends on. |
