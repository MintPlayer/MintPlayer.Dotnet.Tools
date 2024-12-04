using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;

namespace MintPlayer.Verz.Registry.NugetOrg;

internal interface INugetOrgPackageRegistry : IPackageRegistry, IFeedSupportsDotnetSDK { }

internal class NugetOrgPackageRegistry : INugetOrgPackageRegistry
{
    public string NugetFeed => "https://api.nuget.org/v3/index.json";
}