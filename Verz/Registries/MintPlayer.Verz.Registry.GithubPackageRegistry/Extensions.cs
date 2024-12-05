using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Registry.GithubPackageRegistry;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;
using MintPlayer.Verz.Sdk.Nodejs.Abstractions;

namespace MintPlayer.Verz;

public static class Extensions
{
    public static IServiceCollection AddGithubPackageRegistry(this IServiceCollection services, string organization, string token)
        => services
            .AddSingleton<IGithubPackageRegistry>(provider => new GithubPackageRegistry(organization, token))
            .AddSingleton<IPackageRegistry>(provider => provider.GetRequiredService<IGithubPackageRegistry>())
            .AddSingleton<IFeedSupportsDotnetSDK>(provider => provider.GetRequiredService<IGithubPackageRegistry>())
            .AddSingleton<IFeedSupportsNodejsSDK>(provider => provider.GetRequiredService<IGithubPackageRegistry>());
}
