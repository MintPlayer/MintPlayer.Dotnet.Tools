using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static readonly HashSet<string> HashExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".verz-types", "dist", "build", "out", "coverage", ".cache",
        ".nx", ".angular", ".next", ".nuxt", ".svelte-kit",
    };

    public Task<string> ComputePublicApiHashAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
    {
        // v1 hash: SHA-256 over (relative-path, file-bytes) for every file
        // under the project directory, excluding common build/cache dirs.
        // Stricter than a .d.ts-only hash (any source whitespace change moves
        // the hash) — favors a conservative MINOR bump over missing a real
        // surface change. Refinement to TypeScript .d.ts compilation can come
        // later without changing the contract.
        var files = Directory.EnumerateFiles(project.ProjectDir, "*", SearchOption.AllDirectories)
            .Select(f => (
                RelativePath: Path.GetRelativePath(project.ProjectDir, f).Replace('\\', '/'),
                FullPath: f))
            .Where(x => !x.RelativePath
                .Split('/')
                .Any(seg => HashExcludedDirs.Contains(seg)))
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();

        using var sha = SHA256.Create();
        var sep = new byte[] { 0 };
        foreach (var (rel, full) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = Encoding.UTF8.GetBytes(rel);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            sha.TransformBlock(sep, 0, 1, null, 0);
            var content = File.ReadAllBytes(full);
            sha.TransformBlock(content, 0, content.Length, null, 0);
            sha.TransformBlock(sep, 0, 1, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var hex = Convert.ToHexString(sha.Hash!);
        logger.LogDebug("[{Sdk}] {Pkg}: hashed {Count} files -> {Hash}",
            Id, project.PackageId, files.Length, hex);
        return Task.FromResult(hex);
    }

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

    public async Task<IReadOnlyList<Artifact>> PackAsync(
        DiscoveredProject project, string configuration, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetTempPath(), $"verz-npm-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);

        await RunNpmPackAsync(project.ProjectDir, output, cancellationToken);

        string? hash = null;
        try
        {
            hash = await ComputePublicApiHashAsync(project, configuration, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[{Sdk}] {Pkg}: could not compute public-API hash; package.json will not carry publicApiHash",
                Id, project.PackageId);
        }

        var artifacts = new List<Artifact>();
        foreach (var tgz in Directory.EnumerateFiles(output, "*.tgz"))
        {
            if (hash is not null)
            {
                try
                {
                    InjectPackageJsonMetadata(tgz, hash, project.FrameworkMajor);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[{Sdk}] {Pkg}: failed to inject metadata into {Path}; pack continues",
                        Id, project.PackageId, tgz);
                }
            }
            artifacts.Add(new Artifact(tgz, ArtifactKinds.Npm));
        }

        logger.LogInformation("[{Sdk}] {Pkg}: packed {Count} artifact(s) into {Dir}",
            Id, project.PackageId, artifacts.Count, output);

        return artifacts;
    }

    private static async Task RunNpmPackAsync(string projectDir, string output, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("npm")
        {
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add("--pack-destination");
        psi.ArgumentList.Add(output);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start npm; is Node.js installed and on PATH?");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"npm pack in {projectDir} failed (exit {proc.ExitCode}):\n{stdout}\n{stderr}");
        }
    }

    /// <summary>
    /// Extracts the tarball to a temp dir, edits <c>package/package.json</c>
    /// to add <c>publicApiHash</c> and <c>frameworkMajor</c>, then re-archives
    /// in place. npm tarballs nest everything under a top-level <c>package/</c>
    /// dir per the npm spec.
    /// </summary>
    private static void InjectPackageJsonMetadata(string tgzPath, string publicApiHash, int? frameworkMajor)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"verz-tgz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            using (var input = File.OpenRead(tgzPath))
            using (var gzipIn = new GZipStream(input, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzipIn, temp, overwriteFiles: true);
            }

            var packageJson = Path.Combine(temp, "package", "package.json");
            if (!File.Exists(packageJson)) return; // unexpected layout — skip silently

            var json = JsonNode.Parse(File.ReadAllText(packageJson))!.AsObject();
            json["publicApiHash"] = publicApiHash;
            if (frameworkMajor.HasValue)
            {
                json["frameworkMajor"] = frameworkMajor.Value;
            }
            File.WriteAllText(
                packageJson,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            File.Delete(tgzPath);
            using (var output = File.Create(tgzPath))
            using (var gzipOut = new GZipStream(output, CompressionLevel.Optimal))
            {
                TarFile.CreateFromDirectory(temp, gzipOut, includeBaseDirectory: false);
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}
