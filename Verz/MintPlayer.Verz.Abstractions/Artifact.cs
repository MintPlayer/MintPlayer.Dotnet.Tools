namespace MintPlayer.Verz.Abstractions;

public sealed record Artifact(string Path, string Kind);

public static class ArtifactKinds
{
    public const string Nuget = "nuget";
    public const string NugetSymbols = "nuget-symbols";
    public const string Npm = "npm";
}
