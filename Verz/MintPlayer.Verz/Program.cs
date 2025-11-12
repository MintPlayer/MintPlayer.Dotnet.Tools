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
using NuGet.Versioning;
using System.CommandLine;
using System.Reflection;
using System.Text;

namespace MintPlayer.Verz;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) => { cts.Cancel(); e.Cancel = true; };

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((configuration) =>
            {
                var cwd = Directory.GetCurrentDirectory();
                //var cwd = @"C:\Repos\MintPlayer.DotnetDesktop.Tools";
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
        host.Services.GetRequiredService<IConfiguration>().Bind(verzConfig);

        var (registries, sdks) = await LoadToolsAsync(verzConfig.Tools, cts.Token);

        var root = new RootCommand("MintPlayer.Verz: compute package versions across feeds");
        var dotnetVersionCmd = new Command("dotnet-version", "Compute next version for a .NET project")
        {
            new Option<string>(name: "--project")
            {
                Description = ".csproj file",
                DefaultValueFactory = (arg) => FindSingleCsprojInCwd()
            },
            new Option<string>(name: "--configuration")
            {
                Description = "Build configuration (for locating bin)",
                DefaultValueFactory = (arg) => "Release"
            },
        };

        dotnetVersionCmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue<string>("--project");
            var configuration = parseResult.GetValue<string>("--configuration");
            var sdk = sdks.OfType<IDevelopmentSdk>().FirstOrDefault(s => s.CanHandle(project));
            if (sdk == null)
            {
                Console.Error.WriteLine("No compatible SDK found to handle project: " + project);
                return -1;
            }

            var packageId = await sdk.GetPackageIdAsync(project, cts.Token);
            var major = await sdk.GetMajorVersionAsync(project, cts.Token);

            var versionSet = new HashSet<NuGetVersion>(VersionComparer.VersionRelease);
            var latestPerRegistry = new List<(IPackageRegistry Registry, NuGetVersion Version)>();
            foreach (var r in registries)
            {
                var versions = await r.GetAllVersionsAsync(packageId, cts.Token);
                var inMajor = versions.Where(v => v.Major == major).OrderBy(v => v).ToList();
                if (inMajor.Count > 0)
                {
                    latestPerRegistry.Add((r, inMajor.Last()));
                    foreach (var v in inMajor) versionSet.Add(v);
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

            await using var nupkg = await source.DownloadPackageAsync(packageId, latest, cts.Token) ?? Stream.Null;
            string? prevHash = null;
            if (nupkg != Stream.Null)
            {
                prevHash = await sdk.ComputePackagePublicApiHashAsync(nupkg, major, cts.Token);
            }
            var currentHash = await sdk.ComputeCurrentPublicApiHashAsync(project, configuration, cts.Token);

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
        });

        var initDotnetCmd = new Command("init-dotnet", "Replace <Version> tags in all csproj with placeholder 0.0.0-placeholder")
        {
            new Option<string>("--root")
            {
                Description = "Root directory to scan for .csproj files",
                DefaultValueFactory = (arg) => Directory.GetCurrentDirectory()
            }
        };
        initDotnetCmd.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var rootDir = parseResult.GetValue<string>("--root");
            var csprojs = Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories);
            int updated = 0;
            foreach (var csproj in csprojs)
            {
                try
                {
                    var text = File.ReadAllText(csproj);
                    var original = text;
                    if (text.Contains("<Version>", StringComparison.OrdinalIgnoreCase))
                    {
                        text = System.Text.RegularExpressions.Regex.Replace(text, "<Version>.*?</Version>", "<Version>0.0.0-placeholder</Version>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
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
                catch { }
            }
            Console.WriteLine($"Updated {updated} project files with placeholder version.");
            return 0;
        });

        root.Add(dotnetVersionCmd);
        root.Add(initDotnetCmd);

        var rootCommand = root.Parse(args);
        return await rootCommand.InvokeAsync();
    }

    private static async Task<(List<IPackageRegistry> registries, List<IDevelopmentSdk> sdks)> LoadToolsAsync(string[] tools, CancellationToken cancellationToken)
    {
        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

        var provider = new SourceRepositoryProvider(new PackageSourceProvider(NullSettings.Instance), Repository.Provider.GetCoreV3());
        var sourceRepository = provider.CreateRepository(new PackageSource("https://api.nuget.org/v3/index.json"));
        var packageFinder = await sourceRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var packagePathResolver = new VersionPackagePathResolver(cacheFolder, true);
        var extractionContext = new PackageExtractionContext(PackageSaveMode.Files, XmlDocFileSaveMode.None, ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance), NullLogger.Instance);
        var sourceContext = new SourceCacheContext();

        var assemblies = await Task.WhenAll(tools.Select(tool => Task.Run(async () =>
        {
            try { return Assembly.Load(tool); }
            catch
            {
                var versions = await packageFinder.GetAllVersionsAsync(tool, sourceContext, NullLogger.Instance, cancellationToken);
                var identity = new PackageIdentity(tool, versions.Last());
                using var ms = new MemoryStream();
                var ok = await packageFinder.CopyNupkgToStreamAsync(tool, versions.Last(), ms, sourceContext, NullLogger.Instance, cancellationToken);
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

    private static string FindSingleCsprojInCwd()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly);
        return files.Length == 1 ? files[0] : string.Empty;
    }
}

public class VersionPackagePathResolver : PackagePathResolver
{
    public VersionPackagePathResolver(string rootDirectory, bool useSideBySidePaths) : base(rootDirectory, useSideBySidePaths) { }
    public override string GetPackageDirectoryName(PackageIdentity packageIdentity)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(packageIdentity.Id.ToLowerInvariant());
        stringBuilder.Append(Path.DirectorySeparatorChar);
        stringBuilder.Append(packageIdentity.Version.ToNormalizedString().ToLowerInvariant());
        return stringBuilder.ToString();
    }
}
