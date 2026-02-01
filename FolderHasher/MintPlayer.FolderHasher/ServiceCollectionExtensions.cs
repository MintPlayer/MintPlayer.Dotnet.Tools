using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher.Abstractions;

namespace MintPlayer.FolderHasher;

/// <summary>
/// Extension methods for registering FolderHasher services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="IFolderHasher"/> service to the dependency injection container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// The <see cref="IFolderHasher"/> is registered as a transient service, meaning a new instance
    /// is created each time it is requested.
    /// </remarks>
    /// <example>
    /// <code>
    /// var services = new ServiceCollection();
    /// services.AddFolderHasher();
    /// var provider = services.BuildServiceProvider();
    /// var hasher = provider.GetRequiredService&lt;IFolderHasher&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddFolderHasher(this IServiceCollection services)
        => services.AddTransient<IFolderHasher, FolderHasher>();
}
