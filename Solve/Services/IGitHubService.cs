using Solve.Models;

namespace Solve.Services;

/// <summary>
/// Service for GitHub operations via the gh CLI.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Checks if the gh CLI is installed.
    /// </summary>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the gh CLI is installed and authenticated.
    /// </summary>
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets issue details.
    /// </summary>
    Task<GitHubIssue?> GetIssueAsync(string owner, string repo, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets issue details using just the number (from current repo context).
    /// </summary>
    Task<GitHubIssue?> GetIssueAsync(int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an issue's body.
    /// </summary>
    Task<bool> UpdateIssueBodyAsync(string owner, string repo, int number, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the PR associated with the current branch.
    /// </summary>
    Task<(int Number, string Title, string Url)?> GetPullRequestForBranchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a pull request.
    /// </summary>
    Task<string?> CreatePullRequestAsync(string title, string body, string baseBranch, bool draft = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unresolved review threads for a PR.
    /// </summary>
    Task<string?> GetPullRequestReviewsAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the contents of a file from a GitHub repository.
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <param name="path">Path to the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File contents or null if not found</returns>
    Task<string?> GetFileContentsAsync(string owner, string repo, string path, CancellationToken cancellationToken = default);
}
