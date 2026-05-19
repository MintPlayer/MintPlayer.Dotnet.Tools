using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;

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
        => throw new NotImplementedException("Public-API-hash lands in milestone 3.");

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
