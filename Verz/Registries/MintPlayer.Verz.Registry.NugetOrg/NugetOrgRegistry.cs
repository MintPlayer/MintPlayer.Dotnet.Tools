using MintPlayer.Verz.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace MintPlayer.Verz.Registry.NugetOrg;

public class NugetOrgRegistry : IPackageRegistry
{
    private const string NugetOrgSource = "https://api.nuget.org/v3/index.json";
    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cacheContext = new();

    public NugetOrgRegistry()
    {
        var provider = new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3());
        _repository = provider.CreateRepository(new PackageSource(NugetOrgSource));
    }

    public string Name => "nuget.org";

    public async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        var finder = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var versions = await finder.GetAllVersionsAsync(packageId, _cacheContext, NullLogger.Instance, cancellationToken);
        return versions?.ToList() ?? new List<NuGetVersion>();
    }

    public async Task<Stream?> DownloadPackageAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        var finder = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var ms = new MemoryStream();
        var ok = await finder.CopyNupkgToStreamAsync(packageId, version, ms, _cacheContext, NullLogger.Instance, cancellationToken);
        if (!ok)
        {
            ms.Dispose();
            return null;
        }
        ms.Position = 0;
        return ms;
    }
}
