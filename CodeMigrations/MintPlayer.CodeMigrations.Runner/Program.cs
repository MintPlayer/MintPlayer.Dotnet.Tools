// See https://aka.ms/new-console-template for more information
using MintPlayer.CodeMigrations.Tools;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

Console.WriteLine("Current directory");
Console.WriteLine(Directory.GetCurrentDirectory());
var migrationConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "migration-config.json");
if (!File.Exists(migrationConfigPath))
{
    Console.WriteLine($"Migration config file not found at {migrationConfigPath}");
    return;
}

var migrationConfigJson = File.ReadAllText(migrationConfigPath);
var migrationConfig = System.Text.Json.JsonSerializer.Deserialize<MigrationConfig>(migrationConfigJson);
var packageName = migrationConfig.PackageName;

var cacheContext = new SourceCacheContext();
var downloadContext = new PackageDownloadContext(cacheContext);

var logger = NullLogger.Instance;
// Connect to the default NuGet V3 endpoint
var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
var packageFinder = await repository.GetResourceAsync<FindPackageByIdResource>();
var packageVersions = await packageFinder.GetAllVersionsAsync(packageName, cacheContext, logger, CancellationToken.None);

var packageDownloader = await repository.GetResourceAsync<DownloadResource>();
var latestVersion = packageVersions.OrderByDescending(v => v).FirstOrDefault();
var latestIdentifier = new PackageIdentity(packageName, latestVersion);

var packageFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var downloadResult = await packageDownloader.GetDownloadResourceResultAsync(latestIdentifier, downloadContext, packageFolder, logger, CancellationToken.None);
