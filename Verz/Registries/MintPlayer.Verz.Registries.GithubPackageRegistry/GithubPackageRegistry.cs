using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace MintPlayer.Verz.Registries.GithubPackageRegistry;

/// <summary>
/// IPackageRegistry plugin specialised for GitHub Packages NuGet feeds
/// (https://nuget.pkg.github.com/{owner}/index.json). The protocol surface
/// is identical to nuget.org, but GitHub Packages has auth quirks: most
/// reads require a bearer token, even for public feeds, and unauthenticated
/// lookups return 403 instead of an empty result. Verz tolerates that by
/// treating a failed lookup as "not found" — the planner moves to the next
/// configured registry.
/// </summary>
public sealed class GithubPackageRegistry(ILogger<GithubPackageRegistry> logger) : IPackageRegistry
{
    private readonly ConcurrentDictionary<string, SourceRepository> _repos = new(StringComparer.OrdinalIgnoreCase);

    public string Kind => "nuget";

    public IReadOnlyList<string> AcceptedKinds { get; } =
        new[] { ArtifactKinds.Nuget, ArtifactKinds.NugetSymbols };

    public bool CanHandle(string registryUrl)
    {
        if (string.IsNullOrWhiteSpace(registryUrl)) return false;
        return registryUrl.Contains("nuget.pkg.github.com", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PriorPackageInfo?> LookupAsync(
        string registryUrl, string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        var repo = _repos.GetOrAdd(registryUrl,
            u => new SourceRepository(new PackageSource(u), Repository.Provider.GetCoreV3()));

        FindPackageByIdResource finder;
        try
        {
            finder = await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "could not connect to {Source} for {Package}@{Version} (likely missing auth)",
                registryUrl, packageId, version);
            return null;
        }

        using var cache = new SourceCacheContext();
        using var ms = new MemoryStream();

        bool found;
        try
        {
            found = await finder.CopyNupkgToStreamAsync(
                packageId, version, ms, cache, NullLogger.Instance, cancellationToken);
        }
        catch (Exception ex)
        {
            // GitHub Packages typically surfaces auth failures as 403/401
            // wrapped in HttpRequestException or FatalProtocolException. Treat
            // those as "not present" so the planner falls through to the next
            // configured registry rather than failing the whole run.
            logger.LogDebug(ex,
                "lookup of {Package}@{Version} on {Source} failed",
                packageId, version, registryUrl);
            return null;
        }

        if (!found) return null;

        ms.Position = 0;
        using var reader = new PackageArchiveReader(ms);
        using var nuspec = reader.GetNuspec();

        var doc = XDocument.Load(nuspec);
        var metadata = doc.Root?
            .Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "metadata", StringComparison.OrdinalIgnoreCase));
        if (metadata is null) return null;

        var hash = ReadCustom(metadata, "PublicApiHash");
        var frameworkMajorRaw = ReadCustom(metadata, "FrameworkMajor");
        int? frameworkMajor = int.TryParse(frameworkMajorRaw, out var fm) ? fm : null;

        return new PriorPackageInfo
        {
            PublicApiHash = hash,
            FrameworkMajor = frameworkMajor,
        };
    }

    public async Task PushAsync(string registryUrl, Artifact artifact, CancellationToken cancellationToken)
    {
        if (!File.Exists(artifact.Path))
        {
            throw new PublishFailureException($"artifact not found at {artifact.Path}");
        }

        // Defer to `dotnet nuget push` so credentials in
        // ~/.nuget/NuGet.config's <packageSourceCredentials> are picked up
        // automatically — that's how GitHub Packages auth has to flow for
        // CI runs that set GITHUB_TOKEN via the workflow.
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(artifact.Path);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(registryUrl);
        psi.ArgumentList.Add("--skip-duplicate");
        // GitHub Packages rejects symbols pushes — emit a hint when we see
        // one. The caller can route symbols to nuget.org instead.
        if (string.Equals(artifact.Kind, ArtifactKinds.NugetSymbols, StringComparison.Ordinal))
        {
            psi.ArgumentList.Add("--no-symbols");
        }

        using var proc = Process.Start(psi)
            ?? throw new PublishFailureException("could not start dotnet; is the .NET SDK on PATH?");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(cancellationToken);

        logger.LogInformation("[{Kind}/github] pushed {File} -> {Url} (exit {Exit})",
            Kind, Path.GetFileName(artifact.Path), registryUrl, proc.ExitCode);

        if (proc.ExitCode != 0)
        {
            var combined = $"dotnet nuget push {Path.GetFileName(artifact.Path)} -> {registryUrl} " +
                $"failed (exit {proc.ExitCode}):\n{stderr}\n{stdout}";
            throw new PublishFailureException(combined.TrimEnd());
        }
    }

    private static string? ReadCustom(XElement metadata, string localName) =>
        metadata.Elements()
            .FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim();
}
