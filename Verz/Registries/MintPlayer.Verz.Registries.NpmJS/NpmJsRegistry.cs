using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using NuGet.Versioning;

namespace MintPlayer.Verz.Registries.NpmJS;

/// <summary>
/// IPackageRegistry plugin for npm registries. Lookup hits the registry's
/// REST endpoint (https://{host}/{packageId}/{version}) which returns the
/// published package.json blob. Push shells out to <c>npm publish</c> so
/// auth flows through the consumer-provided ~/.npmrc.
/// </summary>
public sealed class NpmJsRegistry(ILogger<NpmJsRegistry> logger) : IPackageRegistry
{
    private HttpClient? _http;
    private HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public string Kind => "npm";

    public IReadOnlyList<string> AcceptedKinds { get; } = new[] { ArtifactKinds.Npm };

    public bool CanHandle(string registryUrl)
    {
        if (string.IsNullOrWhiteSpace(registryUrl)) return false;

        // NuGet feeds end in index.json — let the NuGet plugin take those.
        if (registryUrl.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)) return false;

        if (registryUrl.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase)) return true;
        if (registryUrl.Contains("registry.yarnpkg.com", StringComparison.OrdinalIgnoreCase)) return true;
        if (registryUrl.Contains("npm.pkg.github.com", StringComparison.OrdinalIgnoreCase)) return true;

        // Other npm-flavored URLs need the npm-style HTTP path. Heuristic:
        // claim it only if "npm" appears in the URL — keeps us out of e.g.
        // local file dir feeds that the NuGet plugin handles.
        return registryUrl.Contains("npm", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PriorPackageInfo?> LookupAsync(
        string registryUrl, string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        // Scoped packages (@scope/name) are URL-encoded as @scope%2Fname on
        // the registry endpoint.
        var escaped = packageId.Replace("/", "%2F", StringComparison.Ordinal);
        var endpoint = $"{registryUrl.TrimEnd('/')}/{escaped}/{version.ToNormalizedString()}";

        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(endpoint, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "lookup {Endpoint} failed", endpoint);
            return null;
        }

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("lookup {Endpoint} returned {Status}", endpoint, (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(json) is not JsonObject obj) return null;

        var hash = obj["publicApiHash"]?.GetValue<string>();
        int? frameworkMajor = null;
        if (obj["frameworkMajor"] is JsonNode fm)
        {
            frameworkMajor = fm.GetValueKind() switch
            {
                JsonValueKind.Number => fm.GetValue<int>(),
                JsonValueKind.String => int.TryParse(fm.GetValue<string>(), out var v) ? v : null,
                _ => null,
            };
        }

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

        var psi = new ProcessStartInfo("npm")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(artifact.Path);
        psi.ArgumentList.Add("--registry");
        psi.ArgumentList.Add(registryUrl);
        // Scoped packages default to "restricted" without --access. Verz only
        // touches packages explicitly listed in verz.json so we assume the
        // user wants them published; their ~/.npmrc can override visibility
        // per scope if needed.
        psi.ArgumentList.Add("--access");
        psi.ArgumentList.Add("public");

        using var proc = Process.Start(psi)
            ?? throw new PublishFailureException("could not start npm; is Node.js installed and on PATH?");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(cancellationToken);

        logger.LogInformation("[{Kind}] pushed {File} -> {Url} (exit {Exit})",
            Kind, Path.GetFileName(artifact.Path), registryUrl, proc.ExitCode);

        if (proc.ExitCode != 0)
        {
            var combined = $"npm publish {Path.GetFileName(artifact.Path)} -> {registryUrl} " +
                $"failed (exit {proc.ExitCode}):\n{stderr}\n{stdout}";
            throw new PublishFailureException(combined.TrimEnd());
        }
    }
}
