using Microsoft.Extensions.DependencyInjection;
using MintPlayer.CliParser.Abstractions;

namespace MintPlayer.CliParser.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the CLI parser to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCliParser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<ICliParser, CliParser>();
    }
}
