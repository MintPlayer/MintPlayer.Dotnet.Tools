# MintPlayer.FolderHasher.Targets

MSBuild targets package for computing folder hashes. Just install this package to use the `ComputeFolderHashTask` in your build process - no additional configuration required.

## Installation

```bash
dotnet add package MintPlayer.FolderHasher.Targets
```

## Usage

After installing, the `ComputeFolderHashTask` is automatically available in your `.csproj` files:

```xml
<Target Name="ComputeClientAppHash" BeforeTargets="Build">
  <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)/ClientApp">
    <Output TaskParameter="Hash" PropertyName="ClientAppHash" />
  </ComputeFolderHashTask>
  <Message Text="Client app hash: $(ClientAppHash)" Importance="high" />
</Target>
```

## Common Use Cases

### Skip npm install when dependencies haven't changed

```xml
<Target Name="NpmInstall" BeforeTargets="Build">
  <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)/ClientApp">
    <Output TaskParameter="Hash" PropertyName="CurrentHash" />
  </ComputeFolderHashTask>

  <ReadLinesFromFile File="$(IntermediateOutputPath)npm-hash.txt"
                     Condition="Exists('$(IntermediateOutputPath)npm-hash.txt')">
    <Output TaskParameter="Lines" PropertyName="CachedHash" />
  </ReadLinesFromFile>

  <Exec Command="npm install" WorkingDirectory="$(MSBuildProjectDirectory)/ClientApp"
        Condition="'$(CurrentHash)' != '$(CachedHash)'" />

  <WriteLinesToFile File="$(IntermediateOutputPath)npm-hash.txt"
                    Lines="$(CurrentHash)" Overwrite="true" />
</Target>
```

### Skip frontend build when source hasn't changed

```xml
<Target Name="BuildFrontend" BeforeTargets="Build">
  <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)/ClientApp/src">
    <Output TaskParameter="Hash" PropertyName="FrontendSourceHash" />
  </ComputeFolderHashTask>

  <ReadLinesFromFile File="$(IntermediateOutputPath)frontend-hash.txt"
                     Condition="Exists('$(IntermediateOutputPath)frontend-hash.txt')">
    <Output TaskParameter="Lines" PropertyName="CachedFrontendHash" />
  </ReadLinesFromFile>

  <Exec Command="npm run build" WorkingDirectory="$(MSBuildProjectDirectory)/ClientApp"
        Condition="'$(FrontendSourceHash)' != '$(CachedFrontendHash)'" />

  <WriteLinesToFile File="$(IntermediateOutputPath)frontend-hash.txt"
                    Lines="$(FrontendSourceHash)" Overwrite="true" />
</Target>
```

### Using .hasherignore

Create a `.hasherignore` file in the folder being hashed to exclude files:

```gitignore
# Exclude build outputs
dist/
*.js.map

# Exclude test files
**/*.spec.ts
**/*.test.ts

# Exclude IDE files
.idea/
.vscode/
```

## Task Properties

### Input

| Property | Required | Description |
|----------|----------|-------------|
| `FolderPath` | Yes | Absolute path to the folder to hash |

### Output

| Property | Description |
|----------|-------------|
| `Hash` | 64-character lowercase hex string (SHA256) |

## Features

- **Zero configuration** - Just install and use
- **`.hasherignore` support** - Exclude files with gitignore-style patterns
- **Large file streaming** - Memory-efficient hashing of large files (>10MB)
- **Graceful error handling** - Skips inaccessible files/directories
- **Deterministic** - Same contents always produce the same hash

## Package Contents

This package is a development dependency that includes:
- MSBuild targets for automatic task registration
- `MintPlayer.FolderHasher.MSBuild.dll` - The MSBuild task
- Required runtime dependencies

## Related Packages

- [MintPlayer.FolderHasher](../MintPlayer.FolderHasher/README.md) - Runtime library for use with dependency injection
- [MintPlayer.FolderHasher.MSBuild](../MintPlayer.FolderHasher.MSBuild/README.md) - MSBuild task (included in this package)

## License

Apache-2.0
