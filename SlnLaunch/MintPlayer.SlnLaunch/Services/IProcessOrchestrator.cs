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
}
