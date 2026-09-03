using Solve.Models;
using Solve.Services;

namespace Solve.Tests._Fakes;

/// <summary>
/// Stands in for launching the Claude CLI, recording what it would have been launched with.
/// </summary>
/// <remarks>
/// <see cref="IsAvailable"/> defaults true so the happy path needs no setup; setting it false
/// exercises the "not installed, carry on without it" branch, which must never fail the command —
/// the setup work it did beforehand is still valid.
/// </remarks>
internal sealed class FakeClaudeService : IClaudeService
{
    public bool IsAvailable { get; set; } = true;
    public bool LaunchSucceeds { get; set; } = true;

    public (GitHubIssue Issue, string? PlanPath, string? PrdPath)? LaunchedForIssue { get; private set; }
    public List<string> Prompts { get; } = [];

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(IsAvailable);

    public Task<bool> LaunchForIssueAsync(GitHubIssue issue, string? planPath, string? prdPath, CancellationToken cancellationToken = default)
    {
        LaunchedForIssue = (issue, planPath, prdPath);
        return Task.FromResult(LaunchSucceeds);
    }

    public Task<bool> LaunchWithPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Prompts.Add(prompt);
        return Task.FromResult(LaunchSucceeds);
    }
}
