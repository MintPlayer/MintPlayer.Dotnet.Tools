using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using Solve.Models;
using Solve.Services;

namespace Solve.Commands;

[CliCommand("prd", Description = "Generate PRD and development plan for an issue")]
[CliParentCommand(typeof(SolveCommand))]
public partial class PrdCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;
    [Inject] private readonly IPrdGenerator _prdGenerator;

    [CliArgument(0, Name = "issue-url", Required = false, Description = "GitHub issue URL or reference (detects from branch if omitted)"), NoInterfaceMember]
    public string? IssueUrl { get; set; }

    [CliOption("--force", "-f", Description = "Overwrite existing PRD/plan"), NoInterfaceMember]
    public bool Force { get; set; }

    [CliOption("--update-issue", Description = "Update GitHub issue with clarifications"), NoInterfaceMember]
    public bool UpdateIssue { get; set; }

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
        int? issueNumber = null;

        // Try to get issue number from argument or branch
        if (!string.IsNullOrEmpty(IssueUrl))
        {
            var issueRef = IssueReference.Parse(IssueUrl, owner, repo);
            issueNumber = issueRef?.Number;
        }
        else
        {
            // Try to detect from branch name
            var currentBranch = await _git.GetCurrentBranchAsync(cancellationToken);
            if (!string.IsNullOrEmpty(currentBranch))
            {
                issueNumber = IssueReference.ExtractFromBranchName(currentBranch);
                if (issueNumber.HasValue)
                {
                    _console.WriteInfo($"Detected issue #{issueNumber} from branch '{currentBranch}'");
                }
            }
        }

        if (!issueNumber.HasValue)
        {
            _console.WriteError("Error: Could not determine issue number.");
            _console.WriteLine("Please provide an issue URL/reference or ensure you're on an issue branch.");
            _console.WriteLine("Usage: solve prd <issue-url>");
            _console.WriteLine("       solve prd  (when on issues/123-description branch)");
            return 1;
        }

        // Check if PRD/plan already exist
        if (!Force)
        {
            if (_prdGenerator.PrdExists(issueNumber.Value))
            {
                _console.WriteWarning($"PRD already exists: {_prdGenerator.GetPrdPath(issueNumber.Value)}");
                if (!_console.Confirm("Overwrite?"))
                    return 1;
            }
            if (_prdGenerator.PlanExists(issueNumber.Value))
            {
                _console.WriteWarning($"Plan already exists: {_prdGenerator.GetPlanPath(issueNumber.Value)}");
                if (!_console.Confirm("Overwrite?"))
                    return 1;
            }
        }

        // Fetch issue details
        _console.WriteInfo($"Fetching issue #{issueNumber}...");
        var issue = owner != null && repo != null
            ? await _github.GetIssueAsync(owner, repo, issueNumber.Value, cancellationToken)
            : await _github.GetIssueAsync(issueNumber.Value, cancellationToken);

        if (issue == null)
        {
            _console.WriteError($"Error: Could not fetch issue #{issueNumber}");
            return 1;
        }

        _console.WriteLine($"  Title: {issue.Title}");
        _console.WriteLine($"  Type: {issue.GetIssueType()}");
        _console.WriteLine($"  Priority: {issue.GetPriority()}");

        // Analyze issue clarity
        var issueClear = AnalyzeIssueClarity(issue);
        if (!issueClear)
        {
            _console.WriteWarning("\nThe issue description may be unclear or incomplete.");
            _console.WriteLine("Consider adding:");
            _console.WriteLine("  - Clear problem statement");
            _console.WriteLine("  - Expected behavior");
            _console.WriteLine("  - Acceptance criteria");

            if (UpdateIssue && _console.Confirm("Would you like to update the issue description?"))
            {
                var clarification = _console.Prompt("Enter additional context to add");
                if (!string.IsNullOrEmpty(clarification))
                {
                    var newBody = issue.Body + "\n\n---\n\n**Additional Context:**\n" + clarification;
                    if (owner != null && repo != null)
                    {
                        await _github.UpdateIssueBodyAsync(owner, repo, issueNumber.Value, newBody, cancellationToken);
                        _console.WriteSuccess("Issue updated.");
                        issue.Body = newBody;
                    }
                }
            }
        }

        // Generate development plan
        _console.WriteInfo("\nGenerating development plan...");
        var planContent = await _prdGenerator.GeneratePlanAsync(issue, cancellationToken);
        var planPath = await _prdGenerator.SavePlanAsync(issueNumber.Value, planContent, force: true, cancellationToken);
        _console.WriteSuccess($"Plan saved to: {planPath}");

        // Generate PRD
        _console.WriteInfo("Generating PRD...");
        var prdContent = await _prdGenerator.GeneratePrdAsync(issue, cancellationToken);
        var prdPath = await _prdGenerator.SavePrdAsync(issueNumber.Value, prdContent, force: true, cancellationToken);
        _console.WriteSuccess($"PRD saved to: {prdPath}");

        _console.WriteLine("\nNext steps:");
        _console.WriteLine("  1. Review and refine the generated PRD and plan");
        _console.WriteLine("  2. Run 'solve work' to start implementation with Claude Code");

        return 0;
    }

    private static bool AnalyzeIssueClarity(GitHubIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Body))
            return false;

        var body = issue.Body.ToLowerInvariant();

        // Check for common indicators of a well-defined issue
        var hasDescription = issue.Body.Length > 50;
        var hasExpectedBehavior = body.Contains("expect") || body.Contains("should") || body.Contains("want");
        var hasAcceptanceCriteria = body.Contains("- [ ]") || body.Contains("acceptance") || body.Contains("criteria");

        return hasDescription && (hasExpectedBehavior || hasAcceptanceCriteria);
    }
}
