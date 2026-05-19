using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Helpers;
using MintPlayer.Verz.Hosting;

namespace MintPlayer.Verz.Commands;

public sealed class SetVersionsCommand(
    ILogger<SetVersionsCommand> logger,
    PluginCatalogProvider catalogProvider,
    GitClient git)
{
    public async Task<int> HandleAsync(SetVersionsOptions options, CancellationToken cancellationToken)
    {
        var cwd = Directory.GetCurrentDirectory();
        var resolved = git.RevParse(options.Ref, cwd);
        var rawTags = git.TagsPointingAt(options.Ref, cwd);

        var parsedTags = new List<SemverTag>();
        foreach (var raw in rawTags)
        {
            if (SemverTag.TryParse(raw, out var tag)) parsedTags.Add(tag);
            else logger.LogDebug("ignoring non-conforming tag {Tag}", raw);
        }

        if (parsedTags.Count == 0)
        {
            throw new NoTagsAtRefException(resolved);
        }

        var grouped = parsedTags.GroupBy(t => t.PackageId, StringComparer.Ordinal).ToList();
        foreach (var group in grouped.Where(g => g.Count() > 1))
        {
            throw UnmatchedTagException.Duplicates(group.Key, resolved, group.Select(t => t.TagName));
        }

        Console.WriteLine($"HEAD = {resolved[..Math.Min(7, resolved.Length)]} (tags: {string.Join(", ", parsedTags.Select(t => t.TagName))})");

        var catalog = await catalogProvider.GetAsync(cancellationToken);
        if (catalog.Sdks.Count == 0)
        {
            throw UnmatchedTagException.NoSdks(resolved);
        }

        var allProjects = new Dictionary<string, (DiscoveredProject Project, IDevelopmentSdk Sdk)>(StringComparer.Ordinal);
        foreach (var sdk in catalog.Sdks)
        {
            var discovered = await sdk.DiscoverAsync(cwd, cancellationToken);
            foreach (var project in discovered)
            {
                if (!allProjects.TryAdd(project.PackageId, (project, sdk)))
                {
                    logger.LogWarning(
                        "{PackageId} discovered by multiple SDKs; first {First} won, {Second} ignored",
                        project.PackageId, allProjects[project.PackageId].Sdk.Id, sdk.Id);
                }
            }
        }

        var changed = 0;
        foreach (var tag in parsedTags)
        {
            if (!allProjects.TryGetValue(tag.PackageId, out var match))
            {
                throw UnmatchedTagException.ForPackageId(tag.PackageId, resolved);
            }

            var version = tag.Version.ToNormalizedString();
            if (options.DryRun)
            {
                Console.WriteLine($"[{match.Sdk.Id}] {tag.PackageId} -> {match.Project.ProjectFile}: {version} (dry-run)");
            }
            else
            {
                await match.Sdk.StampVersionAsync(match.Project, version, cancellationToken);
                Console.WriteLine($"[{match.Sdk.Id}] {tag.PackageId} -> {match.Project.ProjectFile}: {version}");
                changed++;
            }
        }

        Console.WriteLine(options.DryRun
            ? $"{parsedTags.Count} projects would be updated."
            : $"{changed} projects updated.");

        return 0;
    }
}

public sealed record SetVersionsOptions(string Ref, bool DryRun);
