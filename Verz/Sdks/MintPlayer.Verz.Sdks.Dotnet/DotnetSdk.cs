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
    private static readonly string HashTempRoot = InitHashTempRoot();

    // Two-layer cleanup for the temp dll copies that PublicApiGenerator needs
    // a real Assembly.Location for. (1) ProcessExit attempts to wipe this
    // run's subdir; (2) the next run sweeps orphan {pid} subdirs from any
    // prior process that exited without successful cleanup (testhost being
    // the typical offender — it holds onto loaded assemblies until full
    // shutdown, past our ProcessExit handler).
    private static string InitHashTempRoot()
    {
        var tempBase = Path.GetTempPath();
        var currentPid = Environment.ProcessId;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(tempBase, "verz-hash-*"))
            {
                var pidPart = Path.GetFileName(dir);
                if (!pidPart.StartsWith("verz-hash-", StringComparison.Ordinal)) continue;
                if (!int.TryParse(pidPart["verz-hash-".Length..], out var pid)) continue;
                if (pid == currentPid) continue;

                if (ProcessExists(pid)) continue;

                try { Directory.Delete(dir, recursive: true); }
                catch { /* still locked or already gone; try again next run */ }
            }
        }
        catch { /* enumeration races are tolerable */ }

        var own = Path.Combine(tempBase, $"verz-hash-{currentPid}");
        Directory.CreateDirectory(own);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(own, recursive: true); }
            catch { /* next run's orphan sweep will get it */ }
        };
        return own;
    }

    private static bool ProcessExists(int pid)
    {
        try { System.Diagnostics.Process.GetProcessById(pid); return true; }
        catch (ArgumentException) { return false; }
        catch { return true; /* assume alive on permission/other errors */ }
    }

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

        // Copy to a unique temp path before loading. Two reasons:
        //   (1) Assembly.LoadFrom pins the file. We don't want the actual bin
        //       output pinned because a single `verz` invocation may rebuild
        //       the same project (e.g., for downstream transitive bumps).
        //   (2) PublicApiGenerator needs assembly.Location to be set, so we
        //       can't load from a byte[].
        // The temp copies live under a per-process subdir wiped on ProcessExit
        // (see HashTempRoot). Each hash also lives in its own collectible
        // AssemblyLoadContext so repeat calls with the same logical assembly
        // name don't collide.
        var tempPath = Path.Combine(
            HashTempRoot,
            $"{Guid.NewGuid():N}-{Path.GetFileName(assemblyPath)}");
        File.Copy(assemblyPath, tempPath, overwrite: true);

        var alc = new AssemblyLoadContext($"verz-hash-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var assembly = alc.LoadFromAssemblyPath(tempPath);
            var publicApi = ApiGenerator.GeneratePublicApi(assembly);

            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(publicApi));
            var hex = Convert.ToHexString(hashBytes);

            logger.LogDebug("[{Sdk}] {PackageId} ({Tfm}): public-API SHA256 = {Hash}",
                Id, project.PackageId, tfm, hex);

            return Task.FromResult(hex);
        }
        finally
        {
            alc.Unload();
        }
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
