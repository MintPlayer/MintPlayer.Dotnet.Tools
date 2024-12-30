using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Registry.NpmjsCom;
using MintPlayer.Verz.Sdk.Nodejs.Abstractions;

namespace MintPlayer.Verz;

public static class Extensions
{
    public static IServiceCollection AddNpmjsComRegistry(this IServiceCollection services)
        => services
            .AddSingleton<INpmjsComPackageRegistry, NpmjsComPackageRegistry>()
            .AddSingleton<IPackageRegistry>(provider => provider.GetRequiredService<INpmjsComPackageRegistry>())
            .AddSingleton<IFeedSupportsNodejsSDK>(provider => provider.GetRequiredService<INpmjsComPackageRegistry>());
}
