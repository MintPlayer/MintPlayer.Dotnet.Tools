using MintPlayer.SlnLaunch.Models;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Launches all projects in a <see cref="LaunchPlan"/> concurrently, multiplexes their output, and
/// guarantees that on cancellation every child process — and its whole tree — is torn down.
/// </summary>
public interface IProcessOrchestrator
{
    /// <summary>
    /// Starts every command, then waits until all exit or <paramref name="cancellationToken"/> fires.
    /// Returns 0 when everything exited cleanly or the user cancelled; otherwise the first non-zero exit
    /// code (or 1 if a process failed to start).
    /// </summary>
    Task<int> RunAsync(LaunchPlan plan, LaunchRunOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Builds every project in <paramref name="plan"/> sequentially (one <c>dotnet build</c> at a time) so the
    /// parallel run can use <c>--no-build</c> and never races on the MSBuild server pipe or shared output DLLs.
    /// Returns <c>true</c> when all builds succeed; <c>false</c> on the first failure or on cancellation.
    /// </summary>
    Task<bool> BuildAsync(LaunchPlan plan, LaunchBuildOptions options, CancellationToken cancellationToken);
}
