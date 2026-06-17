namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Runtime options for <see cref="IProcessOrchestrator"/>.
/// </summary>
public sealed class LaunchRunOptions
{
    /// <summary>Don't prefix child output with the project label.</summary>
    public bool NoPrefix { get; init; }

    /// <summary>If any project exits non-zero, tear down the rest.</summary>
    public bool KillOnFail { get; init; }

    /// <summary>
    /// How long to wait for processes to exit on their own (e.g. after a console Ctrl+C they may already
    /// be shutting down) before force-killing their process trees.
    /// </summary>
    public TimeSpan GraceTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
