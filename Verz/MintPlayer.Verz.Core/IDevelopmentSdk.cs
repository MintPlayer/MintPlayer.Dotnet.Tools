namespace MintPlayer.Verz.Core;

public interface IDevelopmentSdk
{
    // Returns true if the SDK can handle the project at path (e.g., .csproj)
    bool CanHandle(string projectPath);

    // Derive package id from project (PackageId or project file name)
    Task<string> GetPackageIdAsync(string projectPath, CancellationToken cancellationToken);

    // Determine major version from project definition (e.g., TargetFramework(s))
    Task<int> GetMajorVersionAsync(string projectPath, CancellationToken cancellationToken);

    // Compute the Public API hash for current project build output
    Task<string> ComputeCurrentPublicApiHashAsync(string projectPath, string configuration, CancellationToken cancellationToken);

    // Compute Public API hash from a downloaded .nupkg (prefer <PublicApiHash> in nuspec; fallback to computing from contained lib assembly)
    Task<string?> ComputePackagePublicApiHashAsync(Stream nupkgStream, int majorVersion, CancellationToken cancellationToken);
}
