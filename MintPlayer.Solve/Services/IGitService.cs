namespace MintPlayer.Solve.Services;

/// <summary>
/// Service for Git operations.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default branch name (main, master, development, etc.).
    /// </summary>
    Task<string?> GetDefaultBranchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a branch exists locally or remotely.
    /// </summary>
    Task<bool> BranchExistsAsync(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds branches matching a pattern.
    /// </summary>
    Task<List<string>> FindBranchesAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to a branch.
    /// </summary>
    Task<bool> CheckoutAsync(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new branch and switches to it.
    /// </summary>
    Task<bool> CreateBranchAsync(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches from remote.
    /// </summary>
    Task<bool> FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls latest changes.
    /// </summary>
    Task<bool> PullAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes to remote with upstream tracking.
    /// </summary>
    Task<bool> PushAsync(bool setUpstream = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the remote URL to extract owner/repo.
    /// </summary>
    Task<(string? Owner, string? Repo)> GetRemoteInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there are uncommitted changes.
    /// </summary>
    Task<bool> HasUncommittedChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the commit log between two refs.
    /// </summary>
    Task<string?> GetLogAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the diff between two refs.
    /// </summary>
    Task<string?> GetDiffAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default);
}
