using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;

namespace MintPlayer.Verz.Sdk.Dotnet;

internal class DotnetSDK : IDevelopmentSdk
{
    private readonly IEnumerable<IFeedSupportsDotnetSDK> dotnetFeeds;
    public DotnetSDK(IEnumerable<IFeedSupportsDotnetSDK> dotnetFeeds)
    {
        this.dotnetFeeds = dotnetFeeds;
    }

    public Task<string> GetPackageById(string packageId)
    {
        throw new NotImplementedException();
        //dotnetFeeds.Select(feed => feed.NugetFeedUrl)
    }
}
