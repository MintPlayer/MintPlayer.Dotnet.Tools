using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;
using MintPlayer.Verz.Sdk.Nodejs.Abstractions;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Diagnostics.CodeAnalysis;

namespace MintPlayer.Verz.Registry.GithubPackageRegistry;

internal interface IGithubPackageRegistry : IFeedSupportsDotnetSDK, IFeedSupportsNodejsSDK { }

internal class GithubPackageRegistry : IGithubPackageRegistry
{
    private readonly string organization;
    private readonly string token;
    private SourceCacheContext? cache;
    private FindPackageByIdResource? nugetPackageFinder;
    public GithubPackageRegistry(string organization, string token)
    {
        this.organization = organization;
        this.token = token;
    }

    // https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry
    public string NugetFeedUrl => $"https://nuget.pkg.github.com/{organization}/index.json";

    // https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-npm-registry
    public string NpmFeed => "https://npm.pkg.github.com";

    public async Task<IEnumerable<string>> GetPackageVersions(string packageId)
    {
        if (nugetPackageFinder == null)
            await InitializeFeed();

        var packageVersions = await nugetPackageFinder.GetAllVersionsAsync(packageId, cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        return packageVersions.Select(v => string.IsNullOrEmpty(v.Release)
            ? v.ToString()
            : $"{v.Version}-{v.Release}");
    }

    [MemberNotNull(nameof(nugetPackageFinder))]
    public async Task InitializeFeed()
    {
        var feed = new PackageSource(NugetFeedUrl, "github.com");
        feed.Credentials = new PackageSourceCredential("github.com", organization, token, true, null); // goto github.com/settings/tokens
        var repository = Repository.Factory.GetCoreV3(feed);
        nugetPackageFinder = await repository.GetResourceAsync<FindPackageByIdResource>();
        cache = new SourceCacheContext();
    }
}
