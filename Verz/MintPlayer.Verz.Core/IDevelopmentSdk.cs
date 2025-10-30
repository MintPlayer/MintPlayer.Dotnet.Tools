namespace MintPlayer.Verz.Core;

public interface IDevelopmentSdk
{
    // Returns true if the SDK can operate in this repository
    bool IsApplicable(string rootPath);

    // Discover packable packages within the repository
    Task<IReadOnlyList<PackageInfo>> DiscoverPackagesAsync(string rootPath, CancellationToken cancellationToken);

    // Compute the Public API hash for the built artifact of a discovered package
    Task<string> ComputePublicApiHashAsync(PackageInfo package, CancellationToken cancellationToken);
}

public record PackageInfo(
    string ProjectPath,
    string PackageId,
    string TargetFramework,
    int Major
);
