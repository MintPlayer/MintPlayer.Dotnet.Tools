using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using Solve.Models;
using Solve.Services;

namespace Solve.Commands;

[CliCommand("work", Description = "Start or continue working on an issue")]
[CliParentCommand(typeof(SolveCommand))]
public partial class WorkCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;
    [Inject] private readonly IPrdGenerator _prdGenerator;
    [Inject] private readonly IClaudeService _claude;

    [CliArgument(0, Name = "issue-url", Required = false, Description = "GitHub issue URL or reference (detects from branch if omitted)"), NoInterfaceMember]
    public string? IssueUrl { get; set; }

    [CliOption("--status-only", Description = "Only show status, don't launch Claude"), NoInterfaceMember]
    public bool StatusOnly { get; set; }

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
            if (issueNumber.HasValue)
            {
                _console.WriteInfo($"Detected issue #{issueNumber} from branch '{currentBranch}'");
            }
        }

        if (!issueNumber.HasValue)
        {
            _console.WriteError("Error: Could not determine issue number.");
            _console.WriteLine("Please provide an issue URL/reference or ensure you're on an issue branch.");
            _console.WriteLine("Usage: solve work <issue-url>");
            _console.WriteLine("       solve work  (when on issues/123-description branch)");
            return 1;
        }

        // Check for required files
        var hasPrd = _prdGenerator.PrdExists(issueNumber.Value);
        var hasPlan = _prdGenerator.PlanExists(issueNumber.Value);

        if (!hasPrd || !hasPlan)
        {
            _console.WriteError($"The development plan and/or PRD for Issue #{issueNumber} could not be found.");
            _console.WriteLine();
            _console.WriteLine("Missing files:");
            if (!hasPlan)
                _console.WriteLine($"  - {_prdGenerator.GetPlanPath(issueNumber.Value)}");
            if (!hasPrd)
                _console.WriteLine($"  - {_prdGenerator.GetPrdPath(issueNumber.Value)}");
            _console.WriteLine();
            _console.WriteLine("Please run 'solve prd' first to create the development plan and PRD.");
            return 1;
        }

        // Fetch issue details
        var issue = owner != null && repo != null
            ? await _github.GetIssueAsync(owner, repo, issueNumber.Value, cancellationToken)
            : await _github.GetIssueAsync(issueNumber.Value, cancellationToken);

        var issueTitle = issue?.Title ?? $"Issue #{issueNumber}";

        // Parse and display status
        var status = await _prdGenerator.ParseWorkStatusAsync(issueNumber.Value, issueTitle, cancellationToken);

        if (status != null)
        {
            DisplayStatus(status);
        }

        // Check for uncommitted changes
        if (await _git.HasUncommittedChangesAsync(cancellationToken))
        {
            _console.WriteWarning("\nNote: You have uncommitted changes.");
        }

        // Check for open PR
        var pr = await _github.GetPullRequestForBranchAsync(cancellationToken);
        if (pr.HasValue)
        {
            _console.WriteInfo($"\nOpen PR: #{pr.Value.Number} - {pr.Value.Title}");
            _console.WriteLine($"  {pr.Value.Url}");
        }

        if (StatusOnly)
        {
            return 0;
        }

        // Launch Claude
        _console.WriteLine();
        if (await _claude.IsAvailableAsync(cancellationToken))
        {
            _console.WriteInfo("Launching Claude Code...");
            if (issue != null)
            {
                await _claude.LaunchForIssueAsync(
                    issue,
                    _prdGenerator.GetPlanPath(issueNumber.Value),
                    _prdGenerator.GetPrdPath(issueNumber.Value),
                    cancellationToken);
            }
        }
        else
        {
            _console.WriteWarning("Claude CLI not found.");
            _console.WriteLine("You can start Claude manually with:");
            _console.WriteLine($"  PRD: {_prdGenerator.GetPrdPath(issueNumber.Value)}");
            _console.WriteLine($"  Plan: {_prdGenerator.GetPlanPath(issueNumber.Value)}");
        }

        return 0;
    }

    private void DisplayStatus(WorkStatus status)
    {
        _console.WriteHeader($"\n## Issue #{status.IssueNumber}: {status.IssueTitle}");
        _console.WriteLine();
        _console.WriteLine($"**PRD Status**: {status.PrdStatus}");
        _console.WriteLine($"**Implementation Status**: {status.ImplementationStatus}");
        _console.WriteLine();

        _console.WriteLine("### Progress Summary");
        _console.WriteLine($"- Functional Requirements: {status.CompletedRequirements} of {status.TotalRequirements} completed");
        _console.WriteLine();

        if (status.CompletedItems.Count > 0)
        {
            _console.WriteSuccess("### Completed");
            foreach (var item in status.CompletedItems.Take(10))
            {
                _console.WriteLine($"- [x] {item}");
            }
            if (status.CompletedItems.Count > 10)
                _console.WriteLine($"  ... and {status.CompletedItems.Count - 10} more");
            _console.WriteLine();
        }

        if (status.RemainingItems.Count > 0)
        {
            _console.WriteLine("### Remaining Work");
            foreach (var item in status.RemainingItems.Take(10))
            {
                _console.WriteLine($"- [ ] {item}");
            }
            if (status.RemainingItems.Count > 10)
                _console.WriteLine($"  ... and {status.RemainingItems.Count - 10} more");
            _console.WriteLine();
        }

        if (status.OpenQuestions.Count > 0)
        {
            _console.WriteWarning("### Open Questions");
            foreach (var question in status.OpenQuestions)
            {
                _console.WriteLine($"- {question}");
            }
            _console.WriteLine();
        }

        if (status.Blockers.Count > 0)
        {
            _console.WriteError("### Blockers");
            foreach (var blocker in status.Blockers)
            {
                _console.WriteLine($"- {blocker}");
            }
            _console.WriteLine();
        }
    }
}
