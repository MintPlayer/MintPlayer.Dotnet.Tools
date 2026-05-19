using NuGet.Versioning;

namespace MintPlayer.Verz.Abstractions;

public interface IPackageRegistry
{
    /// <summary>
    /// Stable identifier; must match a <c>Registries[].id</c> entry in verz.json
    /// for the host to wire credentials correctly.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Artifact kinds this registry accepts (see <see cref="ArtifactKinds"/>).
    /// </summary>
    IReadOnlyList<string> AcceptedKinds { get; }

    /// <summary>
    /// Read prior-package metadata (public-API-hash + framework-major) for
    /// <paramref name="packageId"/>@<paramref name="version"/>. Returns null if
    /// the registry does not host this version.
    /// </summary>
    Task<PriorPackageInfo?> LookupAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Push <paramref name="artifact"/> to this registry. Implementations rely
    /// on the host's native credential file (~/.nuget/NuGet.config, ~/.npmrc).
    /// Must not prompt; CI runs are non-interactive.
    /// </summary>
    Task PushAsync(Artifact artifact, CancellationToken cancellationToken);
}
