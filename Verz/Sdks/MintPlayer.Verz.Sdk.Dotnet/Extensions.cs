using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Sdk.Dotnet;

namespace MintPlayer.Verz;

public static class Extensions
{
    public static IServiceCollection AddDotnetSDK(this IServiceCollection services)
        => services.AddSingleton<IDevelopmentSdk, DotnetSDK>();
}
