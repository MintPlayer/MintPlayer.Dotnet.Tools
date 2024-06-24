using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher.Abstractions;

namespace MintPlayer.FolderHasher;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFolderHasher(this IServiceCollection services)
        => services.AddTransient<IFolderHasher, FolderHasher>();
}
