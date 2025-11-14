// dotnet tool install --global MintPlayer.Verz

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Core;
using MintPlayer.Verz.Helpers;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.Reflection;

namespace MintPlayer.Verz;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var currentDirectory = Directory.GetCurrentDirectory();
        var verzPath = Path.Combine(currentDirectory, "verz.json");
        builder.Configuration.AddJsonFile(verzPath, optional: true);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.AddSingleton(provider =>
        {
            var verzConfig = new VerzConfig();
            provider.GetRequiredService<IConfiguration>().Bind(verzConfig);
            return verzConfig;
        });

        builder.Services.AddSingleton<ToolCatalog>();
        builder.Services.AddVerzCommand();

        var app = builder.Build();
        var exitCode = await app.InvokeVerzCommandAsync(args);
        return exitCode;
    }

    internal static async Task<(List<IPackageRegistry> registries, List<IDevelopmentSdk> sdks)> LoadToolsAsync(string[] tools, CancellationToken cancellationToken)
    {
        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        if (!Directory.Exists(cacheFolder))
        {
            Directory.CreateDirectory(cacheFolder);
        }

        var provider = new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3());
        var sourceRepository = provider.CreateRepository(new PackageSource("https://api.nuget.org/v3/index.json"));
        var packageFinder = await sourceRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var packagePathResolver = new VersionPackagePathResolver(cacheFolder, useSideBySidePaths: true);
        var extractionContext = new PackageExtractionContext(
            PackageSaveMode.Files,
            XmlDocFileSaveMode.None,
            ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance),
            NullLogger.Instance);
        var sourceContext = new SourceCacheContext();

        var assemblies = await Task.WhenAll((tools ?? Array.Empty<string>())
            .Select(tool => Task.Run(async () =>
            {
                try
                {
                    return Assembly.Load(tool);
                }
                catch
                {
                    var versions = await packageFinder.GetAllVersionsAsync(tool, sourceContext, NullLogger.Instance, cancellationToken);
                    var latest = versions.Last();
                    var identity = new PackageIdentity(tool, latest);
                    using var ms = new MemoryStream();
                    await packageFinder.CopyNupkgToStreamAsync(tool, latest, ms, sourceContext, NullLogger.Instance, cancellationToken);
                    ms.Position = 0;
                    using var packageReader = new PackageArchiveReader(ms);
                    await PackageExtractor.ExtractPackageAsync(string.Empty, packageReader, packagePathResolver, extractionContext, cancellationToken);
                    var path = Path.Combine(packagePathResolver.GetInstallPath(identity), "lib", "net10.0", $"{tool}.dll");
                    return Assembly.LoadFrom(path);
                }
            }, cancellationToken)));

        var types = assemblies.SelectMany(a => a.GetTypes());
        var registries = types.Where(t => typeof(IPackageRegistry).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IPackageRegistry)Activator.CreateInstance(t)!).ToList();
        var sdks = types.Where(t => typeof(IDevelopmentSdk).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IDevelopmentSdk)Activator.CreateInstance(t)!).ToList();

        return (registries, sdks);
    }

    internal static string? FindSingleCsprojInCwd()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly);
        return files.Length == 1 ? files[0] : string.Empty;
    }
}
