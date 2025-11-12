using NuGet.Versioning;

namespace MintPlayer.Verz.Core;

public interface IPackageRegistry
{
    string Name { get; }

    // Returns all published versions for a package id
    Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(string packageId, CancellationToken cancellationToken);

    // Try to download the .nupkg for a specific version
    Task<Stream?> DownloadPackageAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);
}
