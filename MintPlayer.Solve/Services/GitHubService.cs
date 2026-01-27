using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;

namespace MintPlayer.Solve.Services;

[Register(typeof(IGitHubService), ServiceLifetime.Scoped, "SolveServices")]
public class GitHubService : IGitHubService
{
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync("--version", cancellationToken);
        return result.Success;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync("auth status", cancellationToken);
        return result.Success;
    }

    public async Task<GitHubIssue?> GetIssueAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(
            $"issue view {number} --repo {owner}/{repo} --json number,title,body,state,author,labels,url",
            cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            var issue = new GitHubIssue
            {
                Number = root.GetProperty("number").GetInt32(),
                Title = root.GetProperty("title").GetString() ?? string.Empty,
                Body = root.GetProperty("body").GetString() ?? string.Empty,
                State = root.GetProperty("state").GetString() ?? string.Empty,
                Url = root.GetProperty("url").GetString() ?? string.Empty
            };

            if (root.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login))
            {
                issue.Author = login.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    if (label.TryGetProperty("name", out var name))
                    {
                        issue.Labels.Add(name.GetString() ?? string.Empty);
                    }
                }
            }

            return issue;
        }
        catch
        {
            return null;
        }
    }

    public async Task<GitHubIssue?> GetIssueAsync(int number, CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync(
            $"issue view {number} --json number,title,body,state,author,labels,url",
            cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            var issue = new GitHubIssue
            {
                Number = root.GetProperty("number").GetInt32(),
                Title = root.GetProperty("title").GetString() ?? string.Empty,
                Body = root.GetProperty("body").GetString() ?? string.Empty,
                State = root.GetProperty("state").GetString() ?? string.Empty,
                Url = root.GetProperty("url").GetString() ?? string.Empty
            };

            if (root.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login))
            {
                issue.Author = login.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    if (label.TryGetProperty("name", out var name))
                    {
                        issue.Labels.Add(name.GetString() ?? string.Empty);
                    }
                }
            }

            return issue;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateIssueBodyAsync(string owner, string repo, int number, string body, CancellationToken cancellationToken = default)
    {
        // Write body to temp file to avoid escaping issues
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, body, cancellationToken);
            var result = await RunGhAsync(
                $"issue edit {number} --repo {owner}/{repo} --body-file \"{tempFile}\"",
                cancellationToken);
            return result.Success;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    public async Task<(int Number, string Title, string Url)?> GetPullRequestForBranchAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunGhAsync("pr view --json number,title,url", cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            return (
                root.GetProperty("number").GetInt32(),
                root.GetProperty("title").GetString() ?? string.Empty,
                root.GetProperty("url").GetString() ?? string.Empty
            );
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> CreatePullRequestAsync(string title, string body, string baseBranch, bool draft = false, CancellationToken cancellationToken = default)
    {
        // Write body to temp file to avoid escaping issues
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, body, cancellationToken);

            var draftFlag = draft ? "--draft" : "";
            var result = await RunGhAsync(
                $"pr create --title \"{EscapeQuotes(title)}\" --body-file \"{tempFile}\" --base {baseBranch} {draftFlag}",
                cancellationToken);

            return result.Success ? result.Output?.Trim() : null;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    public async Task<string?> GetPullRequestReviewsAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
    {
        // Use GraphQL to get unresolved review threads
        var query = """
            query($owner: String!, $repo: String!, $pr: Int!) {
              repository(owner: $owner, name: $repo) {
                pullRequest(number: $pr) {
                  reviews(first: 50, states: [CHANGES_REQUESTED, COMMENTED]) {
                    nodes {
                      author { login }
                      state
                      body
                    }
                  }
                  reviewThreads(first: 100) {
                    nodes {
                      isResolved
                      path
                      line
                      comments(first: 50) {
                        nodes {
                          author { login }
                          body
                          createdAt
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        var result = await RunGhAsync(
            $"api graphql -f query='{EscapeForShell(query)}' -f owner={owner} -f repo={repo} -F pr={prNumber}",
            cancellationToken);

        return result.Success ? result.Output : null;
    }

    public async Task<string?> GetFileContentsAsync(string owner, string repo, string path, CancellationToken cancellationToken = default)
    {
        // Use gh api to get file contents
        var result = await RunGhAsync(
            $"api repos/{owner}/{repo}/contents/{path} --jq .content",
            cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            // GitHub returns base64 encoded content
            var base64Content = result.Output.Trim().Replace("\n", "");
            var bytes = Convert.FromBase64String(base64Content);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(bool Success, string? Output)> RunGhAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
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

    private static string EscapeQuotes(string input) => input.Replace("\"", "\\\"");

    private static string EscapeForShell(string input) =>
        input.Replace("'", "'\\''").Replace("\r\n", " ").Replace("\n", " ");
}
