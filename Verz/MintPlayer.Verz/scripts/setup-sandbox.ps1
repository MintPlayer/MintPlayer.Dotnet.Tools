#requires -Version 7
<#
.SYNOPSIS
  Prepares the Verz sandbox under Verz/MintPlayer.Verz/sandbox/ so the
  launch profiles in Properties/launchSettings.json work in Visual Studio.

.DESCRIPTION
  Run this once (and after `git clean` / fresh checkout):

    pwsh ./scripts/setup-sandbox.ps1

  It creates:
    sandbox/.feed/                       — local NuGet feed
    sandbox/.feed/MintPlayer.Verz.Sdks.Dotnet.*.nupkg
    sandbox/fixture-set-versions/        — a one-package git repo
        Foo.csproj                       — <Version>0.0.0-placeholder</Version>
        verz.json                        — Plugins pinned to .feed
        .git/                            — initial commit + tag Foo/v1.2.3

  After running this, the "verz set-versions" launch profile in VS will
  load the SDK plugin from the local feed, parse the Foo/v1.2.3 tag at
  HEAD, and stamp 1.2.3 into Foo.csproj.

  The init profiles do not need any setup — they create their own
  verz.json under sandbox/init*. Delete those subdirs between runs if
  you want to re-test "first invocation" behavior.

.NOTES
  Idempotent: re-running wipes the fixture dir and re-creates it.
#>

$ErrorActionPreference = 'Stop'

$here       = $PSScriptRoot
$projectDir = (Resolve-Path "$here/..").Path
$repoRoot   = (Resolve-Path "$projectDir/../..").Path
$sandbox    = Join-Path $projectDir 'sandbox'
$feed       = Join-Path $sandbox '.feed'
$fixture    = Join-Path $sandbox 'fixture-set-versions'
$sdkProject = Join-Path $repoRoot 'Verz/Sdks/MintPlayer.Verz.Sdks.Dotnet/MintPlayer.Verz.Sdks.Dotnet.csproj'

Write-Host "Project dir : $projectDir"
Write-Host "Sandbox     : $sandbox"

# 1. Pack the SDK plugin into the local feed.
Write-Host "`n[1/3] Packing MintPlayer.Verz.Sdks.Dotnet -> $feed"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
& dotnet pack $sdkProject -c Release -o $feed --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed (exit $LASTEXITCODE)" }

# 2. Rebuild the fixture dir from scratch.
Write-Host "`n[2/3] Resetting fixture at $fixture"
if (Test-Path $fixture) {
    Remove-Item -Recurse -Force $fixture
}
New-Item -ItemType Directory -Force -Path $fixture | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Foo</PackageId>
    <Version>0.0.0-placeholder</Version>
  </PropertyGroup>
</Project>
"@ | Set-Content (Join-Path $fixture 'Foo.csproj')

$feedAbs   = (Resolve-Path $feed).Path
$feedJson  = $feedAbs -replace '\\', '\\'
@"
{
  "`$schema": "https://mintplayer.com/verz/v1/schema.json",
  "Registries": [
    { "id": "local", "kind": "nuget", "url": "$feedJson" }
  ],
  "Plugins": [
    { "id": "MintPlayer.Verz.Sdks.Dotnet", "version": "0.0.0-placeholder" }
  ]
}
"@ | Set-Content (Join-Path $fixture 'verz.json')

# 3. git init + initial commit + tag.
Write-Host "`n[3/3] Initialising git repo + Foo/v1.2.3 tag"
Push-Location $fixture
try {
    & git init -q -b main
    & git config user.email 'verz-sandbox@local'
    & git config user.name  'Verz Sandbox'
    & git add Foo.csproj verz.json
    & git commit -qm 'initial fixture'
    & git tag 'Foo/v1.2.3'
} finally {
    Pop-Location
}

Write-Host "`nReady. In Visual Studio, pick a 'verz ...' launch profile and F5."
