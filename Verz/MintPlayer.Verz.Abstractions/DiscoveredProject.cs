namespace MintPlayer.Verz.Abstractions;

public sealed record DiscoveredProject
{
    public required string PackageId { get; init; }
    public required string ProjectDir { get; init; }
    public required string ProjectFile { get; init; }
    public required string OwnerSdkId { get; init; }
    public int? FrameworkMajor { get; init; }
}
