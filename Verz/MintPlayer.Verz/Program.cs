// dotnet tool install --global MintPlayer.Verz

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.Text;
using System.Text.Json;

namespace MintPlayer.Verz;

class Program
{
    static async Task Main(string[] args)
    {
        var cwd = Directory.GetCurrentDirectory();
        var verzPath = Path.Combine(cwd, "verz.json");
        if (!File.Exists(verzPath))
        {
            Console.WriteLine("verz.json not found. Please run 'verz init' first.");
            return;
        }

        var verzJson = File.ReadAllText("verz.json");
        var verzConfig = JsonSerializer.Deserialize<VerzConfig>(verzJson);

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

        foreach (var tool in verzConfig.Tools)
        {
            //var packageVersion = new NuGet.Versioning.NuGetVersion(version);

            var packageFilePath = Path.Combine(cacheFolder, tool);
            var versions = await packageFinder.GetAllVersionsAsync(tool, sourceContext, NullLogger.Instance, default);
            var packageId = new PackageIdentity(tool, versions.Last());

            using var ms = new MemoryStream();
            var success = await packageFinder.CopyNupkgToStreamAsync(tool, versions.Last(), ms, sourceContext, NullLogger.Instance, default);
            ms.Seek(0, SeekOrigin.Begin);

            using var packageReader = new PackageArchiveReader(ms);
            await PackageExtractor.ExtractPackageAsync("", packageReader, packagePathResolver, extrationContext, default);
        }
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