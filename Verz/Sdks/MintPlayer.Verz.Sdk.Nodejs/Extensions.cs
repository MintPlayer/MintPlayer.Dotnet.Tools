using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Verz.Sdk.Nodejs;
using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz;

public static class Extensions
{
    public static IServiceCollection AddNodejsSDK(this IServiceCollection services)
        => services.AddSingleton<IDevelopmentSdk, NodejsSDK>();
}
