using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Nodejs.Abstractions;

namespace MintPlayer.Verz.Registry.NpmjsCom;

internal interface INpmjsComPackageRegistry : IPackageRegistry, IFeedSupportsNodejsSDK { }

internal class NpmjsComPackageRegistry : INpmjsComPackageRegistry
{
    public string NpmFeed => "https://registry.npmjs.org";

    public Task<IEnumerable<string>> GetPackageVersions(string packageId)
    {
        return Task.FromResult<IEnumerable<string>>([]);
    }
}
