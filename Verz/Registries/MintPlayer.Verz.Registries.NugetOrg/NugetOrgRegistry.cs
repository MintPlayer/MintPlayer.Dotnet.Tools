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

namespace MintPlayer.Verz.Registries.NugetOrg;

/// <summary>
/// IPackageRegistry plugin for NuGet v3 feeds. Handles nuget.org, any
/// authenticated v3 feed (auth via ~/.nuget/NuGet.config), and local
/// directory feeds. GitHub Packages has its own plugin so we refuse those
/// URLs to keep responsibilities clean.
/// </summary>
public sealed class NugetOrgRegistry(ILogger<NugetOrgRegistry> logger) : IPackageRegistry
{
    private readonly ConcurrentDictionary<string, SourceRepository> _repos = new(StringComparer.OrdinalIgnoreCase);

    public string Kind => "nuget";

    public IReadOnlyList<string> AcceptedKinds { get; } =
        new[] { ArtifactKinds.Nuget, ArtifactKinds.NugetSymbols };

    public bool CanHandle(string registryUrl)
    {
        if (string.IsNullOrWhiteSpace(registryUrl)) return false;

        // GitHub Packages has a dedicated plugin to deal with its auth quirks.
        if (registryUrl.Contains("pkg.github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        if (registryUrl.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Local directory feeds (a folder of .nupkg files).
        if (Directory.Exists(registryUrl)) return true;

        // file:// URLs.
        if (Uri.TryCreate(registryUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            return true;

        return false;
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
            logger.LogDebug(ex, "could not connect to {Source} for {Package}@{Version}",
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
            logger.LogDebug(ex, "lookup of {Package}@{Version} on {Source} failed",
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
            throw new PublishFailureException(
                $"artifact not found at {artifact.Path}");
        }

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
        // Symbols packages get pushed by name; main packages auto-detect.

        using var proc = Process.Start(psi)
            ?? throw new PublishFailureException("could not start dotnet; is the .NET SDK on PATH?");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(cancellationToken);

        logger.LogInformation("[{Kind}] pushed {File} -> {Url} (exit {ExitCode})",
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
