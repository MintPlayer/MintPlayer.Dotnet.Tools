using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Helpers;
using MintPlayer.Verz.Hosting;

namespace MintPlayer.Verz.Commands;

internal sealed class PublishCommand(
    ILogger<PublishCommand> logger,
    VerzConfigProvider configProvider,
    PluginCatalogProvider catalogProvider,
    GitClient git)
{
    public async Task<int> HandleAsync(PublishOptions options, CancellationToken cancellationToken)
    {
        var cwd = Directory.GetCurrentDirectory();
        var config = configProvider.Require();
        var catalog = await catalogProvider.GetAsync(cancellationToken);

        if (catalog.Sdks.Count == 0)
        {
            Console.Error.WriteLine("no SDK plugins loaded; nothing to pack");
            return 4;
        }

        var rawTags = git.TagsPointingAt("HEAD", cwd);
        var tags = new List<SemverTag>();
        foreach (var raw in rawTags)
        {
            if (SemverTag.TryParse(raw, out var parsed)) tags.Add(parsed);
        }

        if (tags.Count == 0)
        {
            Console.WriteLine("No {PackageId}/v{semver} tags at HEAD; nothing to publish.");
            return 0;
        }

        // Build PackageId -> (sdk, project) once.
        var byPackageId = new Dictionary<string, (IDevelopmentSdk Sdk, DiscoveredProject Project)>(StringComparer.Ordinal);
        foreach (var sdk in catalog.Sdks)
        {
            foreach (var project in await sdk.DiscoverAsync(cwd, cancellationToken))
            {
                byPackageId.TryAdd(project.PackageId, (sdk, project));
            }
        }

        var publishPlans = new List<(SemverTag Tag, IDevelopmentSdk Sdk, DiscoveredProject Project)>();
        foreach (var tag in tags)
        {
            if (byPackageId.TryGetValue(tag.PackageId, out var match))
            {
                publishPlans.Add((tag, match.Sdk, match.Project));
            }
            else
            {
                logger.LogWarning("no project discovered for tag {Tag}; skipping", tag.TagName);
            }
        }

        if (publishPlans.Count == 0)
        {
            throw new NoArtifactsException();
        }

        // Filter Registries by --registry id flags, if any.
        var registryFilter = options.Registries is { Count: > 0 }
            ? new HashSet<string>(options.Registries, StringComparer.OrdinalIgnoreCase)
            : null;
        var activeRegistries = config.Registries
            .Where(r => registryFilter is null || registryFilter.Contains(r.Id))
            .ToList();

        if (activeRegistries.Count == 0)
        {
            logger.LogWarning("no active registries after filter; nothing will be pushed");
        }

        // Pack each tagged project.
        var allArtifacts = new List<(string Tag, Artifact Artifact)>();
        foreach (var (tag, sdk, project) in publishPlans)
        {
            Console.WriteLine($"Packing {tag.PackageId}@{tag.Version.ToNormalizedString()} via {sdk.Id}...");
            var artifacts = await sdk.PackAsync(project, options.Configuration, cancellationToken);
            foreach (var a in artifacts)
            {
                allArtifacts.Add((tag.TagName, a));
            }
        }

        if (allArtifacts.Count == 0)
        {
            throw new NoArtifactsException();
        }

        // Route artifacts to registries: each (artifact, registry) pair where
        // the plugin handles the URL and accepts the artifact's kind.
        var failures = new List<string>();
        var pushedCount = 0;

        foreach (var (tagName, artifact) in allArtifacts)
        {
            var matched = false;
            foreach (var registry in activeRegistries)
            {
                var plugin = catalog.Registries.FirstOrDefault(p =>
                    p.AcceptedKinds.Contains(artifact.Kind) && p.CanHandle(registry.Url));
                if (plugin is null) continue;

                Console.WriteLine($"  -> {registry.Id} ({plugin.Kind}): {Path.GetFileName(artifact.Path)}");
                matched = true;
                try
                {
                    await plugin.PushAsync(registry.Url, artifact, cancellationToken);
                    pushedCount++;
                }
                catch (PublishFailureException ex)
                {
                    var msg = $"{tagName} -> {registry.Id}: {ex.Message}";
                    failures.Add(msg);
                    logger.LogError("{Msg}", msg);
                }
            }

            if (!matched)
            {
                logger.LogWarning("no registry plugin accepts kind {Kind} for {Path}",
                    artifact.Kind, Path.GetFileName(artifact.Path));
            }
        }

        if (failures.Count > 0)
        {
            foreach (var f in failures) Console.Error.WriteLine(f);
            throw new PublishFailureException($"{failures.Count} push(es) failed");
        }

        Console.WriteLine($"\nPublished {pushedCount} artifact(s) to {activeRegistries.Count} registr(ies).");
        return 0;
    }
}

internal sealed record PublishOptions(
    string Configuration,
    IReadOnlyList<string>? Registries);
