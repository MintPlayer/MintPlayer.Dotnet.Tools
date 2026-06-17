namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Options for the sequential build phase that runs before the parallel launch. Mirrors the build-related
/// knobs forwarded to each project so the up-front <c>dotnet build</c> matches the eventual <c>dotnet run</c>.
/// </summary>
public sealed class LaunchBuildOptions
{
    /// <summary>Build configuration (e.g. <c>Debug</c>/<c>Release</c>); <c>null</c> uses the SDK default.</summary>
    public string? Configuration { get; init; }

    /// <summary>Target framework to build; <c>null</c> builds the project's single/default framework.</summary>
    public string? Framework { get; init; }

    /// <summary>MSBuild verbosity for the build output; <c>null</c> uses the SDK default.</summary>
    public string? Verbosity { get; init; }

    /// <summary>Don't prefix build output with the project label.</summary>
    public bool NoPrefix { get; init; }
}
