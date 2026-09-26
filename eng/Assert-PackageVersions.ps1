<#
.SYNOPSIS
    Fails when a packed assembly's version doesn't match the version of the package that ships it.

.DESCRIPTION
    The SourceGenerators releases 10.20.2-12.0.1 and MintPlayer.Assertions 1.0.1-11.0.0-rc.4 shipped
    assemblies versioned 99.9.9.0 (#187; the per-version list is in
    docs/PRD-ValueComparerGenerator-DownstreamFindings.md, F4): the packaging
    tests packed the repository's own projects in place with -p:Version=99.9.9-packtest, and CI's
    `dotnet pack --no-build` after the test run packed those rebuilt dlls under the real package
    version. Nothing checked what was inside the package.

    Two rules, over every *.nupkg under -Path:
      1. The assembly named after the package (lib/, analyzers/ or tools/) has AssemblyVersion
         Major.Minor.Patch.0 and a FileVersion starting with Major.Minor.Patch of the package version.
      2. No assembly in any package has AssemblyVersion 99.9.9.0, the packaging tests' version.

    Assemblies bundled from other packages (Newtonsoft.Json, MintPlayer.SourceGenerators.Tools inside a
    generator) keep their own versions, so rule 1 only looks at the package's own assembly; those
    packages are checked when their own package is.

.EXAMPLE
    pwsh eng/Assert-PackageVersions.ps1 -Path .
#>
param(
    [string] $Path = '.'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$testVersion = [version] '99.9.9.0'
$packages = Get-ChildItem -Path $Path -Recurse -Filter *.nupkg -File

if (-not $packages) {
    Write-Error "No .nupkg found under '$Path'; nothing was checked."
}

$failures = [System.Collections.Generic.List[string]]::new()
$checked = 0
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("pkgversions-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

try {
    foreach ($package in $packages) {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' } | Select-Object -First 1
            $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }

            $id = $nuspec.package.metadata.id
            $packageVersion = $nuspec.package.metadata.version
            $numeric = [version] ($packageVersion -split '[-+]')[0]
            $expected = [version]::new($numeric.Major, $numeric.Minor, [math]::Max($numeric.Build, 0), 0)

            $dlls = $zip.Entries | Where-Object { $_.FullName -match '^(lib|analyzers|tools)/.*\.dll$' }
            foreach ($entry in $dlls) {
                $file = Join-Path $scratch ([guid]::NewGuid().ToString('N') + '.dll')
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file)

                $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($file).Version
                $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileVersion
                $where = "$($package.Name)!$($entry.FullName)"

                if ($assemblyVersion -eq $testVersion) {
                    $failures.Add("$where has AssemblyVersion $assemblyVersion, the packaging tests' version.")
                }

                if ([System.IO.Path]::GetFileNameWithoutExtension($entry.Name) -eq $id) {
                    $checked++
                    if ($assemblyVersion -ne $expected) {
                        $failures.Add("$where has AssemblyVersion $assemblyVersion; package $id $packageVersion expects $expected.")
                    }
                    if (-not "$fileVersion".StartsWith("$($expected.Major).$($expected.Minor).$($expected.Build)")) {
                        $failures.Add("$where has FileVersion $fileVersion; package $id $packageVersion expects $($expected.Major).$($expected.Minor).$($expected.Build).x.")
                    }
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
}
finally {
    Remove-Item -Recurse -Force $scratch
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "::error::$_" }
    Write-Error "$($failures.Count) packed assembly version(s) don't match their package."
}

Write-Host "Checked $($packages.Count) package(s), $checked own assembly/assemblies: every version matches."
