using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;
using MintPlayer.Solve.Services;

namespace MintPlayer.Solve.Commands;

[CliRootCommand(Name = "solve", Description = "Delegate GitHub issues to Claude Code")]
public partial class SolveCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;
    [Inject] private readonly IPrdGenerator _prdGenerator;
    [Inject] private readonly IClaudeService _claude;

    [CliArgument(0, Name = "issue-url", Required = false, Description = "GitHub issue URL or reference"), NoInterfaceMember]
    public string? IssueUrl { get; set; }

    [CliOption("--branch-prefix", "-p", Description = "Branch prefix", DefaultValue = "issues"), NoInterfaceMember]
    public string BranchPrefix { get; set; } = "issues";

    [CliOption("--skip-prd", Description = "Skip PRD generation"), NoInterfaceMember]
    public bool SkipPrd { get; set; }

    [CliOption("--skip-claude", Description = "Don't launch Claude Code after setup"), NoInterfaceMember]
    public bool SkipClaude { get; set; }

    [CliOption("--dry-run", Description = "Show what would be done without executing"), NoInterfaceMember]
    public bool DryRun { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(IssueUrl))
        {
            _console.WriteError("Error: Issue URL or reference is required.");
            _console.WriteLine("Usage: solve <issue-url>");
            _console.WriteLine("       solve https://github.com/owner/repo/issues/123");
            _console.WriteLine("       solve owner/repo#123");
            _console.WriteLine("       solve #123");
            return 1;
        }

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

        // Parse issue reference
        var (owner, repo) = await _git.GetRemoteInfoAsync(cancellationToken);
        var issueRef = IssueReference.Parse(IssueUrl, owner, repo);

        if (issueRef == null)
        {
            _console.WriteError($"Error: Could not parse issue reference: {IssueUrl}");
            return 1;
        }

        // Fetch issue details
        _console.WriteInfo($"Fetching issue #{issueRef.Number}...");
        var issue = issueRef.Owner != null && issueRef.Repo != null
            ? await _github.GetIssueAsync(issueRef.Owner, issueRef.Repo, issueRef.Number, cancellationToken)
            : await _github.GetIssueAsync(issueRef.Number, cancellationToken);

        if (issue == null)
        {
            _console.WriteError($"Error: Could not fetch issue #{issueRef.Number}");
            return 1;
        }

        _console.WriteLine($"  Title: {issue.Title}");
        if (issue.Labels.Count > 0)
            _console.WriteLine($"  Labels: {string.Join(", ", issue.Labels)}");

        if (DryRun)
        {
            _console.WriteHeader("\n[DRY RUN] Would execute:");
            _console.WriteLine($"  1. Switch to default branch and pull");
            _console.WriteLine($"  2. Create branch: {BranchPrefix}/{issue.Number}-{issue.GetSlug()}");
            if (!SkipPrd)
            {
                _console.WriteLine($"  3. Generate PRD at: {_prdGenerator.GetPrdPath(issue.Number)}");
                _console.WriteLine($"  4. Generate plan at: {_prdGenerator.GetPlanPath(issue.Number)}");
            }
            if (!SkipClaude)
                _console.WriteLine($"  5. Launch Claude Code");
            return 0;
        }

        // Switch to default branch
        var defaultBranch = await _git.GetDefaultBranchAsync(cancellationToken);
        _console.WriteInfo($"Switching to default branch ({defaultBranch})...");
        await _git.CheckoutAsync(defaultBranch!, cancellationToken);

        // Pull latest
        _console.WriteInfo("Pulling latest changes...");
        await _git.PullAsync(cancellationToken);

        // Create feature branch
        var branchName = $"{BranchPrefix}/{issue.Number}-{issue.GetSlug()}";

        // Check if branch already exists
        if (await _git.BranchExistsAsync(branchName, cancellationToken))
        {
            _console.WriteWarning($"Branch '{branchName}' already exists.");
            if (_console.Confirm("Switch to existing branch?"))
            {
                await _git.CheckoutAsync(branchName, cancellationToken);
            }
            else
            {
                return 1;
            }
        }
        else
        {
            _console.WriteInfo($"Creating branch: {branchName}");
            await _git.CreateBranchAsync(branchName, cancellationToken);
        }

        // Generate PRD and plan
        if (!SkipPrd)
        {
            try
            {
                var planContent = await _prdGenerator.GeneratePlanAsync(issue, cancellationToken);
                var planPath = await _prdGenerator.SavePlanAsync(issue.Number, planContent, force: false, cancellationToken);
                _console.WriteSuccess($"Generated plan at: {planPath}");

                var prdContent = await _prdGenerator.GeneratePrdAsync(issue, cancellationToken);
                var prdPath = await _prdGenerator.SavePrdAsync(issue.Number, prdContent, force: false, cancellationToken);
                _console.WriteSuccess($"Generated PRD at: {prdPath}");
            }
            catch (InvalidOperationException ex)
            {
                _console.WriteWarning(ex.Message);
            }
        }

        // Launch Claude
        if (!SkipClaude)
        {
            if (await _claude.IsAvailableAsync(cancellationToken))
            {
                _console.WriteInfo("Launching Claude Code...");
                await _claude.LaunchForIssueAsync(
                    issue,
                    _prdGenerator.GetPlanPath(issue.Number),
                    _prdGenerator.GetPrdPath(issue.Number),
                    cancellationToken);
            }
            else
            {
                _console.WriteWarning("Claude CLI not found. Skipping Claude Code launch.");
                _console.WriteLine("You can start Claude manually with the generated PRD and plan.");
            }
        }

        _console.WriteSuccess("\nSetup complete!");
        return 0;
    }
}
