using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Helpers;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace MintPlayer.Verz.Hosting;

internal sealed class PluginLoader(ILogger<PluginLoader> logger, IServiceProvider services)
{
    private static readonly string[] CandidateTfms = ["net10.0", "net9.0", "net8.0"];

    public async Task<PluginCatalog> LoadAsync(VerzConfig config, CancellationToken cancellationToken)
    {
        var sdks = new List<IDevelopmentSdk>();
        var registries = new List<IPackageRegistry>();

        if (config.Plugins.Count == 0)
        {
            logger.LogDebug("no plugins listed in verz.json");
            return new PluginCatalog(sdks, registries);
        }

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
        Directory.CreateDirectory(cacheRoot);

        var sources = BuildSourceRepositories(config.Registries);
        var pathResolver = new VersionPackagePathResolver(cacheRoot, useSideBySidePaths: true);
        var extractionContext = new PackageExtractionContext(
            PackageSaveMode.Files,
            XmlDocFileSaveMode.None,
            ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance),
            NullLogger.Instance);
        var sourceCacheContext = new SourceCacheContext();

        foreach (var entry in config.Plugins)
        {
            var assembly = await ResolveAndLoadAsync(
                entry, sources, pathResolver, extractionContext, sourceCacheContext, cancellationToken);
            if (assembly is null) continue;

            foreach (var type in EnumerateConcreteTypes(assembly))
            {
                if (typeof(IDevelopmentSdk).IsAssignableFrom(type))
                {
                    var instance = (IDevelopmentSdk)ActivatorUtilities.CreateInstance(services, type);
                    sdks.Add(instance);
                    logger.LogInformation("loaded SDK plugin {Id} ({Type})", instance.Id, type.FullName);
                }
                if (typeof(IPackageRegistry).IsAssignableFrom(type))
                {
                    var instance = (IPackageRegistry)ActivatorUtilities.CreateInstance(services, type);
                    registries.Add(instance);
                    logger.LogInformation("loaded registry plugin {Id} ({Type})", instance.Id, type.FullName);
                }
            }
        }

        return new PluginCatalog(sdks, registries);
    }

    private async Task<Assembly?> ResolveAndLoadAsync(
        PluginEntry entry,
        IReadOnlyList<SourceRepository> sources,
        VersionPackagePathResolver pathResolver,
        PackageExtractionContext extractionContext,
        SourceCacheContext sourceCacheContext,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            try
            {
                var finder = await source.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                var allVersions = await finder.GetAllVersionsAsync(
                    entry.Id, sourceCacheContext, NullLogger.Instance, cancellationToken);

                var selected = entry.Version is null
                    ? allVersions.Where(v => !v.IsPrerelease).OrderByDescending(v => v).FirstOrDefault()
                    : allVersions.FirstOrDefault(v => v.ToNormalizedString() == entry.Version);

                if (selected is null) continue;

                if (entry.Version is null)
                {
                    logger.LogWarning(
                        "plugin {Id} is unpinned; using {Version} (latest stable). " +
                        "Pin via {{\"id\":\"{Id}\",\"version\":\"...\"}} for reproducible loads.",
                        entry.Id, selected.ToNormalizedString(), entry.Id);
                }

                var identity = new PackageIdentity(entry.Id, selected);
                var installPath = pathResolver.GetInstallPath(identity);

                if (!File.Exists(Path.Combine(installPath, $"{entry.Id}.nuspec")))
                {
                    await using var stream = new MemoryStream();
                    await finder.CopyNupkgToStreamAsync(
                        entry.Id, selected, stream, sourceCacheContext, NullLogger.Instance, cancellationToken);
                    stream.Position = 0;
                    using var reader = new PackageArchiveReader(stream);
                    await PackageExtractor.ExtractPackageAsync(
                        source.PackageSource.Source, reader, pathResolver, extractionContext, cancellationToken);
                }

                var assemblyPath = ResolveAssemblyPath(installPath, entry.Id);
                if (assemblyPath is null)
                {
                    logger.LogWarning(
                        "plugin {Id} {Version} has no lib/net*/{Id}.dll", entry.Id, selected, entry.Id);
                    return null;
                }

                var alc = new PluginLoadContext(assemblyPath);
                return alc.LoadFromAssemblyPath(assemblyPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "plugin {Id} not found on {Source}", entry.Id, source.PackageSource.Source);
            }
        }

        logger.LogError("plugin {Id} could not be resolved from any configured registry", entry.Id);
        return null;
    }

    private static string? ResolveAssemblyPath(string installPath, string packageId)
    {
        foreach (var tfm in CandidateTfms)
        {
            var candidate = Path.Combine(installPath, "lib", tfm, $"{packageId}.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IReadOnlyList<SourceRepository> BuildSourceRepositories(IReadOnlyList<RegistryEntry> registries)
    {
        var providers = Repository.Provider.GetCoreV3();
        var repos = new List<SourceRepository>(capacity: registries.Count + 1);

        foreach (var entry in registries)
        {
            // npm registries aren't NuGet sources; skip them at the plugin-loading stage.
            if (string.Equals(entry.Kind, "npm", StringComparison.OrdinalIgnoreCase)) continue;
            repos.Add(new SourceRepository(new PackageSource(entry.Url, entry.Id), providers));
        }

        if (repos.Count == 0)
        {
            repos.Add(new SourceRepository(
                new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"),
                providers));
        }

        return repos;
    }

    private static IEnumerable<Type> EnumerateConcreteTypes(Assembly assembly)
    {
        Type[] exported;
        try
        {
            exported = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            exported = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        return exported.Where(t =>
            t is { IsClass: true, IsAbstract: false } &&
            t.GetConstructors().Any(c => c.IsPublic));
    }
}
