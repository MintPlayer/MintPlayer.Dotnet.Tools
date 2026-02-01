# MintPlayer.FolderHasher.MSBuild

MSBuild task for computing folder hashes during the build process. Use this in `.csproj` files to conditionally run build steps based on folder content changes.

## Installation

For most users, install the [MintPlayer.FolderHasher.Targets](../MintPlayer.FolderHasher.Targets/README.md) package instead, which includes this package and automatically configures the MSBuild task.

```bash
dotnet add package MintPlayer.FolderHasher.Targets
```

## Usage

After installing the Targets package, use the `ComputeFolderHashTask` in your `.csproj`:

```xml
<Target Name="ComputeClientAppHash" BeforeTargets="Build">
  <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)/ClientApp">
    <Output TaskParameter="Hash" PropertyName="ClientAppHash" />
  </ComputeFolderHashTask>
  <Message Text="Client app hash: $(ClientAppHash)" Importance="high" />
</Target>
```

### Conditional Build Example

Skip npm install if `node_modules` already matches `package-lock.json`:

```xml
<Target Name="NpmInstall" BeforeTargets="Build">
  <!-- Compute hash of package files -->
  <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)/ClientApp">
    <Output TaskParameter="Hash" PropertyName="CurrentPackageHash" />
  </ComputeFolderHashTask>

  <!-- Read cached hash if it exists -->
  <ReadLinesFromFile File="$(IntermediateOutputPath)npm-hash.txt" Condition="Exists('$(IntermediateOutputPath)npm-hash.txt')">
    <Output TaskParameter="Lines" PropertyName="CachedPackageHash" />
  </ReadLinesFromFile>

  <!-- Run npm install only if hash changed -->
  <Exec Command="npm install" WorkingDirectory="$(MSBuildProjectDirectory)/ClientApp"
        Condition="'$(CurrentPackageHash)' != '$(CachedPackageHash)'" />

  <!-- Save the new hash -->
  <WriteLinesToFile File="$(IntermediateOutputPath)npm-hash.txt" Lines="$(CurrentPackageHash)" Overwrite="true" />
</Target>
```

## Task Properties

### Input Properties

| Property | Required | Description |
|----------|----------|-------------|
| `FolderPath` | Yes | The absolute path to the folder to hash |

### Output Properties

| Property | Description |
|----------|-------------|
| `Hash` | The computed SHA256 hash (64-character lowercase hex string) |

## Features

- **`.hasherignore` support** - Exclude files using gitignore-style patterns
- **Large file streaming** - Files over 10MB are streamed to minimize memory usage
- **Graceful error handling** - Inaccessible files/directories are silently skipped
- **Deterministic hashing** - Same contents always produce the same hash

## Related Packages

- [MintPlayer.FolderHasher.Targets](../MintPlayer.FolderHasher.Targets/README.md) - Easy installation with automatic MSBuild integration
- [MintPlayer.FolderHasher](../MintPlayer.FolderHasher/README.md) - Runtime library for dependency injection

## License

Apache-2.0
