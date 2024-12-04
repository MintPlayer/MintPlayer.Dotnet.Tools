using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.StringExtensions;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Reflection;

namespace MintPlayer.Verz;

internal class App : IApp, IHelper
{
    public App()
    {
        
    }

    public async Task Run(string[] args)
    {
        var feed = new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org");
        var repository = Repository.Factory.GetCoreV3(feed);
        var packageFinder = await repository.GetResourceAsync<FindPackageByIdResource>();

        var cache = new SourceCacheContext();

        var packageVersions = await packageFinder.GetAllVersionsAsync("Microsoft.Extensions.DependencyInjection", cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        foreach (var version in packageVersions)
        {
            Console.WriteLine(string.IsNullOrEmpty(version.Release)
                ? version.ToString()
                : $"{version.Version}-{version.Release}");
        }
    }

    public Task ShowUsage()
    {
        var versionString = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .ToString();

        var versionLine = $"verz v{versionString}";

        Console.WriteLine($"""
            {versionLine}
            {"-".Repeat(versionLine.Length)}
            Usage:
              verz 
            """);

        return Task.CompletedTask;
    }
}
