# FolderHasher PRD

## Overview

The FolderHasher is a .NET library that computes a deterministic hash of a folder and its contents. The primary use case is to detect whether folder contents have changed, enabling conditional execution of expensive build steps.

### Primary Use Case: SPA Build Optimization

When used with [MintPlayer.AspNetCore.SpaServices](https://github.com/MintPlayer/MintPlayer.AspNetCore.SpaServices), the FolderHasher can determine if the `ClientApp` folder contents have changed. If unchanged, commands like `npm run build` or `npm run build:ssr` can be skipped entirely, allowing the application to start immediately without rebuilding frontend assets.

### Integration Strategy

The SPA build commands (`npm install`, `npm run build`) are triggered through MSBuild targets in [MintPlayer.AspNetCore.NodeServices](https://github.com/MintPlayer/MintPlayer.AspNetCore.SpaServices/blob/master/MintPlayer.AspNetCore.NodeServices/Targets/nodeservices.targets). This target-based approach should be preserved. Integration with FolderHasher will be done through the `MintPlayer.FolderHasher.MSBuild` package, which can be used to conditionally skip the npm build targets when folder contents haven't changed.

---

## Requirements

### Core Functionality

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Folder hashing** | Compute a deterministic SHA256 hash of all files in a folder | ✅ Implemented |
| **Custom algorithms** | Support custom hash algorithms (not just SHA256) | ✅ Implemented |
| **Async operations** | Use async I/O for performance | ✅ Implemented |
| **Parallel file reading** | Read multiple files in parallel | ✅ Implemented |
| **Deterministic output** | Same folder contents always produce the same hash | ✅ Implemented |
| **Dependency injection** | Integrate with Microsoft.Extensions.DependencyInjection | ✅ Implemented |
| **Stream large files** | Use streaming for files over 10MB to avoid memory issues | ✅ Implemented |

### Ignore File Support (.hasherignore)

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Glob patterns** | Use glob patterns (not regex) for matching | ✅ Implemented |
| **Comment support** | Lines starting with `#` are comments | ✅ Implemented |
| **Negation patterns** | Lines starting with `!` negate previous patterns | ✅ Implemented |
| **Directory patterns** | Patterns ending with `/` match directories | ✅ Implemented |
| **Recursive patterns** | Support `**` for recursive matching | ✅ Implemented |
| **Hierarchical ignore** | Process `.hasherignore` files in subdirectories | ✅ Implemented |
| **Exclude ignore files** | `.hasherignore` files not included in hash | ✅ Implemented |
| **Case-insensitive** | Case-insensitive matching on Windows | ✅ Implemented |
| **Inaccessible files** | Silently skip files/folders that cannot be accessed | ✅ Implemented |

### MSBuild Integration

| Requirement | Description | Status |
|-------------|-------------|--------|
| **MSBuild Task** | `ComputeFolderHashTask` for use in targets | ✅ Implemented |
| **FolderPath input** | Required input property for folder path | ✅ Implemented |
| **Hash output** | Output property containing computed hash | ✅ Implemented |
| **Auto-import targets** | `MintPlayer.FolderHasher.Targets` package | ✅ Implemented |
| **UsingTask registration** | Automatic task registration via .targets file | ✅ Implemented |

### Code Quality

| Requirement | Description | Status |
|-------------|-------------|--------|
| **No code duplication** | HasherIgnoreParser should not be duplicated | ✅ Implemented |
| **Unit tests** | Comprehensive unit test coverage | ❌ Missing |
| **Integration tests** | End-to-end testing with real folders | ❌ Missing |

### Documentation

| Requirement | Description | Status |
|-------------|-------------|--------|
| **README** | Package documentation with usage examples | ❌ Missing |
| **XML documentation** | IntelliSense documentation on public APIs | ❌ Missing |
| **SpaServices integration guide** | How to use with NodeServices targets | ❌ Missing |

---

## Architecture

### Projects

| Project | Version | Description |
|---------|---------|-------------|
| `MintPlayer.FolderHasher.Abstractions` | 10.0.0 | Interface definitions (`IFolderHasher`) |
| `MintPlayer.FolderHasher` | 10.2.0 | Core implementation |
| `MintPlayer.FolderHasher.MSBuild` | 10.1.0 | MSBuild task implementation |
| `MintPlayer.FolderHasher.Targets` | 10.1.0 | Auto-import .targets package |
| `MintPlayer.FolderHasher.Test` | - | Demo/test console application |

### Hash Algorithm

The hashing algorithm processes files in a deterministic order:

1. Discover all files recursively in the folder
2. Parse all `.hasherignore` files in the directory tree
3. Filter out files matching ignore patterns
4. Skip any inaccessible files/folders silently
5. Sort remaining files by relative path (ensures determinism)
6. For each file:
   - Hash the relative path (lowercase, UTF-8 encoded)
   - For large files (>10MB): stream the content in chunks
   - For small files: hash the file contents directly
7. Return final hash as lowercase hexadecimal string

---

## API Reference

### IFolderHasher Interface

```csharp
public interface IFolderHasher
{
    Task<string> GetFolderHashAsync(string folder);
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders);
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm);
}
```

### Dependency Injection

```csharp
services.AddFolderHasher();
```

---

## Usage Examples

### C# Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher;

var services = new ServiceCollection();
services.AddFolderHasher();
var provider = services.BuildServiceProvider();

var hasher = provider.GetRequiredService<IFolderHasher>();
var hash = await hasher.GetFolderHashAsync(@"C:\MyProject\ClientApp");
Console.WriteLine($"Folder hash: {hash}");
```

### MSBuild Usage

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MintPlayer.FolderHasher.Targets" Version="10.0.0" />
  </ItemGroup>

  <!-- Compute hash before build -->
  <Target Name="ComputeClientAppHash" BeforeTargets="Build">
    <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)\ClientApp">
      <Output TaskParameter="Hash" PropertyName="ClientAppHash" />
    </ComputeFolderHashTask>
    <Message Text="ClientApp hash: $(ClientAppHash)" Importance="high" />
  </Target>

  <!-- Only run npm build if changed -->
  <Target Name="BuildClientApp"
          AfterTargets="ComputeClientAppHash"
          Inputs="$(ClientAppHash)"
          Outputs="$(IntermediateOutputPath)clientapp.hash">

    <Exec Command="npm run build" WorkingDirectory="$(MSBuildProjectDirectory)\ClientApp" />

    <WriteLinesToFile File="$(IntermediateOutputPath)clientapp.hash"
                      Lines="$(ClientAppHash)"
                      Overwrite="true" />
  </Target>

</Project>
```

### Example .hasherignore File

```gitignore
# Dependencies
node_modules/

# Build outputs
dist/
build/
.angular/

# IDE/Editor
.vscode/
.idea/
*.swp

# Logs
*.log
npm-debug.log*

# Environment files
.env
.env.local

# Keep important files despite earlier rules
!.env.example
```

---

## SpaServices Integration

The MintPlayer.AspNetCore.NodeServices package includes MSBuild targets that automatically run npm commands:

- **DebugEnsureNodeEnv**: Runs before Build (Debug only) - executes `npm install`
- **PublishRunWebpack**: Runs after ComputeFilesToPublish - executes `npm install` and `npm run build` (or SSR build)

### Integration with FolderHasher

To skip npm builds when the ClientApp folder hasn't changed, modify the nodeservices.targets to use FolderHasher:

```xml
<!-- Add to nodeservices.targets -->

<!-- Compute ClientApp hash before checking if build is needed -->
<Target Name="ComputeSpaFolderHash"
        BeforeTargets="DebugEnsureNodeEnv;PublishRunWebpack"
        Condition="'$(EnableSpaBuilder)' == 'true'">

  <ComputeFolderHashTask FolderPath="$(SpaRoot)">
    <Output TaskParameter="Hash" PropertyName="SpaFolderHash" />
  </ComputeFolderHashTask>

  <!-- Read previous hash if it exists -->
  <ReadLinesFromFile File="$(IntermediateOutputPath)spa.hash"
                     Condition="Exists('$(IntermediateOutputPath)spa.hash')">
    <Output TaskParameter="Lines" PropertyName="PreviousSpaHash" />
  </ReadLinesFromFile>

  <!-- Determine if SPA needs rebuild -->
  <PropertyGroup>
    <SpaFolderChanged Condition="'$(SpaFolderHash)' != '$(PreviousSpaHash)'">true</SpaFolderChanged>
    <SpaFolderChanged Condition="'$(SpaFolderHash)' == '$(PreviousSpaHash)'">false</SpaFolderChanged>
  </PropertyGroup>

  <Message Text="SPA folder hash: $(SpaFolderHash)" Importance="high" />
  <Message Text="SPA folder changed: $(SpaFolderChanged)" Importance="high" />
</Target>

<!-- Update hash file after successful build -->
<Target Name="SaveSpaFolderHash"
        AfterTargets="DebugEnsureNodeEnv;PublishRunWebpack"
        Condition="'$(EnableSpaBuilder)' == 'true' AND '$(SpaFolderChanged)' == 'true'">

  <WriteLinesToFile File="$(IntermediateOutputPath)spa.hash"
                    Lines="$(SpaFolderHash)"
                    Overwrite="true" />
</Target>

<!-- Modify existing targets to check SpaFolderChanged -->
<Target Name="DebugEnsureNodeEnv"
        BeforeTargets="Build"
        Condition="'$(EnableSpaBuilder)' == 'true' AND '$(Configuration)' == 'Debug' AND '$(SpaFolderChanged)' == 'true'">
  <!-- existing implementation -->
</Target>

<Target Name="PublishRunWebpack"
        AfterTargets="ComputeFilesToPublish"
        Condition="'$(EnableSpaBuilder)' == 'true' AND '$(SpaFolderChanged)' == 'true'">
  <!-- existing implementation -->
</Target>
```

### Recommended .hasherignore for SPA Projects

Place this file in your ClientApp folder:

```gitignore
# Dependencies (restored by npm install)
node_modules/

# Build outputs (generated by npm run build)
dist/
dist-server/
build/
.angular/
.next/
.nuxt/

# Cache directories
.cache/
.parcel-cache/

# IDE/Editor files
.vscode/
.idea/
*.swp
*.swo

# Logs
*.log
npm-debug.log*
yarn-debug.log*
yarn-error.log*

# OS files
.DS_Store
Thumbs.db

# Environment (may contain secrets)
.env.local
.env.*.local
```

---

## Gap Analysis

### What's Implemented ✅

1. **Core hashing engine** - Fully functional with async/parallel processing
2. **`.hasherignore` support** - Complete with glob patterns, negation, comments, hierarchical processing
3. **MSBuild integration** - Working task and targets packages
4. **DI integration** - ServiceCollection extension methods
5. **Multiple hash algorithms** - Configurable algorithm support

### What's Missing ❌

1. **Unit tests** - No xUnit/NUnit/MSTest project exists
2. **Integration tests** - No automated end-to-end tests
3. **XML documentation** - Public APIs lack IntelliSense docs
4. **README files** - No package-level documentation

---

## Implementation Plan

### Phase 1: Code Quality (High Priority) ✅ COMPLETED

1. ~~**Deduplicate HasherIgnoreParser**~~ ✅
   - Made `HasherIgnoreParser` public in `MintPlayer.FolderHasher`
   - MSBuild task now references the main library instead of duplicating code
   - Targets package updated to pack all required DLLs

2. ~~**Add large file streaming**~~ ✅
   - Threshold set to 10MB
   - Files below threshold: read into memory
   - Files above threshold: streamed in 80KB chunks using async `FileStream`

3. ~~**Handle inaccessible files**~~ ✅
   - File/directory access wrapped in try-catch
   - Inaccessible files/directories are silently skipped
   - Hashing continues with remaining accessible files

### Phase 2: Testing (High Priority)

4. **Create unit test project** (`MintPlayer.FolderHasher.Tests`)
   - Test `HasherIgnoreParser` pattern matching
   - Test hash determinism (same input = same output)
   - Test edge cases: empty folders, special characters, nested .hasherignore files
   - Test large file streaming
   - Test inaccessible file handling

### Phase 3: Documentation (Medium Priority)

5. **Add README.md files** to each project
6. **Add XML documentation** to public APIs
7. **Document SpaServices integration** in main README

---

## Version History

| Version | Changes |
|---------|---------|
| 10.2.0 | MintPlayer.FolderHasher: Added large file streaming (>10MB), inaccessible file handling, made `HasherIgnoreParser` public |
| 10.1.0 | MintPlayer.FolderHasher.MSBuild/Targets: Refactored to use shared `HasherIgnoreParser`, added large file streaming and inaccessible file handling |
| 10.1.0 | MintPlayer.FolderHasher: Previous version |
| 10.0.0 | Initial release of all packages |
