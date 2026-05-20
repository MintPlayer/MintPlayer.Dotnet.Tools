using Microsoft.Extensions.FileSystemGlobbing;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace MintPlayer.Verz.Sdks.NodeJS;

/// <summary>
/// Resolves a list of workspace member directories under
/// <paramref name="repoRoot"/> using whichever convention is in use:
/// <c>package.json#workspaces</c>, <c>pnpm-workspace.yaml</c>, or
/// <c>nx.json#projects</c>. Returns absolute directory paths.
/// </summary>
internal static class WorkspaceDiscovery
{
    public static IReadOnlyList<string> ResolveMemberDirs(string repoRoot)
    {
        var rootPackageJson = Path.Combine(repoRoot, "package.json");
        if (File.Exists(rootPackageJson))
        {
            var reader = new PackageJsonReader(rootPackageJson);
            if (reader.Workspaces is { Count: > 0 } globs)
            {
                return ExpandGlobs(repoRoot, globs);
            }
        }

        var pnpmFile = Path.Combine(repoRoot, "pnpm-workspace.yaml");
        if (File.Exists(pnpmFile))
        {
            var pnpmGlobs = ReadPnpmWorkspace(pnpmFile);
            if (pnpmGlobs.Count > 0)
            {
                return ExpandGlobs(repoRoot, pnpmGlobs);
            }
        }

        var nxFile = Path.Combine(repoRoot, "nx.json");
        if (File.Exists(nxFile))
        {
            var nxProjects = ReadNxProjects(nxFile);
            if (nxProjects.Count > 0)
            {
                return nxProjects
                    .Select(p => Path.GetFullPath(Path.Combine(repoRoot, p)))
                    .Where(Directory.Exists)
                    .ToArray();
            }
        }

        // Single-package layout: repo root is itself a package, if it has a
        // package.json with a name. (Common for stand-alone npm libraries.)
        if (File.Exists(rootPackageJson))
        {
            try
            {
                var reader = new PackageJsonReader(rootPackageJson);
                if (!string.IsNullOrEmpty(reader.Name))
                {
                    return new[] { repoRoot };
                }
            }
            catch { /* not a usable package.json */ }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ExpandGlobs(string repoRoot, IEnumerable<string> globs)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var g in globs)
        {
            // Workspace globs typically point at directories. Append /package.json
            // so the glob matcher finds matching files we can map back to dirs.
            matcher.AddInclude(g.TrimEnd('/', '\\') + "/package.json");
        }

        var matches = matcher.Execute(
            new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(
                new DirectoryInfo(repoRoot)));

        return matches.Files
            .Select(f => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(repoRoot, f.Path)))!)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadPnpmWorkspace(string path)
    {
        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count == 0) return Array.Empty<string>();

        var root = yaml.Documents[0].RootNode as YamlMappingNode;
        if (root is null) return Array.Empty<string>();

        var packagesKey = root.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(k => string.Equals(k.Value, "packages", StringComparison.Ordinal));
        if (packagesKey is null) return Array.Empty<string>();

        if (root.Children[packagesKey] is not YamlSequenceNode seq) return Array.Empty<string>();

        return seq.Children
            .OfType<YamlScalarNode>()
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => s.Value!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadNxProjects(string path)
    {
        var text = File.ReadAllText(path);
        var node = JsonNode.Parse(text);
        if (node is not JsonObject obj) return Array.Empty<string>();

        // Modern Nx (16+): no "projects" key — projects are discovered via project.json
        // files. Fall back to scanning for project.json siblings of package.json.
        // Older Nx: "projects" object whose keys are workspace paths.
        if (obj["projects"] is JsonObject legacyProjects)
        {
            return legacyProjects.Select(kv => kv.Key).ToArray();
        }

        // Modern Nx: walk the workspace looking for project.json files
        // (limited depth to avoid traversing node_modules).
        var repoRoot = Path.GetDirectoryName(path)!;
        var found = new List<string>();
        var nodeModules = Path.Combine(repoRoot, "node_modules");
        foreach (var projectJson in Directory.EnumerateFiles(repoRoot, "project.json", SearchOption.AllDirectories))
        {
            if (projectJson.StartsWith(nodeModules, StringComparison.OrdinalIgnoreCase)) continue;
            var dir = Path.GetDirectoryName(projectJson);
            if (dir is not null) found.Add(Path.GetRelativePath(repoRoot, dir).Replace('\\', '/'));
        }
        return found;
    }
}
