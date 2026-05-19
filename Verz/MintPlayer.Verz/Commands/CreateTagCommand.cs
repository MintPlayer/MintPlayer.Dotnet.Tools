using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Helpers;
using MintPlayer.Verz.Hosting;

namespace MintPlayer.Verz.Commands;

internal sealed class CreateTagCommand(
    ILogger<CreateTagCommand> logger,
    VerzConfigProvider configProvider,
    PluginCatalogProvider catalogProvider,
    ProjectGraphBuilder graphBuilder,
    VersionPlanner planner,
    GitClient git)
{
    public async Task<int> HandleAsync(CreateTagOptions options, CancellationToken cancellationToken)
    {
        var cwd = Directory.GetCurrentDirectory();
        var config = configProvider.Require();
        var catalog = await catalogProvider.GetAsync(cancellationToken);

        if (catalog.Sdks.Count == 0)
        {
            Console.Error.WriteLine("no SDK plugins loaded; cannot discover projects");
            return 4;
        }

        var graph = await graphBuilder.BuildAsync(catalog.Sdks, cwd, cancellationToken);
        if (graph.Nodes.Count == 0)
        {
            Console.WriteLine("No tags created (0 packages discovered).");
            return 0;
        }

        // For each verz.json Registries entry, pick the first loaded plugin
        // that says it CanHandle the URL.
        var registries = new List<RegistryWithPlugin>();
        foreach (var entry in config.Registries)
        {
            var plugin = catalog.Registries.FirstOrDefault(p => p.CanHandle(entry.Url));
            if (plugin is null)
            {
                logger.LogWarning(
                    "no registry plugin handles {Url} ({Id}); prior-package lookups will skip this feed",
                    entry.Url, entry.Id);
                continue;
            }
            registries.Add(new RegistryWithPlugin(entry, plugin));
        }

        var sdkById = catalog.Sdks.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);

        Console.WriteLine($"Graph: {graph.Nodes.Count} projects, {graph.Nodes.Values.Sum(n => n.Dependencies.Count)} edges, {registries.Count} active registries.");

        var plans = await planner.PlanAsync(
            graph, registries, sdkById, cwd, options.Configuration, cancellationToken);

        // Affected and skipped tallies.
        var skipped = graph.Nodes.Keys.Where(id => !plans.ContainsKey(id)).ToList();

        // Print one summary line per discovered project so the user sees the full picture.
        foreach (var node in graph.TopologicalOrder())
        {
            if (plans.TryGetValue(node.PackageId, out var plan))
            {
                Console.WriteLine($"  {node.PackageId,-50} {plan.BumpLevel,-7} -> {plan.NewVersion.ToNormalizedString()}");
            }
            else
            {
                Console.WriteLine($"  {node.PackageId,-50} SKIP");
            }
        }

        if (options.DryRun)
        {
            Console.WriteLine($"\n[dry-run] {plans.Count} tag(s) would be created, {skipped.Count} skipped.");
            return 0;
        }

        if (plans.Count == 0)
        {
            Console.WriteLine($"\nNo tags created ({skipped.Count} packages skipped, all unchanged).");
            return 0;
        }

        foreach (var plan in plans.Values)
        {
            git.CreateTag(plan.TagName, cwd);
            logger.LogInformation("created tag {Tag}", plan.TagName);
        }

        if (options.Push)
        {
            git.PushTags(cwd, options.Remote);
            Console.WriteLine($"Created {plans.Count} tags. Pushed to {options.Remote}.");
        }
        else
        {
            Console.WriteLine($"Created {plans.Count} tags locally.");
        }

        return 0;
    }
}

internal sealed record CreateTagOptions(
    bool DryRun,
    bool Push,
    string Remote,
    string Configuration);
