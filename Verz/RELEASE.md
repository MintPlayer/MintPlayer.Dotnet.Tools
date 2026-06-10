# Releasing Verz

Verz is the tool defined in this folder, and it also manages its own
release process — i.e. the next version of Verz is decided by Verz
itself. This document explains the one-time bootstrap and the steady-
state loop.

## Steady state — what happens on every merge

1. A PR touching anything under `Verz/**` is merged to `master`.
2. [`verz-create-tag.yml`](../.github/workflows/verz-create-tag.yml)
   runs:
   - Installs Verz from `nuget.org` as a global tool.
   - Builds Verz in `Release`.
   - Runs `verz create-tag --push` from `Verz/`. For every Verz
     package whose source tree changed since its prior tag (or whose
     in-repo deps moved), Verz computes the next version and pushes
     a `{PackageId}/v{semver}` tag.
3. Each pushed tag triggers
   [`verz-publish.yml`](../.github/workflows/verz-publish.yml):
   - Installs Verz again.
   - Runs `verz set-versions` from `Verz/`, which stamps the tag's
     version into the matching `.csproj`.
   - Writes a `~/.nuget/NuGet.config` populated from the
     `PUBLISH_TO_NUGET_ORG` repo secret and the workflow's
     auto-provisioned `GITHUB_TOKEN`.
   - Runs `verz publish`, which packs every tagged package (injecting
     `<PublicApiHash>` + `<FrameworkMajor>` into each nuspec) and
     pushes to both `nuget.org` and `nuget.pkg.github.com/mintplayer`.

No human edits any `<Version>` field along the way. The csproj files
carry `<Version>0.0.0-placeholder</Version>` in committed source; real
versions exist only in git tags and in published nuspecs.

## The bootstrap problem

Both workflows install Verz from `nuget.org` (`dotnet tool install -g
MintPlayer.Verz`). Until Verz exists on `nuget.org` at all, neither
workflow can run.

The first release has to happen by hand:

1. From this directory, manually pack each Verz package:
   ```
   dotnet pack MintPlayer.Verz.Abstractions/MintPlayer.Verz.Abstractions.csproj -c Release
   dotnet pack Sdks/MintPlayer.Verz.Sdks.Dotnet/MintPlayer.Verz.Sdks.Dotnet.csproj -c Release
   dotnet pack Sdks/MintPlayer.Verz.Sdks.NodeJS/MintPlayer.Verz.Sdks.NodeJS.csproj -c Release
   dotnet pack Registries/MintPlayer.Verz.Registries.NugetOrg/MintPlayer.Verz.Registries.NugetOrg.csproj -c Release
   dotnet pack Registries/MintPlayer.Verz.Registries.NpmJS/MintPlayer.Verz.Registries.NpmJS.csproj -c Release
   dotnet pack Registries/MintPlayer.Verz.Registries.GithubPackageRegistry/MintPlayer.Verz.Registries.GithubPackageRegistry.csproj -c Release
   dotnet pack MintPlayer.Verz/MintPlayer.Verz.csproj -c Release
   ```

   This produces seven `0.0.0-placeholder` nupkgs. Either edit each
   csproj to set `<Version>1.0.0</Version>` first, or pass
   `-p:Version=1.0.0` to each `dotnet pack` invocation.

2. Push each to `nuget.org`:
   ```
   dotnet nuget push <nupkg> --source https://api.nuget.org/v3/index.json --api-key <key>
   ```

3. Create matching git tags at the bootstrap commit:
   ```
   git tag MintPlayer.Verz/v1.0.0
   git tag MintPlayer.Verz.Abstractions/v1.0.0
   git tag MintPlayer.Verz.Sdks.Dotnet/v1.0.0
   git tag MintPlayer.Verz.Sdks.NodeJS/v1.0.0
   git tag MintPlayer.Verz.Registries.NugetOrg/v1.0.0
   git tag MintPlayer.Verz.Registries.NpmJS/v1.0.0
   git tag MintPlayer.Verz.Registries.GithubPackageRegistry/v1.0.0
   git push --tags
   ```

4. Set repository secret `PUBLISH_TO_NUGET_ORG` (existing) to a
   `nuget.org` API key with push permissions for the
   `MintPlayer.Verz*` packages.

From the next merge onwards, the two workflows take over. The first
post-bootstrap PR that touches `Verz/**` will get patch bumps across
all libs (their git-diff-vs-prior-tag is non-empty); subsequent
unchanged libs will be skipped per the affected-set rule.

## Cutting over from the legacy workflow

The existing [`dotnet-build-master.yml`](../.github/workflows/dotnet-build-master.yml)
pushes everything in the repo to both `nuget.org` and GitHub Packages
on every master push. It's path-agnostic — it'll keep doing what it
does for the non-Verz libraries.

When you're ready to cut Verz over, either:

- Add `paths-ignore: ['Verz/**']` to the legacy workflow's `on.push`
  trigger so it stops touching Verz packages (Verz handles them now).
- Or delete the legacy workflow entirely once every package in the
  repo is Verz-managed.

The two Verz workflows in this commit are scoped to `Verz/**` so
there's no overlap until you explicitly cut over.
