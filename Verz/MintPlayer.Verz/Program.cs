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
using System.CommandLine;
using System.Xml.Linq;
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

        // Prepare tool assemblies (registries + SDKs)
        var nugetSource = "https://api.nuget.org/v3/index.json";
        var provider = new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3());
        var sourceContext = new SourceCacheContext();
        var sourceRepository = provider.CreateRepository(new PackageSource(nugetSource));
        var packageFinder = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
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

        var registries = tools.OfType<IPackageRegistry>().ToList();
        var sdks = tools.OfType<IDevelopmentSdk>().ToList();

        // Fallbacks if no tools were configured
        if (registries.Count == 0)
        {
            var nugetRegistryType = Type.GetType("MintPlayer.Verz.Registry.NugetOrg.NugetOrgRegistry, MintPlayer.Verz.Registry.NugetOrg");
            if (nugetRegistryType != null)
            {
                var inst = ActivatorUtilities.CreateInstance(app.Services, nugetRegistryType);
                if (inst is IPackageRegistry reg) registries.Add(reg);
            }
        }

        var dotnetSdk = sdks.FirstOrDefault(s => s.IsApplicable(Directory.GetCurrentDirectory()));
        if (dotnetSdk == null)
        {
            // Try fallback to built-in if referenced
            var dotnetSdkType = Type.GetType("MintPlayer.Verz.Sdks.Dotnet.DotnetSdk, MintPlayer.Verz.Sdks.Dotnet");
            if (dotnetSdkType != null)
            {
                var inst = ActivatorUtilities.CreateInstance(app.Services, dotnetSdkType);
                if (inst is IDevelopmentSdk sdk) dotnetSdk = sdk;
            }
        }

        var rootCmd = new RootCommand("Versioning helper CLI (verz)");

        var dotnetCmd = new Command("dotnet", ".NET package helpers");
        var nextCmd = new Command("next", "Compute next versions for discovered packages");

        var sourcesOpt = new Option<string?>(name: "--nuget-config", description: "Path to nuget.config (defaults to repo root)");
        nextCmd.AddOption(sourcesOpt);

        nextCmd.SetHandler(async (string? nugetConfigPath) =>
        {
            if (dotnetSdk == null)
            {
                Console.Error.WriteLine("No .NET SDK tool found.");
                return;
            }

            if (registries.Count == 0)
            {
                Console.Error.WriteLine("No package registry available.");
                return;
            }

            var cwd = Directory.GetCurrentDirectory();
            var sources = NugetConfigReader.ReadNugetSources(nugetConfigPath ?? Path.Combine(cwd, "nuget.config"));
            if (sources.Count == 0) sources.Add("https://api.nuget.org/v3/index.json");

            var packages = await dotnetSdk.DiscoverPackagesAsync(cwd, cancellationTokenSource.Token);
            if (packages.Count == 0)
            {
                Console.WriteLine("No packable .NET projects found.");
                return;
            }

            var registry = registries[0];
            foreach (var pkg in packages)
            {
                var versions = await registry.GetAllVersionsAsync(pkg.PackageId, sources, cancellationTokenSource.Token);
                var parsed = versions
                    .Select(v => (v, NuGet.Versioning.NuGetVersion.Parse(v)))
                    .Where(t => t.Item2.Major == pkg.Major && !t.Item2.IsPrerelease)
                    .OrderBy(t => t.Item2)
                    .ToList();

                var latest = parsed.LastOrDefault();
                var latestVer = latest.Item2;
                var latestStr = latestVer?.ToNormalizedString();

                string? remoteHash = null;
                if (latestStr != null)
                {
                    remoteHash = await registry.TryGetPublicApiHashAsync(pkg.PackageId, latestStr, sources, pkg.TargetFramework, cancellationTokenSource.Token);
                }

                string localHash;
                try
                {
                    localHash = await dotnetSdk.ComputePublicApiHashAsync(pkg, cancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{pkg.PackageId}] Failed to compute local API hash: {ex.Message}");
                    continue;
                }

                NuGet.Versioning.NuGetVersion nextVersion;
                if (latestVer == null)
                {
                    nextVersion = new NuGet.Versioning.NuGetVersion(pkg.Major, 0, 0);
                }
                else if (!string.IsNullOrWhiteSpace(remoteHash) && string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase))
                {
                    // same API => bump revision
                    nextVersion = new NuGet.Versioning.NuGetVersion(latestVer.Major, latestVer.Minor, latestVer.Patch + 1);
                }
                else
                {
                    // different or unknown => bump minor, reset patch
                    nextVersion = new NuGet.Versioning.NuGetVersion(latestVer.Major, latestVer.Minor + 1, 0);
                }

                Console.WriteLine($"{pkg.PackageId} [{pkg.TargetFramework}] => {nextVersion}");
            }
        }, sourcesOpt);

        dotnetCmd.AddCommand(nextCmd);
        rootCmd.AddCommand(dotnetCmd);

        await rootCmd.InvokeAsync(args);
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

static class NugetConfigReader
{
    public static List<string> ReadNugetSources(string configPath)
    {
        var sources = new List<string>();
        try
        {
            if (File.Exists(configPath))
            {
                var x = System.Xml.Linq.XDocument.Load(configPath);
                var ps = x.Root?.Element("packageSources");
                if (ps != null)
                {
                    foreach (var add in ps.Elements("add"))
                    {
                        var val = add.Attribute("value")?.Value;
                        if (!string.IsNullOrWhiteSpace(val)) sources.Add(val);
                    }
                }
            }
        }
        catch { }
        return sources;
    }
}
