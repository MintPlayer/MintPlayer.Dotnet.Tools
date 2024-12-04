using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Registry.NugetOrg;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;

namespace MintPlayer.Verz;

public static class Extensions
{
    public static IServiceCollection AddNugetOrgRegistry(this IServiceCollection services)
        => services
            .AddSingleton<INugetOrgPackageRegistry, NugetOrgPackageRegistry>()
            .AddSingleton<IPackageRegistry>(provider => provider.GetRequiredService<INugetOrgPackageRegistry>())
            .AddSingleton<IFeedSupportsDotnetSDK>(provider => provider.GetRequiredService<INugetOrgPackageRegistry>());
}
