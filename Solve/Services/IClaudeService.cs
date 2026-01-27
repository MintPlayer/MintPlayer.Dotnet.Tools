using Solve.Models;

namespace Solve.Services;

/// <summary>
/// Service for interacting with Claude Code CLI.
/// </summary>
public interface IClaudeService
{
    /// <summary>
    /// Checks if the Claude CLI is available.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches Claude Code with context for working on an issue.
    /// </summary>
    Task<bool> LaunchForIssueAsync(GitHubIssue issue, string? planPath, string? prdPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches Claude Code with a custom prompt.
    /// </summary>
    Task<bool> LaunchWithPromptAsync(string prompt, CancellationToken cancellationToken = default);
}
