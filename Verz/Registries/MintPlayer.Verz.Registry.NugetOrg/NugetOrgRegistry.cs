using MintPlayer.Verz.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace MintPlayer.Verz.Registry.NugetOrg;

public class NugetOrgRegistry : IPackageRegistry
{
    public async Task<IReadOnlyList<string>> GetAllVersionsAsync(string packageId, IEnumerable<string> sources, CancellationToken cancellationToken)
    {
        var allVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var logger = NullLogger.Instance;
        var cache = new SourceCacheContext();
        var providers = Repository.Provider.GetCoreV3();

        foreach (var src in sources)
        {
            var repository = new SourceRepository(new PackageSource(src), providers);
            var finder = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            if (finder == null) continue;

            var versions = await finder.GetAllVersionsAsync(packageId, cache, logger, cancellationToken);
            foreach (var v in versions.Select(v => v.ToNormalizedString()))
                allVersions.Add(v);
        }

        return allVersions.OrderBy(NuGet.Versioning.NuGetVersion.Parse).ToList();
    }

    public async Task<string?> TryGetPublicApiHashAsync(string packageId, string version, IEnumerable<string> sources, string targetFramework, CancellationToken cancellationToken)
    {
        var logger = NullLogger.Instance;
        var cache = new SourceCacheContext();
        var providers = Repository.Provider.GetCoreV3();
        var identity = new NuGet.Packaging.Core.PackageIdentity(packageId, NuGet.Versioning.NuGetVersion.Parse(version));

        foreach (var src in sources)
        {
            var repository = new SourceRepository(new PackageSource(src), providers);
            var finder = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            if (finder == null) continue;

            using var ms = new MemoryStream();
            var ok = await finder.CopyNupkgToStreamAsync(packageId, identity.Version, ms, cache, logger, cancellationToken);
            if (!ok) continue;

            ms.Position = 0;
            using var reader = new PackageArchiveReader(ms, leaveStreamOpen: false);

            // Try custom nuspec metadata element first
            try
            {
                var nuspec = await reader.GetNuspecAsync(cancellationToken);
                var publicApiHash = nuspec.GetMetadataValue("PublicApiHash");
                if (!string.IsNullOrWhiteSpace(publicApiHash))
                    return publicApiHash;
            }
            catch { /* ignore */ }

            // Fallback: try a convention file inside package
            try
            {
                var files = await reader.GetFilesAsync(cancellationToken);
                var hashFile = files.FirstOrDefault(f => f.Replace('\\', '/').EndsWith("build/PublicApiHash.txt", StringComparison.OrdinalIgnoreCase));
                if (hashFile != null)
                {
                    using var s = await reader.GetStreamAsync(hashFile, cancellationToken);
                    using var sr = new StreamReader(s);
                    var content = await sr.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(content))
                        return content.Trim();
                }
            }
            catch { /* ignore */ }
        }

        return null;
    }
}
