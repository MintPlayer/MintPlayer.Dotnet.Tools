# MintPlayer.FolderHasher

A .NET library for computing deterministic hash values of folder contents, with support for `.hasherignore` files (similar to `.gitignore`), large file streaming, and graceful error handling.

## Installation

```bash
dotnet add package MintPlayer.FolderHasher
```

## Usage

### Basic Setup with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher;
using MintPlayer.FolderHasher.Abstractions;

var services = new ServiceCollection();
services.AddFolderHasher();
var provider = services.BuildServiceProvider();

var hasher = provider.GetRequiredService<IFolderHasher>();
```

### Computing a Folder Hash

```csharp
// Simple usage - uses SHA256 by default
var hash = await hasher.GetFolderHashAsync(@"C:\MyProject");

// With folder exclusions (regex-based)
var hash = await hasher.GetFolderHashAsync(@"C:\MyProject", ["node_modules", "bin", "obj"]);

// With custom hash algorithm
using var sha512 = SHA512.Create();
var hash = await hasher.GetFolderHashAsync(@"C:\MyProject", [], sha512);
```

## Features

### .hasherignore Support

Create a `.hasherignore` file in any directory to exclude files from hashing. The syntax is similar to `.gitignore`:

```gitignore
# Comments start with #
*.log
*.tmp

# Directory patterns (matches at root level)
node_modules/
dist/

# Match anywhere in the tree
**/temp/

# Negation patterns
!important.log

# Specific paths
build/*.js
```

**Pattern behavior:**
- `*.log` - Matches `*.log` files in any directory
- `node_modules/` - Matches the `node_modules` directory at the root level
- `**/node_modules/` - Matches `node_modules` anywhere in the directory tree
- `/build` - Matches only at the root (leading slash)
- `!important.log` - Negates previous patterns (keeps the file)

### Nested .hasherignore Files

Each subdirectory can have its own `.hasherignore` file. Patterns in nested files apply to files within that subdirectory and its descendants.

### Large File Streaming

Files larger than 10MB are automatically streamed in 80KB chunks to minimize memory usage. This allows hashing folders containing very large files without loading them entirely into memory.

### Graceful Error Handling

The hasher gracefully handles:
- **Inaccessible directories** - Silently skipped
- **Inaccessible files** - Silently skipped
- **Permission errors** - Silently skipped

This allows hashing system folders or folders with mixed permissions without throwing exceptions.

### Deterministic Hashing

The hash is computed deterministically based on:
1. File paths (sorted alphabetically, case-insensitive)
2. File contents

The same folder contents will always produce the same hash, making it suitable for:
- Cache invalidation
- Change detection
- Content verification

## MSBuild Integration

For build-time folder hashing (e.g., cache invalidation in CI/CD), see [MintPlayer.FolderHasher.Targets](../MintPlayer.FolderHasher.Targets/README.md).

## API Reference

### IFolderHasher

```csharp
public interface IFolderHasher
{
    /// <summary>
    /// Computes a SHA256 hash of the folder contents.
    /// </summary>
    Task<string> GetFolderHashAsync(string folder);

    /// <summary>
    /// Computes a SHA256 hash of the folder contents, excluding folders matching the specified patterns.
    /// </summary>
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders);

    /// <summary>
    /// Computes a hash of the folder contents using the specified algorithm.
    /// </summary>
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm);
}
```

## License

MIT
