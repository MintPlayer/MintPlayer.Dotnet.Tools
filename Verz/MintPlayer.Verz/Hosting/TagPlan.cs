using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Configuration;
using NuGet.Versioning;

namespace MintPlayer.Verz.Hosting;

internal sealed record TagPlan
{
    public required string PackageId { get; init; }
    public required ProjectNode Node { get; init; }
    public required BumpLevel BumpLevel { get; init; }
    public required NuGetVersion NewVersion { get; init; }
    public SemverTag? PriorTag { get; init; }
    public bool SourceChanged { get; init; }
    public bool DepDriven { get; init; }

    public string TagName => $"{PackageId}/v{NewVersion.ToNormalizedString()}";
}

internal sealed record RegistryWithPlugin(RegistryEntry Entry, IPackageRegistry Plugin);
