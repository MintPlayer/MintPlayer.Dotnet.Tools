using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;
using MintPlayer.Solve.Services;

namespace MintPlayer.Solve.Commands;

[CliCommand("init", Description = "Initialize branch for an issue")]
[CliParentCommand(typeof(SolveCommand))]
public partial class InitCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;

    [CliArgument(0, Name = "issue-url", Description = "GitHub issue URL or reference"), NoInterfaceMember]
    public string IssueUrl { get; set; } = string.Empty;

    [CliOption("--branch-prefix", "-p", Description = "Branch prefix", DefaultValue = "issues"), NoInterfaceMember]
    public string BranchPrefix { get; set; } = "issues";

    [CliOption("--no-pull", Description = "Skip git pull"), NoInterfaceMember]
    public bool NoPull { get; set; }

    [CliOption("--force", Description = "Create new branch even if one exists"), NoInterfaceMember]
    public bool Force { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(IssueUrl))
        {
            _console.WriteError("Error: Issue URL or reference is required.");
            _console.WriteLine("Usage: solve init <issue-url>");
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

        // Check for existing branch
        var branchName = $"{BranchPrefix}/{issue.Number}-{issue.GetSlug()}";
        var existingBranches = await _git.FindBranchesAsync($"{BranchPrefix}/{issue.Number}", cancellationToken);

        if (existingBranches.Count > 0 && !Force)
        {
            _console.WriteWarning($"Found existing branch(es) for issue #{issue.Number}:");
            foreach (var branch in existingBranches)
            {
                _console.WriteLine($"  - {branch}");
            }

            if (_console.Confirm("Switch to existing branch?"))
            {
                var targetBranch = existingBranches[0];
                await _git.FetchAsync(cancellationToken);
                await _git.CheckoutAsync(targetBranch, cancellationToken);
                _console.WriteSuccess($"Switched to branch: {targetBranch}");
                return 0;
            }
            else if (!_console.Confirm("Create a new branch anyway?"))
            {
                return 1;
            }
        }

        // Switch to default branch
        var defaultBranch = await _git.GetDefaultBranchAsync(cancellationToken);
        _console.WriteInfo($"Switching to default branch ({defaultBranch})...");
        await _git.CheckoutAsync(defaultBranch!, cancellationToken);

        // Pull latest (unless skipped)
        if (!NoPull)
        {
            _console.WriteInfo("Pulling latest changes...");
            await _git.PullAsync(cancellationToken);
        }

        // Create feature branch
        _console.WriteInfo($"Creating branch: {branchName}");
        var created = await _git.CreateBranchAsync(branchName, cancellationToken);

        if (!created)
        {
            _console.WriteError($"Error: Failed to create branch '{branchName}'");
            return 1;
        }

        _console.WriteSuccess($"\nBranch '{branchName}' created and checked out.");
        _console.WriteLine("\nNext steps:");
        _console.WriteLine("  solve prd       - Generate PRD and development plan");
        _console.WriteLine("  solve work      - Start working with Claude Code");

        return 0;
    }
}
