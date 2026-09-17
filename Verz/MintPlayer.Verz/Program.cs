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
                    var path = ResolvePluginAssembly(packagePathResolver.GetInstallPath(identity), tool);
                    return Assembly.LoadFrom(path);
                }
            }, cancellationToken)));

        static string ResolvePluginAssembly(string installPath, string tool)
        {
            // Was hardcoded to lib/net10.0. That is a runtime failure waiting to happen: the plugin
            // packages multi-target, and the day one stops shipping the hardcoded moniker, verz
            // throws FileNotFoundException at plugin-load time with nothing failing at build time to
            // warn anyone. Probe instead, and pick the way NuGet would.
            var lib = Path.Combine(installPath, "lib");
            if (!Directory.Exists(lib))
                throw new FileNotFoundException($"The package for '{tool}' has no lib/ folder, so it ships no loadable plugin assembly.", lib);

            var running = Environment.Version.Major;

            // Highest netN.0 the running runtime can actually load, i.e. N <= running. Never roll
            // forward: a net11.0 assembly does not load on .NET 10.
            var best = Directory.EnumerateDirectories(lib)
                .Select(d => new { Dir = d, Name = Path.GetFileName(d) })
                .Select(x => new
                {
                    x.Dir,
                    Major = x.Name.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(x.Name.AsSpan(3).ToString().Split('.')[0], out var m) ? m : -1,
                })
                .Where(x => x.Major > 0 && x.Major <= running)
                .OrderByDescending(x => x.Major)
                .Select(x => x.Dir)
                .FirstOrDefault();

            if (best is null)
            {
                var available = string.Join(", ", Directory.EnumerateDirectories(lib).Select(Path.GetFileName));
                throw new FileNotFoundException(
                    $"The package for '{tool}' ships no assembly loadable on .NET {running}. Available: {available}.", lib);
            }

            return Path.Combine(best, $"{tool}.dll");
        }

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
