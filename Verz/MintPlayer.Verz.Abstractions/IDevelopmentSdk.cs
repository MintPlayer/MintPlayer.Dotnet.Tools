namespace MintPlayer.Verz.Abstractions;

public interface IDevelopmentSdk
{
    /// <summary>
    /// Stable identifier for the SDK, e.g. "dotnet" or "nodejs". Surfaces in
    /// <see cref="DiscoveredProject.OwnerSdkId"/> and in log/error output.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Enumerate every project of this SDK's kind under <paramref name="repoRoot"/>.
    /// </summary>
    Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        string repoRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Return the in-repo dependencies of <paramref name="project"/> as a list of
    /// PackageIds. Only edges whose target is present in <paramref name="repoIndex"/>
    /// should be emitted; external dependencies are ignored at the graph layer.
    /// </summary>
    Task<IReadOnlyList<string>> EnumerateInRepoDependenciesAsync(
        DiscoveredProject project,
        IReadOnlyDictionary<string, DiscoveredProject> repoIndex,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compute the public-API hash of <paramref name="project"/> at the current
    /// working-tree state. Implementations may build the project if necessary.
    /// </summary>
    Task<string> ComputePublicApiHashAsync(
        DiscoveredProject project,
        string configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Write <paramref name="version"/> into the project file in place. The file is
    /// expected to live in a CI workspace and is not committed; round-trip
    /// preservation is best-effort but reformatting is acceptable.
    /// </summary>
    Task StampVersionAsync(
        DiscoveredProject project,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Produce one or more publishable artifacts for <paramref name="project"/>.
    /// Each artifact carries a kind tag (e.g. "nuget", "npm") that registries
    /// match against via <see cref="IPackageRegistry.AcceptedKinds"/>.
    /// </summary>
    Task<IReadOnlyList<Artifact>> PackAsync(
        DiscoveredProject project,
        string configuration,
        CancellationToken cancellationToken);
}
