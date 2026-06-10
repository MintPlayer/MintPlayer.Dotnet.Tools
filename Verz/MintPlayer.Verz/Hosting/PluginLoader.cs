using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Helpers;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace MintPlayer.Verz.Hosting;

internal sealed class PluginLoader(ILogger<PluginLoader> logger, IServiceProvider services)
{
    private static readonly string[] CandidateTfms = ["net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"];
    private static readonly NuGetFramework HostFramework = NuGetFramework.Parse("net10.0");

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
        // Include the nuspec in the extraction so WalkDependenciesAsync can
        // read each package's transitive dependency declarations.
        var extractionContext = new PackageExtractionContext(
            PackageSaveMode.Files | PackageSaveMode.Nuspec,
            XmlDocFileSaveMode.None,
            ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance),
            NullLogger.Instance);
        var sourceCacheContext = new SourceCacheContext();

        // Shared across plugins so transitive deps are downloaded once.
        var resolvedDeps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in config.Plugins)
        {
            var assembly = await ResolveAndLoadAsync(
                entry, sources, pathResolver, extractionContext, sourceCacheContext,
                resolvedDeps, cancellationToken);
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
                    logger.LogInformation("loaded registry plugin {Kind} ({Type})", instance.Kind, type.FullName);
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
        Dictionary<string, string> resolvedDeps,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            try
            {
                var installPath = await TryDownloadAsync(
                    source, entry.Id, entry.Version, versionRange: null,
                    pathResolver, extractionContext, sourceCacheContext, cancellationToken);
                if (installPath is null) continue;

                var assemblyPath = ResolveAssemblyPath(installPath, entry.Id);
                if (assemblyPath is null)
                {
                    logger.LogWarning("plugin {Id} has no lib/net*/{Id}.dll under {Path}",
                        entry.Id, entry.Id, installPath);
                    return null;
                }

                // Walk the plugin's transitive deps so PublicApiGenerator,
                // Mono.Cecil, and friends are extracted and on the search path
                // before we construct the ALC.
                var searchDirs = new List<string> { Path.GetDirectoryName(assemblyPath)! };
                resolvedDeps[entry.Id] = installPath;
                await WalkDependenciesAsync(
                    installPath, sources, pathResolver, extractionContext, sourceCacheContext,
                    resolvedDeps, searchDirs, cancellationToken);

                var alc = new PluginLoadContext(assemblyPath, searchDirs);
                return alc.LoadFromAssemblyPath(assemblyPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "plugin {Id} not found on {Source}",
                    entry.Id, source.PackageSource.Source);
            }
        }

        logger.LogError("plugin {Id} could not be resolved from any configured registry", entry.Id);
        return null;
    }

    /// <summary>
    /// Resolve a single package (the plugin itself or one of its deps) against
    /// <paramref name="source"/>: pick a version matching the pin or range,
    /// extract to ~/.nuget/packages if not already there, return the install
    /// directory. Returns null if the package isn't on this source.
    /// </summary>
    private async Task<string?> TryDownloadAsync(
        SourceRepository source,
        string packageId,
        string? pinnedVersion,
        VersionRange? versionRange,
        VersionPackagePathResolver pathResolver,
        PackageExtractionContext extractionContext,
        SourceCacheContext sourceCacheContext,
        CancellationToken ct)
    {
        var finder = await source.GetResourceAsync<FindPackageByIdResource>(ct);
        var allVersions = await finder.GetAllVersionsAsync(
            packageId, sourceCacheContext, NullLogger.Instance, ct);

        NuGetVersion? selected;
        if (pinnedVersion is not null)
        {
            selected = allVersions.FirstOrDefault(v => v.ToNormalizedString() == pinnedVersion);
        }
        else if (versionRange is not null)
        {
            // Prefer stable; fall back to any if no stable satisfies.
            selected = versionRange.FindBestMatch(allVersions.Where(v => !v.IsPrerelease))
                       ?? versionRange.FindBestMatch(allVersions);
        }
        else
        {
            selected = allVersions.Where(v => !v.IsPrerelease).OrderByDescending(v => v).FirstOrDefault();
            if (selected is not null)
            {
                logger.LogWarning(
                    "plugin {Id} is unpinned; using {Version} (latest stable)",
                    packageId, selected.ToNormalizedString());
            }
        }

        if (selected is null) return null;

        var identity = new PackageIdentity(packageId, selected);
        var installPath = pathResolver.GetInstallPath(identity);
        var nuspecAtRoot = Path.Combine(installPath, $"{packageId}.nuspec");

        if (!File.Exists(nuspecAtRoot) &&
            !Directory.Exists(installPath) ||
            !Directory.EnumerateFiles(installPath, "*.nuspec", SearchOption.TopDirectoryOnly).Any())
        {
            Directory.CreateDirectory(installPath);
            await using var stream = new MemoryStream();
            await finder.CopyNupkgToStreamAsync(
                packageId, selected, stream, sourceCacheContext, NullLogger.Instance, ct);
            stream.Position = 0;
            using var reader = new PackageArchiveReader(stream);
            await PackageExtractor.ExtractPackageAsync(
                source.PackageSource.Source, reader, pathResolver, extractionContext, ct);
        }

        return installPath;
    }

    /// <summary>
    /// Recursively resolve and extract every NuGet dependency in the nuspec
    /// at <paramref name="installPath"/>. Adds each dep's lib/{tfm}/ directory
    /// to <paramref name="searchDirs"/> so the ALC's Load can find it.
    /// Shared assemblies (Abstractions, Logging.Abstractions, NuGet.Versioning)
    /// are intentionally skipped — they resolve to the host's default context.
    /// </summary>
    private async Task WalkDependenciesAsync(
        string installPath,
        IReadOnlyList<SourceRepository> sources,
        VersionPackagePathResolver pathResolver,
        PackageExtractionContext extractionContext,
        SourceCacheContext sourceCacheContext,
        Dictionary<string, string> resolvedDeps,
        List<string> searchDirs,
        CancellationToken ct)
    {
        var nuspecPath = Directory.EnumerateFiles(installPath, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (nuspecPath is null) return;

        NuspecReader nuspec;
        using (var s = File.OpenRead(nuspecPath))
        {
            nuspec = new NuspecReader(s);
        }

        var depGroups = nuspec.GetDependencyGroups().ToList();
        if (depGroups.Count == 0) return;

        var reducer = new FrameworkReducer();
        var nearest = reducer.GetNearest(HostFramework, depGroups.Select(g => g.TargetFramework));
        if (nearest is null) return;
        var depPackages = depGroups
            .First(g => g.TargetFramework.Equals(nearest))
            .Packages;

        foreach (var dep in depPackages)
        {
            ct.ThrowIfCancellationRequested();
            if (PluginLoadContext.IsShared(dep.Id)) continue;
            if (resolvedDeps.ContainsKey(dep.Id))
            {
                AddLibDir(resolvedDeps[dep.Id], searchDirs);
                continue;
            }

            string? depInstallPath = null;
            foreach (var source in sources)
            {
                try
                {
                    depInstallPath = await TryDownloadAsync(
                        source, dep.Id, pinnedVersion: null, versionRange: dep.VersionRange,
                        pathResolver, extractionContext, sourceCacheContext, ct);
                    if (depInstallPath is not null) break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "dep {Id} not on {Source}", dep.Id, source.PackageSource.Source);
                }
            }

            if (depInstallPath is null)
            {
                logger.LogDebug("transitive dep {Id} {Range} not found in any source; assuming framework-provided",
                    dep.Id, dep.VersionRange);
                continue;
            }

            resolvedDeps[dep.Id] = depInstallPath;
            AddLibDir(depInstallPath, searchDirs);

            await WalkDependenciesAsync(
                depInstallPath, sources, pathResolver, extractionContext, sourceCacheContext,
                resolvedDeps, searchDirs, ct);
        }
    }

    private static void AddLibDir(string installPath, List<string> searchDirs)
    {
        foreach (var tfm in CandidateTfms)
        {
            var libDir = Path.Combine(installPath, "lib", tfm);
            if (Directory.Exists(libDir))
            {
                if (!searchDirs.Contains(libDir, StringComparer.OrdinalIgnoreCase))
                    searchDirs.Add(libDir);
                return;
            }
        }
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
            if (string.Equals(entry.Kind, "npm", StringComparison.OrdinalIgnoreCase)) continue;
            repos.Add(new SourceRepository(new PackageSource(entry.Url, entry.Id), providers));
        }

        // nuget.org fallback for transitive deps when verz.json only lists
        // private feeds. Plugin packages themselves still come from the user's
        // configured feeds; nuget.org only handles transitive resolution of
        // third-party deps that aren't mirrored on a private feed.
        if (!repos.Any(r => r.PackageSource.Source.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase)))
        {
            repos.Add(new SourceRepository(
                new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org-fallback"),
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
