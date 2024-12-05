using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace MintPlayer.Verz.Registry.NugetOrg;

internal interface INugetOrgPackageRegistry : IFeedSupportsDotnetSDK { }

internal class NugetOrgPackageRegistry : INugetOrgPackageRegistry
{
    private FindPackageByIdResource? packageFinder;
    private SourceCacheContext? cache;

    public string NugetFeedUrl => "https://api.nuget.org/v3/index.json";

    public async Task<IEnumerable<string>> GetPackageVersions(string packageId)
    {
        if (packageFinder == null)
            await InitializeFeed();
            //throw new InvalidOperationException($"Did you forget to call {nameof(InitializeFeed)}?");

        var packageVersions = await packageFinder.GetAllVersionsAsync(packageId, cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        return packageVersions.Select(v => string.IsNullOrEmpty(v.Release)
            ? v.ToString()
            : $"{v.Version}-{v.Release}");
    }

    public async Task InitializeFeed()
    {
        var feed = new PackageSource(NugetFeedUrl, "nuget.org");
        var repository = Repository.Factory.GetCoreV3(feed);
        packageFinder = await repository.GetResourceAsync<FindPackageByIdResource>();
        cache = new SourceCacheContext();
    }
}