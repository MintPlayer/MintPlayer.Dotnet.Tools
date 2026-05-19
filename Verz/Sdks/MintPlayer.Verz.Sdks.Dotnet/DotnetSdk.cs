using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using PublicApiGenerator;

namespace MintPlayer.Verz.Sdks.Dotnet;

public sealed class DotnetSdk(ILogger<DotnetSdk> logger) : IDevelopmentSdk
{
    public string Id => "dotnet";

    public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        string repoRoot, CancellationToken cancellationToken)
    {
        var discovered = new List<DiscoveredProject>();

        foreach (var csproj in Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsInBinOrObj(csproj, repoRoot)) continue;

            CsprojReader reader;
            try
            {
                reader = new CsprojReader(csproj);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not read {Csproj}; skipping", csproj);
                continue;
            }

            if (!reader.IsPackable) continue;
            if (!reader.IsLibrary) continue;

            discovered.Add(new DiscoveredProject
            {
                PackageId = reader.PackageId,
                ProjectDir = Path.GetDirectoryName(csproj)!,
                ProjectFile = csproj,
                OwnerSdkId = Id,
                FrameworkMajor = TfmHelper.DetectMajorFromTargets(reader.TargetFrameworks),
            });
        }

        return Task.FromResult<IReadOnlyList<DiscoveredProject>>(discovered);
    }

    public Task<IReadOnlyList<string>> EnumerateInRepoDependenciesAsync(
        DiscoveredProject project,
        IReadOnlyDictionary<string, DiscoveredProject> repoIndex,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Dependency graph lands in milestone 4.");

    public Task<string> ComputePublicApiHashAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
    {
        var reader = new CsprojReader(project.ProjectFile);
        var tfm = reader.TargetFrameworks
            .Where(TfmHelper.IsNetTfm)
            .OrderByDescending(TfmHelper.ParseNetMajor)
            .ThenByDescending(TfmHelper.ParseNetMinor)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{project.PackageId}: no net*.* TargetFramework found; cannot hash public API");

        var assemblyPath = Path.Combine(
            project.ProjectDir, "bin", configuration, tfm, reader.AssemblyName + ".dll");

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"{project.PackageId}: build output not found at {assemblyPath}. " +
                "Run `dotnet build -c " + configuration + "` first.",
                assemblyPath);
        }

        // Copy the dll to a unique temp path before loading. The collectible
        // ALC below pins the loaded file until the ALC is unloaded *and* the
        // GC has collected the last reference. Copying first means the real
        // bin output is never pinned — a single `verz` invocation may rebuild
        // the same project mid-run (e.g., a transitive bump downstream).
        // PublicApiGenerator needs assembly.Location, so byte[] loads are out.
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"verz-hash-{Guid.NewGuid():N}-{Path.GetFileName(assemblyPath)}");
        File.Copy(assemblyPath, tempPath, overwrite: true);

        var (hex, weakAlc) = LoadAndHashIsolated(tempPath);

        // Drive the unload to completion. `AssemblyLoadContext.Unload` only
        // marks for unloading; the actual release happens after the GC sees
        // no live references. With NoInlining isolating the load-frame above,
        // 2-3 iterations are usually enough.
        for (int i = 0; i < 10 && weakAlc.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        try { File.Delete(tempPath); }
        catch (Exception ex)
        {
            // Unload didn't release the pin in 10 GC cycles; rare but possible
            // under heavy load. The OS will reclaim %TEMP% eventually.
            logger.LogDebug(ex, "could not delete hash temp file {Path}", tempPath);
        }

        logger.LogDebug("[{Sdk}] {PackageId} ({Tfm}): public-API SHA256 = {Hash}",
            Id, project.PackageId, tfm, hex);

        return Task.FromResult(hex);
    }

    /// <summary>
    /// Loads the assembly into its own collectible AssemblyLoadContext, runs
    /// PublicApiGenerator, SHA-256s the result, then unloads. The
    /// NoInlining attribute keeps the ALC + Assembly references confined to
    /// this method's stack frame so they actually become collectible when the
    /// method returns; without it the JIT can keep them alive on the caller's
    /// frame and the unload never completes.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Hash, WeakReference Alc) LoadAndHashIsolated(string assemblyPath)
    {
        var alc = new AssemblyLoadContext(name: $"verz-hash-{Guid.NewGuid():N}", isCollectible: true);
        var assembly = alc.LoadFromAssemblyPath(assemblyPath);
        var publicApi = ApiGenerator.GeneratePublicApi(assembly);

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(publicApi));
        var hex = Convert.ToHexString(hashBytes);

        alc.Unload();
        return (hex, new WeakReference(alc));
    }

    public Task StampVersionAsync(
        DiscoveredProject project, string version, CancellationToken cancellationToken)
    {
        var doc = XDocument.Load(project.ProjectFile, LoadOptions.PreserveWhitespace);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var versionElem = doc.Descendants(ns + "Version")
            .FirstOrDefault(e => e.Attribute("Condition") is null);

        if (versionElem is not null)
        {
            versionElem.Value = version;
        }
        else
        {
            // Insert into the first unconditional PropertyGroup, or create one.
            var propertyGroup = doc.Root?
                .Elements(ns + "PropertyGroup")
                .FirstOrDefault(pg => pg.Attribute("Condition") is null);

            if (propertyGroup is null)
            {
                propertyGroup = new XElement(ns + "PropertyGroup");
                doc.Root!.AddFirst(propertyGroup);
            }
            propertyGroup.Add(new XElement(ns + "Version", version));
        }

        doc.Save(project.ProjectFile, SaveOptions.DisableFormatting);
        logger.LogInformation("[{Sdk}] {PackageId} -> {ProjectFile}: {Version}",
            Id, project.PackageId, project.ProjectFile, version);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Artifact>> PackAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
        => throw new NotImplementedException("Packing lands in milestone 5.");

    private static bool IsInBinOrObj(string path, string repoRoot)
    {
        var relative = Path.GetRelativePath(repoRoot, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(seg =>
                seg.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
