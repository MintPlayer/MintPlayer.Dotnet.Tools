using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Sdks.NodeJS;

public sealed class NodeJsSdk(ILogger<NodeJsSdk> logger) : IDevelopmentSdk
{
    public string Id => "nodejs";

    public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(
        string repoRoot, CancellationToken cancellationToken)
    {
        var memberDirs = WorkspaceDiscovery.ResolveMemberDirs(repoRoot);
        var discovered = new List<DiscoveredProject>();

        foreach (var dir in memberDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageJsonPath = Path.Combine(dir, "package.json");
            if (!File.Exists(packageJsonPath)) continue;

            PackageJsonReader reader;
            try
            {
                reader = new PackageJsonReader(packageJsonPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not parse {Path}; skipping", packageJsonPath);
                continue;
            }

            if (reader.Private) continue;
            if (string.IsNullOrEmpty(reader.Name)) continue;

            discovered.Add(new DiscoveredProject
            {
                PackageId = reader.Name!,
                ProjectDir = dir,
                ProjectFile = packageJsonPath,
                OwnerSdkId = Id,
                FrameworkMajor = FrameworkDetection.DetectMajor(
                    reader.Dependencies, reader.PeerDependencies),
            });
        }

        return Task.FromResult<IReadOnlyList<DiscoveredProject>>(discovered);
    }

    public Task<IReadOnlyList<string>> EnumerateInRepoDependenciesAsync(
        DiscoveredProject project,
        IReadOnlyDictionary<string, DiscoveredProject> repoIndex,
        CancellationToken cancellationToken)
    {
        var reader = new PackageJsonReader(project.ProjectFile);
        var edges = new HashSet<string>(StringComparer.Ordinal);

        AddInRepoMatches(reader.Dependencies, repoIndex, project.PackageId, edges);
        AddInRepoMatches(reader.DevDependencies, repoIndex, project.PackageId, edges);
        AddInRepoMatches(reader.PeerDependencies, repoIndex, project.PackageId, edges);

        return Task.FromResult<IReadOnlyList<string>>(edges.ToArray());
    }

    private static void AddInRepoMatches(
        IReadOnlyDictionary<string, string> deps,
        IReadOnlyDictionary<string, DiscoveredProject> repoIndex,
        string selfPackageId,
        HashSet<string> edges)
    {
        foreach (var depName in deps.Keys)
        {
            if (string.Equals(depName, selfPackageId, StringComparison.Ordinal)) continue;
            if (repoIndex.ContainsKey(depName))
            {
                edges.Add(depName);
            }
        }
    }

    public Task<string> ComputePublicApiHashAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
        => throw new NotImplementedException(".d.ts hashing lands in milestone 6b.");

    public Task StampVersionAsync(
        DiscoveredProject project, string version, CancellationToken cancellationToken)
    {
        var reader = new PackageJsonReader(project.ProjectFile);
        reader.Root["version"] = version;
        reader.Save();

        logger.LogInformation("[{Sdk}] {PackageId} -> {ProjectFile}: {Version}",
            Id, project.PackageId, project.ProjectFile, version);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Artifact>> PackAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
        => throw new NotImplementedException("npm pack lands in milestone 6b.");
}
