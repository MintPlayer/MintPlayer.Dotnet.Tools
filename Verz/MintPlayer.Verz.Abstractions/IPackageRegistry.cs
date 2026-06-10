using NuGet.Versioning;

namespace MintPlayer.Verz.Abstractions;

public interface IPackageRegistry
{
    /// <summary>
    /// Plugin kind, e.g. "nuget" or "npm". Surfaces in diagnostics. Multiple
    /// plugins may share a kind (NugetOrg + GithubPackageRegistry both
    /// return "nuget").
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Artifact kinds this plugin can push (see <see cref="ArtifactKinds"/>).
    /// </summary>
    IReadOnlyList<string> AcceptedKinds { get; }

    /// <summary>
    /// Whether this plugin can talk to <paramref name="registryUrl"/>. The
    /// host iterates loaded registry plugins and picks the first that says
    /// yes. URL shape, hostname, or other heuristics may inform the answer.
    /// </summary>
    bool CanHandle(string registryUrl);

    /// <summary>
    /// Read prior-package metadata (public-API-hash + framework-major) for
    /// <paramref name="packageId"/>@<paramref name="version"/> from the feed
    /// at <paramref name="registryUrl"/>. Returns null if the registry does
    /// not host this version.
    /// </summary>
    Task<PriorPackageInfo?> LookupAsync(
        string registryUrl,
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Push <paramref name="artifact"/> to the feed at
    /// <paramref name="registryUrl"/>. Auth flows through the host's native
    /// credential mechanism (~/.nuget/NuGet.config, ~/.npmrc); the plugin
    /// must not prompt.
    /// </summary>
    Task PushAsync(
        string registryUrl,
        Artifact artifact,
        CancellationToken cancellationToken);
}
