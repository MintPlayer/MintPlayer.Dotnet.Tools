# MintPlayer.TokenReplacer.Targets

Generic MSBuild token replacement. Declare which files contain `$token$` placeholders and where
the values come from (literals, MSBuild properties, resolved NuGet package versions); at build
time the tokens are replaced and the result written to an output file. Ships `buildTransitive`
targets, so package authors can use it to stamp **their own package version** into assets they
ship — with zero configuration for their consumers.

## Installation

```bash
dotnet add package MintPlayer.TokenReplacer.Targets
```

## Usage in a project

```xml
<ItemGroup>
  <PackageReference Include="MintPlayer.TokenReplacer.Targets" Version="1.0.0" />
</ItemGroup>

<ItemGroup>
  <!-- Token values: literals or MSBuild properties -->
  <TokenReplaceValue Include="version" Value="$(Version)" />
  <TokenReplaceValue Include="buildConfig" Value="$(Configuration)" />

  <!-- Token value from the RESOLVED version of a referenced NuGet package -->
  <TokenReplacePackageVersion Include="Some.Cdn.Library" TokenName="cdnVersion" />

  <!-- Files to process -->
  <TokenReplaceFile Include="wwwroot\index.template.html"
                    OutputFile="$(IntermediateOutputPath)tokenreplacer\index.html"
                    IncludeAs="Content" CopyToOutputDirectory="PreserveNewest" Link="wwwroot\index.html" />
</ItemGroup>
```

`index.template.html` containing

```html
<script src="https://cdn.example.com/lib@$cdnVersion$/loader.js"></script>
<!-- built $buildConfig$ v$version$ -->
```

is materialized with the actual resolved version of `Some.Cdn.Library`, the build configuration
and your project version filled in.

## Usage by package authors (stamp your own package version)

Reference this package **without** `PrivateAssets="all"` (it must flow to your consumers), ship
your asset template, and add a thin `buildTransitive/<YourPackage>.targets`:

```xml
<Project>
	<PropertyGroup>
		<!-- <packagesRoot>/<your.package.id>/<version>/buildTransitive/ → "<version>" -->
		<_MyPackageVersion>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory)))))))</_MyPackageVersion>
	</PropertyGroup>
	<ItemGroup>
		<TokenReplaceValue Include="version" Value="$(_MyPackageVersion)" />
		<TokenReplaceFile Include="$(MSBuildThisFileDirectory)..\content\web-loader.template.js"
		                  OutputFile="$(IntermediateOutputPath)tokenreplacer\web-loader.js"
		                  IncludeAs="Content" CopyToOutputDirectory="PreserveNewest" Link="web-loader.js" />
	</ItemGroup>
</Project>
```

Every project that references your package then automatically gets `web-loader.js` with
`$version$` replaced by the exact version of your package that NuGet restored.

## Reference

### Items

| Item | Meaning |
|------|---------|
| `TokenReplaceFile` | A file to process. Metadata: `OutputFile` (target path; defaults to `$(IntermediateOutputPath)tokenreplacer\<name>`), `IncludeAs` (`Content`/`None` — add the output to the build; combine with `CopyToOutputDirectory`/`Link`) |
| `TokenReplaceValue` | A token. Item spec = token name, `Value` metadata = replacement |
| `TokenReplacePackageVersion` | Registers a token whose value is the resolved version of a package (from `project.assets.json`). Item spec = package id, optional `TokenName` metadata (defaults to the package id) |

### Properties

| Property | Default | Meaning |
|----------|---------|---------|
| `TokenReplacerTokenStart` / `TokenReplacerTokenEnd` | `$` / `$` | Token delimiters |
| `TokenReplacerMissingTokenPolicy` | `Warn` | `Warn`, `Error` or `Ignore` for tokens without a configured value |
| `MintPlayerReplaceTokensBeforeTargets` | `AssignTargetPaths` | Hook point of the replacement target |
| `TokenReplacerOwnVersion` | derived from package path | The resolved version of this package; preset it for ProjectReference/source scenarios |
| `TokenReplacerTasksAssembly` | dll inside this package | Override the task assembly location (testing/local development) |

### Behavior

- **Write-if-changed**: outputs are only rewritten when content actually changed.
- **Incremental**: the replacement target is skipped when outputs are newer than the templates/project files.
- **Clean-aware**: generated files are registered as `FileWrites` and removed by `dotnet clean` (note: MSBuild only tracks files under `bin`/`obj` for Clean; outputs elsewhere are left alone).
- **UTF-8** with BOM preservation (a template with a BOM produces output with a BOM).
- Token names match `[A-Za-z0-9_.-]+` and are case-insensitive; `$(Property)` constructs are never treated as tokens.

## License

Apache-2.0
