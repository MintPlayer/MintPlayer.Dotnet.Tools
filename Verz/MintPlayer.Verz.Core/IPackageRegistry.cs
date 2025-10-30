namespace MintPlayer.Verz.Core;

public interface IPackageRegistry
{
    // Return all versions for a package id across configured sources
    Task<IReadOnlyList<string>> GetAllVersionsAsync(string packageId, IEnumerable<string> sources, CancellationToken cancellationToken);

    // Try to obtain the PublicApiHash for a specific version and TFM
    Task<string?> TryGetPublicApiHashAsync(string packageId, string version, IEnumerable<string> sources, string targetFramework, CancellationToken cancellationToken);
}
