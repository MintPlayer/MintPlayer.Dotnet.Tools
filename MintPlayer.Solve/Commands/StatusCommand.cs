using System.Text.Json;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;
using MintPlayer.Solve.Services;

namespace MintPlayer.Solve.Commands;

[CliCommand("status", Description = "Show work status for an issue")]
[CliParentCommand(typeof(SolveCommand))]
public partial class StatusCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;
    [Inject] private readonly IPrdGenerator _prdGenerator;

    [CliArgument(0, Name = "issue-url", Required = false, Description = "GitHub issue URL or reference (detects from branch if omitted)"), NoInterfaceMember]
    public string? IssueUrl { get; set; }

    [CliOption("--json", Description = "Output as JSON"), NoInterfaceMember]
    public bool JsonOutput { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        // Check prerequisites
        if (!await _github.IsInstalledAsync(cancellationToken))
        {
            _console.WriteGhInstallInstructions();
            return 1;
        }

        if (!await _github.IsAuthenticatedAsync(cancellationToken))
        {
            _console.WriteGhAuthInstructions();
            return 1;
        }

        var (owner, repo) = await _git.GetRemoteInfoAsync(cancellationToken);
        var currentBranch = await _git.GetCurrentBranchAsync(cancellationToken);
        int? issueNumber = null;

        // Try to get issue number from argument or branch
        if (!string.IsNullOrEmpty(IssueUrl))
        {
            var issueRef = IssueReference.Parse(IssueUrl, owner, repo);
            issueNumber = issueRef?.Number;
        }
        else if (!string.IsNullOrEmpty(currentBranch))
        {
            issueNumber = IssueReference.ExtractFromBranchName(currentBranch);
        }

        if (!issueNumber.HasValue)
        {
            if (JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = "Could not determine issue number" }));
            }
            else
            {
                _console.WriteError("Error: Could not determine issue number.");
                _console.WriteLine("Please provide an issue URL/reference or ensure you're on an issue branch.");
            }
            return 1;
        }

        // Fetch issue details
        var issue = owner != null && repo != null
            ? await _github.GetIssueAsync(owner, repo, issueNumber.Value, cancellationToken)
            : await _github.GetIssueAsync(issueNumber.Value, cancellationToken);

        var issueTitle = issue?.Title ?? $"Issue #{issueNumber}";

        // Parse status from PRD
        var status = await _prdGenerator.ParseWorkStatusAsync(issueNumber.Value, issueTitle, cancellationToken);

        if (status == null)
        {
            status = new WorkStatus
            {
                IssueNumber = issueNumber.Value,
                IssueTitle = issueTitle,
                PrdStatus = "Not Created",
                ImplementationStatus = "Not Started"
            };
        }

        // Check for uncommitted changes
        status.HasUncommittedChanges = await _git.HasUncommittedChangesAsync(cancellationToken);

        // Check for open PR
        var pr = await _github.GetPullRequestForBranchAsync(cancellationToken);
        if (pr.HasValue)
        {
            status.OpenPrUrl = pr.Value.Url;
        }

        if (JsonOutput)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(status, options));
        }
        else
        {
            DisplayStatus(status);
        }

        return 0;
    }

    private void DisplayStatus(WorkStatus status)
    {
        _console.WriteHeader($"\n## Issue #{status.IssueNumber}: {status.IssueTitle}");
        _console.WriteLine();
        _console.WriteLine($"**PRD Status**: {status.PrdStatus}");
        _console.WriteLine($"**Implementation Status**: {status.ImplementationStatus}");

        if (status.TotalRequirements > 0)
        {
            _console.WriteLine();
            _console.WriteLine("### Progress Summary");
            _console.WriteLine($"- Requirements: {status.CompletedRequirements}/{status.TotalRequirements} ({status.CompletionPercentage}%)");

            // Progress bar
            var filled = status.CompletionPercentage / 5;
            var empty = 20 - filled;
            _console.WriteLine($"  [{"".PadRight(filled, '#')}{"".PadRight(empty, '-')}]");
        }

        if (status.CompletedItems.Count > 0)
        {
            _console.WriteLine();
            _console.WriteSuccess($"### Completed ({status.CompletedItems.Count})");
            foreach (var item in status.CompletedItems.Take(5))
            {
                _console.WriteLine($"  - {item}");
            }
            if (status.CompletedItems.Count > 5)
                _console.WriteLine($"  ... and {status.CompletedItems.Count - 5} more");
        }

        if (status.RemainingItems.Count > 0)
        {
            _console.WriteLine();
            _console.WriteLine($"### Remaining ({status.RemainingItems.Count})");
            foreach (var item in status.RemainingItems.Take(5))
            {
                _console.WriteLine($"  - {item}");
            }
            if (status.RemainingItems.Count > 5)
                _console.WriteLine($"  ... and {status.RemainingItems.Count - 5} more");
        }

        if (status.OpenQuestions.Count > 0)
        {
            _console.WriteLine();
            _console.WriteWarning($"### Open Questions ({status.OpenQuestions.Count})");
            foreach (var q in status.OpenQuestions)
            {
                _console.WriteLine($"  - {q}");
            }
        }

        if (status.HasUncommittedChanges)
        {
            _console.WriteLine();
            _console.WriteWarning("Note: Uncommitted changes detected");
        }

        if (!string.IsNullOrEmpty(status.OpenPrUrl))
        {
            _console.WriteLine();
            _console.WriteInfo($"Open PR: {status.OpenPrUrl}");
        }

        _console.WriteLine();
    }
}
