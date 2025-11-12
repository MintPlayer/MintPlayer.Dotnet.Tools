// dotnet tool install --global MintPlayer.Verz

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Verz.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

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
        builder.Services.AddVerzCommandTree();

        var app = builder.Build();
        var exitCode = await app.Services.InvokeVerzCommandAsync(args);
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

[CliRootCommand(Name = "verz", Description = "MintPlayer.Verz: compute package versions across feeds")]
public partial class VerzCommand : ICliCommand
{
    public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
}

[CliCommand("dotnet-version", Description = "Compute next version for a .NET project")]
[CliParentCommand(typeof(VerzCommand))]
internal partial class DotnetVersionCommand : ICliCommand
{
    private readonly ToolCatalog toolCatalog;

    public DotnetVersionCommand(ToolCatalog toolCatalog)
    {
        this.toolCatalog = toolCatalog;
    }

    [CliOption("--project", Description = ".csproj file"), NoInterfaceMember]
    public string? Project { get; set; }

    [CliOption("--configuration", Description = "Build configuration (for locating bin)", DefaultValue = "Release"), NoInterfaceMember]
    public string Configuration { get; set; } = "Release";

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var toolset = await toolCatalog.GetToolsetAsync(cancellationToken);
        var registries = toolset.Registries;
        var sdks = toolset.Sdks;

        var project = string.IsNullOrWhiteSpace(Project) ? Program.FindSingleCsprojInCwd() : Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            Console.Error.WriteLine("No project specified and none detected in the current directory.");
            return -1;
        }

        var sdk = sdks.FirstOrDefault(s => s.CanHandle(project));
        if (sdk == null)
        {
            Console.Error.WriteLine("No compatible SDK found to handle project: " + project);
            return -1;
        }

        var packageId = await sdk.GetPackageIdAsync(project, cancellationToken);
        var major = await sdk.GetMajorVersionAsync(project, cancellationToken);

        var versionSet = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
        var latestPerRegistry = new List<(IPackageRegistry Registry, NuGetVersion Version)>();
        foreach (var registry in registries)
        {
            var versions = await registry.GetAllVersionsAsync(packageId, cancellationToken);
            var inMajor = versions.Where(v => v.Major == major).OrderBy(v => v).ToList();
            if (inMajor.Count == 0)
            {
                continue;
            }

            latestPerRegistry.Add((registry, inMajor.Last()));
            foreach (var version in inMajor)
            {
                versionSet.Add(version);
            }
        }

        NuGetVersion? latest = versionSet.OrderBy(v => v).LastOrDefault();
        if (latest == null)
        {
            Console.WriteLine($"{major}.0.0");
            return -1;
        }

        var source = latestPerRegistry.FirstOrDefault(x => VersionComparer.Version.Equals(x.Version, latest)).Registry
                     ?? registries.First();

        await using var nupkg = await source.DownloadPackageAsync(packageId, latest, cancellationToken) ?? Stream.Null;
        string? prevHash = null;
        if (nupkg != Stream.Null)
        {
            prevHash = await sdk.ComputePackagePublicApiHashAsync(nupkg, major, cancellationToken);
        }
        var currentHash = await sdk.ComputeCurrentPublicApiHashAsync(project, Configuration, cancellationToken);

        NuGetVersion next;
        if (!string.IsNullOrWhiteSpace(prevHash) && string.Equals(prevHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            next = new NuGetVersion(major, latest.Minor, latest.Patch + 1);
        }
        else
        {
            next = new NuGetVersion(major, latest.Minor + 1, 0);
        }

        Console.WriteLine(next.ToNormalizedString());
        return 0;
    }
}

[CliCommand("init-dotnet", Description = "Replace <Version> tags in all csproj with placeholder 0.0.0-placeholder")]
[CliParentCommand(typeof(VerzCommand))]
public partial class InitDotnetCommand : ICliCommand
{
    private static readonly Regex VersionRegex = new("<Version>.*?</Version>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    [CliOption("--root", Description = "Root directory to scan for .csproj files"), NoInterfaceMember]
    public string? Root { get; set; }

    public Task<int> Execute(CancellationToken cancellationToken)
    {
        var rootDir = string.IsNullOrWhiteSpace(Root) ? Directory.GetCurrentDirectory() : Root;
        var csprojs = Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories);

        var updated = 0;
        foreach (var csproj in csprojs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = File.ReadAllText(csproj);
                var original = text;
                if (VersionRegex.IsMatch(text))
                {
                    text = VersionRegex.Replace(text, "<Version>0.0.0-placeholder</Version>");
                }
                else
                {
                    var insertIdx = text.IndexOf("<PropertyGroup>", StringComparison.OrdinalIgnoreCase);
                    if (insertIdx >= 0)
                    {
                        var endIdx = text.IndexOf("</PropertyGroup>", insertIdx, StringComparison.OrdinalIgnoreCase);
                        if (endIdx > insertIdx)
                        {
                            var toInsert = "\n    <Version>0.0.0-placeholder</Version>\n";
                            text = text.Insert(endIdx, toInsert);
                        }
                    }
                }

                if (!string.Equals(original, text, StringComparison.Ordinal))
                {
                    File.WriteAllText(csproj, text);
                    updated++;
                }
            }
            catch
            {
                // Ignore errors so other projects still get updated.
            }
        }

        Console.WriteLine($"Updated {updated} project files with placeholder version.");
        return Task.FromResult(0);
    }
}

internal sealed class ToolCatalog
{
    private readonly VerzConfig verzConfig;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private ToolCatalogResult? cache;

    public ToolCatalog(VerzConfig verzConfig)
    {
        this.verzConfig = verzConfig;
    }

    public async Task<ToolCatalogResult> GetToolsetAsync(CancellationToken cancellationToken)
    {
        if (cache is not null)
        {
            return cache;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (cache is null)
            {
                var (registries, sdks) = await Program.LoadToolsAsync(verzConfig.Tools ?? [], cancellationToken);
                cache = new ToolCatalogResult(registries, sdks);
            }
        }
        finally
        {
            initializationLock.Release();
        }

        return cache!;
    }
}

internal sealed class ToolCatalogResult
{
    public ToolCatalogResult(IReadOnlyList<IPackageRegistry> registries, IReadOnlyList<IDevelopmentSdk> sdks)
    {
        Registries = registries;
        Sdks = sdks;
    }

    public IReadOnlyList<IPackageRegistry> Registries { get; }
    public IReadOnlyList<IDevelopmentSdk> Sdks { get; }
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
