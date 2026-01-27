using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.Solve.Services;

[Register(typeof(IGitService), ServiceLifetime.Scoped, "SolveServices")]
public partial class GitService : IGitService
{
    public async Task<string?> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync("rev-parse --abbrev-ref HEAD", cancellationToken);
        return result.Success ? result.Output?.Trim() : null;
    }

    public async Task<string?> GetDefaultBranchAsync(CancellationToken cancellationToken = default)
    {
        // Try to get from remote HEAD
        var result = await RunGitAsync("symbolic-ref refs/remotes/origin/HEAD --short", cancellationToken);
        if (result.Success && !string.IsNullOrEmpty(result.Output))
        {
            // Returns something like "origin/main" - extract just the branch name
            var branch = result.Output.Trim();
            if (branch.StartsWith("origin/"))
                return branch["origin/".Length..];
            return branch;
        }

        // Fallback: check common default branch names
        var commonDefaults = new[] { "main", "master", "development", "develop" };
        foreach (var defaultBranch in commonDefaults)
        {
            if (await BranchExistsAsync(defaultBranch, cancellationToken))
                return defaultBranch;
        }

        return "main"; // Ultimate fallback
    }

    public async Task<bool> BranchExistsAsync(string branchName, CancellationToken cancellationToken = default)
    {
        // Check local
        var localResult = await RunGitAsync($"show-ref --verify --quiet refs/heads/{branchName}", cancellationToken);
        if (localResult.Success)
            return true;

        // Check remote
        var remoteResult = await RunGitAsync($"ls-remote --heads origin {branchName}", cancellationToken);
        return remoteResult.Success && !string.IsNullOrWhiteSpace(remoteResult.Output);
    }

    public async Task<List<string>> FindBranchesAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var branches = new List<string>();

        // Search all branches (local and remote)
        var result = await RunGitAsync("branch -a", cancellationToken);
        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return branches;

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var branch = line.Trim().TrimStart('*').Trim();
            if (branch.StartsWith("remotes/origin/"))
                branch = branch["remotes/origin/".Length..];

            if (regex.IsMatch(branch) && !branches.Contains(branch))
                branches.Add(branch);
        }

        return branches;
    }

    public async Task<bool> CheckoutAsync(string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync($"checkout {branchName}", cancellationToken);
        return result.Success;
    }

    public async Task<bool> CreateBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync($"checkout -b {branchName}", cancellationToken);
        return result.Success;
    }

    public async Task<bool> FetchAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync("fetch origin", cancellationToken);
        return result.Success;
    }

    public async Task<bool> PullAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync("pull", cancellationToken);
        return result.Success;
    }

    public async Task<bool> PushAsync(bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        var args = setUpstream ? "push -u origin HEAD" : "push";
        var result = await RunGitAsync(args, cancellationToken);
        return result.Success;
    }

    public async Task<(string? Owner, string? Repo)> GetRemoteInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync("remote get-url origin", cancellationToken);
        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return (null, null);

        var url = result.Output.Trim();

        // Parse SSH format: git@github.com:owner/repo.git
        var sshMatch = SshRemoteRegex().Match(url);
        if (sshMatch.Success)
        {
            return (sshMatch.Groups["owner"].Value, sshMatch.Groups["repo"].Value.TrimSuffix(".git"));
        }

        // Parse HTTPS format: https://github.com/owner/repo.git
        var httpsMatch = HttpsRemoteRegex().Match(url);
        if (httpsMatch.Success)
        {
            return (httpsMatch.Groups["owner"].Value, httpsMatch.Groups["repo"].Value.TrimSuffix(".git"));
        }

        return (null, null);
    }

    public async Task<bool> HasUncommittedChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync("status --porcelain", cancellationToken);
        return result.Success && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<string?> GetLogAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync($"log {baseRef}..{headRef} --oneline", cancellationToken);
        return result.Success ? result.Output : null;
    }

    public async Task<string?> GetDiffAsync(string baseRef, string headRef = "HEAD", CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync($"diff {baseRef}...{headRef}", cancellationToken);
        return result.Success ? result.Output : null;
    }

    private static async Task<(bool Success, string? Output)> RunGitAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return (process.ExitCode == 0, output);
        }
        catch
        {
            return (false, null);
        }
    }

    [GeneratedRegex(@"git@github\.com:(?<owner>[^/]+)/(?<repo>.+)")]
    private static partial Regex SshRemoteRegex();

    [GeneratedRegex(@"https://github\.com/(?<owner>[^/]+)/(?<repo>.+)")]
    private static partial Regex HttpsRemoteRegex();
}

internal static class StringExtensions
{
    public static string TrimSuffix(this string str, string suffix)
    {
        return str.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? str[..^suffix.Length]
            : str;
    }
}
