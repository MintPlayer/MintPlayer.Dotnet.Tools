using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Sdk.Dotnet.Abstractions;

public interface IFeedSupportsDotnetSDK : IPackageRegistry
{
    string NugetFeedUrl { get; }
    Task InitializeFeed();
}
