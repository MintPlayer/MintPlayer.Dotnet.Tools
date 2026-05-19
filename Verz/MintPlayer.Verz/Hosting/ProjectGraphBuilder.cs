using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Hosting;

internal sealed class ProjectGraphBuilder(ILogger<ProjectGraphBuilder> logger)
{
    public async Task<ProjectGraph> BuildAsync(
        IReadOnlyList<IDevelopmentSdk> sdks,
        string repoRoot,
        CancellationToken cancellationToken)
    {
        var discoveries = await Task.WhenAll(
            sdks.Select(s => s.DiscoverAsync(repoRoot, cancellationToken)));

        var index = new Dictionary<string, DiscoveredProject>(StringComparer.Ordinal);
        var ownerOf = new Dictionary<string, IDevelopmentSdk>(StringComparer.Ordinal);

        for (var i = 0; i < sdks.Count; i++)
        {
            var sdk = sdks[i];
            foreach (var project in discoveries[i])
            {
                if (!index.TryAdd(project.PackageId, project))
                {
                    throw new CycleException(new[]
                    {
                        $"{project.PackageId} (discovered by {ownerOf[project.PackageId].Id} and {sdk.Id})"
                    });
                }
                ownerOf[project.PackageId] = sdk;
            }
        }

        var nodes = new Dictionary<string, ProjectNode>(StringComparer.Ordinal);
        foreach (var (id, project) in index)
        {
            var sdk = ownerOf[id];
            var deps = await sdk.EnumerateInRepoDependenciesAsync(project, index, cancellationToken);

            nodes[id] = new ProjectNode
            {
                PackageId = project.PackageId,
                ProjectDir = project.ProjectDir,
                ProjectFile = project.ProjectFile,
                OwnerSdkId = project.OwnerSdkId,
                FrameworkMajor = project.FrameworkMajor,
                Dependencies = deps,
            };
        }

        var graph = new ProjectGraph(nodes);
        _ = graph.TopologicalOrder(); // throws CycleException if not a DAG

        logger.LogInformation("project graph: {NodeCount} projects, {EdgeCount} in-repo edges",
            nodes.Count, nodes.Values.Sum(n => n.Dependencies.Count(d => nodes.ContainsKey(d))));

        return graph;
    }
}
