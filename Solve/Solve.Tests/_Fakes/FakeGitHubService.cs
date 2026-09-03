using Solve.Models;
using Solve.Services;

namespace Solve.Tests._Fakes;

/// <summary>
/// An in-memory GitHub, keyed by issue number.
/// </summary>
/// <remarks>
/// Defaults to installed and authenticated, because almost every test is about what happens after
/// those checks pass. The two flags exist so the guard clauses at the top of each command — which
/// are the paths a real user hits first and the ones no other test would reach — can be exercised
/// deliberately.
/// </remarks>
internal sealed class FakeGitHubService : IGitHubService
{
    public bool IsInstalled { get; set; } = true;
    public bool IsAuthenticated { get; set; } = true;

    /// <summary>Issues this fake knows about, by number.</summary>
    public Dictionary<int, GitHubIssue> Issues { get; } = [];

    public (int Number, string Title, string Url)? PullRequestForBranch { get; set; }
    public string? CreatedPullRequestUrl { get; set; } = "https://github.com/o/r/pull/1";
    public string? ReviewsJson { get; set; }
    public string? FileContents { get; set; }
    public bool UpdateIssueBodySucceeds { get; set; } = true;

    /// <summary>The body handed to the last <see cref="UpdateIssueBodyAsync"/>.</summary>
    public string? UpdatedIssueBody { get; private set; }

    /// <summary>Arguments of the last <see cref="CreatePullRequestAsync"/>.</summary>
    public (string Title, string Body, string BaseBranch, bool Draft)? CreatedPullRequest { get; private set; }

    /// <summary>Registers an issue and returns it, so a test can adjust it inline.</summary>
    public GitHubIssue AddIssue(int number, string title = "Something is broken", params string[] labels)
    {
        var issue = new GitHubIssue
        {
            Number = number,
            Title = title,
            Body = "Body",
            State = "OPEN",
            Author = "someone",
            Labels = [.. labels],
            Url = $"https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/{number}",
        };

        Issues[number] = issue;
        return issue;
    }

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(IsInstalled);

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(IsAuthenticated);

    public Task<GitHubIssue?> GetIssueAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
        => GetIssueAsync(number, cancellationToken);

    public Task<GitHubIssue?> GetIssueAsync(int number, CancellationToken cancellationToken = default)
        => Task.FromResult(Issues.TryGetValue(number, out var issue) ? issue : null);

    public Task<bool> UpdateIssueBodyAsync(string owner, string repo, int number, string body, CancellationToken cancellationToken = default)
    {
        UpdatedIssueBody = body;
        return Task.FromResult(UpdateIssueBodySucceeds);
    }

    public Task<(int Number, string Title, string Url)?> GetPullRequestForBranchAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(PullRequestForBranch);

    public Task<string?> CreatePullRequestAsync(string title, string body, string baseBranch, bool draft = false, CancellationToken cancellationToken = default)
    {
        CreatedPullRequest = (title, body, baseBranch, draft);
        return Task.FromResult(CreatedPullRequestUrl);
    }

    public Task<string?> GetPullRequestReviewsAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(ReviewsJson);

    public Task<string?> GetFileContentsAsync(string owner, string repo, string path, CancellationToken cancellationToken = default)
        => Task.FromResult(FileContents);
}
