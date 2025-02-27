// dotnet tool install --global MintPlayer.Verz

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MintPlayer.Verz;

class Program
{
    static async Task Main(string[] args)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            cancellationTokenSource.Cancel();
            e.Cancel = true;
        };

        var app = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((configuration) =>
            {
                var cwd = Directory.GetCurrentDirectory();
                var verzPath = Path.Combine(cwd, "verz.json");
                configuration.AddJsonFile(verzPath, optional: true);
            })
            .ConfigureLogging((logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .Build();

        var verzConfig = new VerzConfig();
        app.Services.GetRequiredService<IConfiguration>().Bind(verzConfig);

        var nugetSource = "https://api.nuget.org/v3/index.json";
        var provider = new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3());
        var sourceContext = new SourceCacheContext();
        var sourceRepository = provider.CreateRepository(new PackageSource(nugetSource));
        var packageFinder = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

        // Ensure output directory exists
        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        if (!Directory.Exists(cacheFolder))
            Directory.CreateDirectory(cacheFolder);

        var packagePathResolver = new VersionPackagePathResolver(cacheFolder, true);
        var extrationContext = new PackageExtractionContext(PackageSaveMode.Files, XmlDocFileSaveMode.None, ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance), NullLogger.Instance);


        var toolAssemblies = await Task.WhenAll(verzConfig.Tools
            .Select((tool) =>
            {
                return Task.Run(async () =>
                {
                    try
                    {
                        return Assembly.Load(tool);
                    }
                    catch (Exception)
                    {
                        var packageFilePath = Path.Combine(cacheFolder, tool);
                        var versions = await packageFinder.GetAllVersionsAsync(tool, sourceContext, NullLogger.Instance, default);
                        var packageId = new PackageIdentity(tool, versions.Last());

                        using var ms = new MemoryStream();
                        var success = await packageFinder.CopyNupkgToStreamAsync(tool, versions.Last(), ms, sourceContext, NullLogger.Instance, default);
                        ms.Seek(0, SeekOrigin.Begin);

                        using var packageReader = new PackageArchiveReader(ms);
                        await PackageExtractor.ExtractPackageAsync(string.Empty, packageReader, packagePathResolver, extrationContext, default);

                        var path = Path.Combine(packagePathResolver.GetInstallPath(packageId), "lib", "net9.0", $"{tool}.dll");
                        return Assembly.LoadFrom(path);
                    }
                }, cancellationTokenSource.Token);
            }));

        var tools = toolAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetInterfaces().Intersect([typeof(IPackageRegistry), typeof(IDevelopmentSdk)]).Any())
            .Select(type => ActivatorUtilities.CreateInstance(app.Services, type))
            .ToList();

        //await app.RunAsync(cancellationTokenSource.Token);
    }
}

public class VersionPackagePathResolver : PackagePathResolver
{
    public VersionPackagePathResolver(string rootDirectory, bool useSideBySidePaths) : base(rootDirectory, useSideBySidePaths)
    {
    }

    public override string GetPackageDirectoryName(PackageIdentity packageIdentity)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(packageIdentity.Id.ToLowerInvariant());
        stringBuilder.Append(Path.DirectorySeparatorChar);
        stringBuilder.Append(packageIdentity.Version.ToNormalizedString().ToLowerInvariant());
        return stringBuilder.ToString();
    }
}