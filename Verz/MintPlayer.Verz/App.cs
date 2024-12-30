using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.StringExtensions;
using MintPlayer.Verz.Sdk.Dotnet.Abstractions;
using MintPlayer.Verz.Sdk.Nodejs.Abstractions;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Reflection;

namespace MintPlayer.Verz;

internal class App : IApp, IHelper
{
    private readonly IEnumerable<IFeedSupportsDotnetSDK> dotnetFeeds;
    private readonly IEnumerable<IFeedSupportsNodejsSDK> npmFeeds;
    public App(IEnumerable<IFeedSupportsDotnetSDK> dotnetFeeds, IEnumerable<IFeedSupportsNodejsSDK> npmFeeds)
    {
        this.dotnetFeeds = dotnetFeeds;
        this.npmFeeds = npmFeeds;
        //var eq = ReferenceEquals(dotnetFeeds.ElementAt(1), npmFeeds.ElementAt(1));
    }

    public async Task Run(string[] args)
    {
        //const string packageName = "Microsoft.Extensions.DependencyInjection";
        //var result = await Task.WhenAll(dotnetFeeds.Select(f => f.GetPackageVersions(packageName)));
        //var allVersions = result.SelectMany(x => x).Distinct().ToArray();

        const string packageName = "MintPlayer.AspNetCore.SpaServices.Prerendering";
        var result = await Task.WhenAll(dotnetFeeds.Select(f => f.GetPackageVersions(packageName)));
        var allVersions = result.SelectMany(x => x).Distinct().ToArray();

        //var feed = new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org");
        //var repository = Repository.Factory.GetCoreV3(feed);
        //var packageFinder = await repository.GetResourceAsync<FindPackageByIdResource>();

        //var cache = new SourceCacheContext();

        //var packageVersions = await packageFinder.GetAllVersionsAsync("Microsoft.Extensions.DependencyInjection", cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        //foreach (var version in packageVersions)
        //{
        //    Console.WriteLine(string.IsNullOrEmpty(version.Release)
        //        ? version.ToString()
        //        : $"{version.Version}-{version.Release}");
        //}
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
