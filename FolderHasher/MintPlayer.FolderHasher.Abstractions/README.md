# MintPlayer.FolderHasher.Abstractions

Abstractions package for MintPlayer.FolderHasher. Contains the `IFolderHasher` interface for computing deterministic folder hashes.

## Installation

```bash
dotnet add package MintPlayer.FolderHasher.Abstractions
```

## Usage

This package is typically used when you want to depend only on the abstraction (for dependency injection or testing) without taking a dependency on the concrete implementation.

```csharp
using MintPlayer.FolderHasher.Abstractions;

public class MyService
{
    private readonly IFolderHasher _folderHasher;

    public MyService(IFolderHasher folderHasher)
    {
        _folderHasher = folderHasher;
    }

    public async Task<bool> HasFolderChangedAsync(string folder, string previousHash)
    {
        var currentHash = await _folderHasher.GetFolderHashAsync(folder);
        return currentHash != previousHash;
    }
}
```

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

## Related Packages

- [MintPlayer.FolderHasher](../MintPlayer.FolderHasher/README.md) - Concrete implementation
- [MintPlayer.FolderHasher.SpaServices](../MintPlayer.FolderHasher.SpaServices/README.md) - ASP.NET Core SPA integration

## License

MIT
