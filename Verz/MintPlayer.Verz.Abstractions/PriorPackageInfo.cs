namespace MintPlayer.Verz.Abstractions;

public sealed record PriorPackageInfo
{
    public string? PublicApiHash { get; init; }
    public int? FrameworkMajor { get; init; }
}
