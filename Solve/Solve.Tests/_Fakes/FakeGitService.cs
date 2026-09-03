using Solve.Services;

namespace Solve.Tests._Fakes;

/// <summary>
/// An in-memory git, with every answer settable and every mutating call recorded.
/// </summary>
/// <remarks>
/// Defaults describe the ordinary case — a clean checkout of <c>master</c> on a repo the commands
/// can read — so a test only states the part it is actually about.
///
/// <see cref="Calls"/> records the mutating operations in order. Ordering genuinely matters for
/// these commands: checking out the default branch before pulling, and pulling before branching,
/// is the difference between a branch off current origin and a branch off whatever was lying
/// around.
/// </remarks>
internal sealed class FakeGitService : IGitService
{
    public List<string> Calls { get; } = [];

    public string? CurrentBranch { get; set; } = "master";
    public string? DefaultBranch { get; set; } = "master";
    public bool BranchExists { get; set; }
    public List<string> MatchingBranches { get; set; } = [];
    public bool CheckoutSucceeds { get; set; } = true;
    public bool CreateBranchSucceeds { get; set; } = true;
    public bool PushSucceeds { get; set; } = true;
    public bool HasUncommittedChanges { get; set; }
    public (string? Owner, string? Repo) RemoteInfo { get; set; } = ("MintPlayer", "MintPlayer.Dotnet.Tools");
    public string? Log { get; set; } = "";
    public string? Diff { get; set; } = "";

    /// <summary>The branch handed to the last successful <see cref="CreateBranchAsync"/>.</summary>
    public string? CreatedBranch { get; private set; }

    /// <summary>Every branch checked out, in order.</summary>
    public List<string> CheckedOut { get; } = [];

    public Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentBranch);

    public Task<string?> GetDefaultBranchAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DefaultBranch);

    public Task<bool> BranchExistsAsync(string branchName, CancellationToken cancellationToken = default)
        => Task.FromResult(BranchExists);

    public Task<List<string>> FindBranchesAsync(string pattern, CancellationToken cancellationToken = default)
        => Task.FromResult(MatchingBranches);

    public Task<bool> CheckoutAsync(string branchName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"checkout {branchName}");
        CheckedOut.Add(branchName);
        if (CheckoutSucceeds) CurrentBranch = branchName;
        return Task.FromResult(CheckoutSucceeds);
    }

    public Task<bool> CreateBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"branch {branchName}");
        if (CreateBranchSucceeds)
        {
            CreatedBranch = branchName;
            CurrentBranch = branchName;
        }
        return Task.FromResult(CreateBranchSucceeds);
    }

    public Task<bool> FetchAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("fetch");
        return Task.FromResult(true);
    }

    public Task<bool> PullAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("pull");
        return Task.FromResult(true);
    }

    public Task<bool> PushAsync(bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        Calls.Add(setUpstream ? "push -u" : "push");
        return Task.FromResult(PushSucceeds);
    }

    public Task<(string? Owner, string? Repo)> GetRemoteInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(RemoteInfo);

    public Task<bool> HasUncommittedChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasUncommittedChanges);

    public Task<string?> GetLogAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default)
        => Task.FromResult(Log);

    public Task<string?> GetDiffAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default)
        => Task.FromResult(Diff);
}
