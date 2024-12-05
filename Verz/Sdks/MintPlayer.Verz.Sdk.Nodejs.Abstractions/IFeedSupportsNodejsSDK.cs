using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Sdk.Nodejs.Abstractions;

public interface IFeedSupportsNodejsSDK : IPackageRegistry
{
    string NpmFeed { get; }
}
