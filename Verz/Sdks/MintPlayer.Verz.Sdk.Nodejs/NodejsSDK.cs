using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Sdk.Nodejs;

internal class NodejsSDK : IDevelopmentSdk
{
    public Task<string> GetPackageById(string packageId)
    {
        throw new NotImplementedException();
    }
}
