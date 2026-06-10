using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Helpers;
using NuGet.Versioning;

namespace MintPlayer.Verz.Hosting;

internal sealed class VersionPlanner(
    GitClient git,
    ILogger<VersionPlanner> logger)
{
    public async Task<IReadOnlyDictionary<string, TagPlan>> PlanAsync(
        ProjectGraph graph,
        IReadOnlyList<RegistryWithPlugin> registries,
        IReadOnlyDictionary<string, IDevelopmentSdk> sdksById,
        string repoRoot,
        string configuration,
        CancellationToken cancellationToken)
    {
        var topo = graph.TopologicalOrder();
        var plans = new Dictionary<string, TagPlan>(StringComparer.Ordinal);

        foreach (var node in topo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = await PlanNodeAsync(
                node, plans, registries, sdksById, repoRoot, configuration, cancellationToken);
            if (plan is not null) plans[node.PackageId] = plan;
        }

        return plans;
    }

    private async Task<TagPlan?> PlanNodeAsync(
        ProjectNode node,
        IReadOnlyDictionary<string, TagPlan> plansSoFar,
        IReadOnlyList<RegistryWithPlugin> registries,
        IReadOnlyDictionary<string, IDevelopmentSdk> sdksById,
        string repoRoot,
        string configuration,
        CancellationToken ct)
    {
        var priorTag = FindPriorTag(node.PackageId, repoRoot);

        bool sourceChanged;
        if (priorTag is null)
        {
            sourceChanged = true; // never tagged -> all current source counts as new
        }
        else
        {
            var relative = Path.GetRelativePath(repoRoot, node.ProjectDir).Replace('\\', '/');
            sourceChanged = git.HasChanges(priorTag.TagName, relative, repoRoot);
        }

        var depBumps = node.Dependencies
            .Select(d => plansSoFar.TryGetValue(d, out var p) ? p.BumpLevel : BumpLevel.None)
            .Where(b => b != BumpLevel.None)
            .ToList();

        if (!sourceChanged && depBumps.Count == 0)
        {
            return null; // SKIP: nothing changed for this lib or any of its in-repo deps
        }

        if (priorTag is null)
        {
            var initial = new NuGetVersion(node.FrameworkMajor ?? 0, 0, 0);
            logger.LogInformation("[plan] {Package}: no prior tag, fx {Fx}. INITIAL -> {Version}",
                node.PackageId, node.FrameworkMajor, initial.ToNormalizedString());
            return new TagPlan
            {
                PackageId = node.PackageId,
                Node = node,
                BumpLevel = BumpLevel.Initial,
                NewVersion = initial,
                PriorTag = null,
                SourceChanged = sourceChanged,
                DepDriven = false,
            };
        }

        var priorPkg = await LookupPriorAsync(node.PackageId, priorTag.Version, registries, ct);
        if (priorPkg is null)
        {
            throw new ColdStartException(node.PackageId, priorTag.Version.ToNormalizedString());
        }

        // Framework-major rule wins outright when it fires.
        if (node.FrameworkMajor is int currentFx && priorPkg.FrameworkMajor is int priorFx)
        {
            if (currentFx < priorFx)
            {
                throw new FrameworkDowngradeException(node.PackageId, priorFx, currentFx);
            }
            if (currentFx > priorFx)
            {
                var bumped = new NuGetVersion(currentFx, 0, 0);
                logger.LogInformation("[plan] {Package}: fx {Prior} -> {Current}. MAJOR -> {Version}",
                    node.PackageId, priorFx, currentFx, bumped.ToNormalizedString());
                return new TagPlan
                {
                    PackageId = node.PackageId,
                    Node = node,
                    BumpLevel = BumpLevel.Major,
                    NewVersion = bumped,
                    PriorTag = priorTag,
                    SourceChanged = sourceChanged,
                    DepDriven = false,
                };
            }
        }

        var sourceBump = BumpLevel.None;
        string? currentHash = null;
        if (sourceChanged)
        {
            // Compute the current public-API hash to decide MINOR vs PATCH.
            // Caller is expected to have built the project first (via the
            // CI workflow). If bin output is missing we treat the hash as
            // unknown and conservatively bump MINOR — the registry-stored
            // hash isn't there to compare against.
            try
            {
                if (sdksById.TryGetValue(node.OwnerSdkId, out var sdk))
                {
                    var disc = ToDiscoveredProject(node);
                    currentHash = await sdk.ComputePublicApiHashAsync(disc, configuration, ct);
                }
            }
            catch (FileNotFoundException ex)
            {
                logger.LogWarning(
                    "[plan] {Package}: build output missing — cannot compute current hash ({Msg}). " +
                    "Assuming MINOR bump conservatively.",
                    node.PackageId, ex.Message);
                currentHash = null;
            }

            sourceBump = (currentHash is not null
                          && string.Equals(currentHash, priorPkg.PublicApiHash, StringComparison.OrdinalIgnoreCase))
                ? BumpLevel.Patch
                : BumpLevel.Minor;
        }

        var depBump = depBumps.Count > 0 ? depBumps.Max() : BumpLevel.None;

        // Demotion: a dep bumped MINOR but our own public surface is unchanged
        // -> consumer only needs PATCH (per PRD transitive bump rule).
        if (depBump == BumpLevel.Minor && sourceBump != BumpLevel.Minor)
        {
            depBump = BumpLevel.Patch;
        }

        var effectiveBump = (BumpLevel)Math.Max((int)sourceBump, (int)depBump);
        var newVersion = ApplyBump(priorTag.Version, effectiveBump);
        var depDriven = depBump > sourceBump;

        logger.LogInformation(
            "[plan] {Package}: prior {Prior}, source {Source}, deps {Deps}, current hash {Hash}. {Bump} -> {Next}",
            node.PackageId,
            priorTag.Version.ToNormalizedString(),
            sourceChanged ? "changed" : "unchanged",
            depBumps.Count == 0 ? "no-change" : string.Join("/", depBumps),
            currentHash is null ? "n/a" : currentHash[..Math.Min(8, currentHash.Length)],
            effectiveBump,
            newVersion.ToNormalizedString());

        return new TagPlan
        {
            PackageId = node.PackageId,
            Node = node,
            BumpLevel = effectiveBump,
            NewVersion = newVersion,
            PriorTag = priorTag,
            SourceChanged = sourceChanged,
            DepDriven = depDriven,
        };
    }

    private SemverTag? FindPriorTag(string packageId, string repoRoot)
    {
        var raw = git.TagList($"{packageId}/v*", mergedHead: true, repoRoot);
        SemverTag? best = null;
        foreach (var t in raw)
        {
            if (!SemverTag.TryParse(t, out var parsed)) continue;
            if (best is null || parsed.Version > best.Version) best = parsed;
        }
        return best;
    }

    private async Task<PriorPackageInfo?> LookupPriorAsync(
        string packageId, NuGetVersion version,
        IReadOnlyList<RegistryWithPlugin> registries, CancellationToken ct)
    {
        foreach (var rwp in registries)
        {
            try
            {
                var info = await rwp.Plugin.LookupAsync(rwp.Entry.Url, packageId, version, ct);
                if (info is not null) return info;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "lookup of {Package}@{Version} on {Url} failed",
                    packageId, version, rwp.Entry.Url);
            }
        }
        return null;
    }

    private static NuGetVersion ApplyBump(NuGetVersion prior, BumpLevel level) => level switch
    {
        BumpLevel.Major => new NuGetVersion(prior.Major + 1, 0, 0),
        BumpLevel.Minor => new NuGetVersion(prior.Major, prior.Minor + 1, 0),
        BumpLevel.Patch => new NuGetVersion(prior.Major, prior.Minor, prior.Patch + 1),
        BumpLevel.Initial => prior,    // unreachable: INITIAL only fires when prior is null
        _ => prior,                    // BumpLevel.None: should not happen here
    };

    private static DiscoveredProject ToDiscoveredProject(ProjectNode node) => new()
    {
        PackageId = node.PackageId,
        ProjectDir = node.ProjectDir,
        ProjectFile = node.ProjectFile,
        OwnerSdkId = node.OwnerSdkId,
        FrameworkMajor = node.FrameworkMajor,
    };
}
