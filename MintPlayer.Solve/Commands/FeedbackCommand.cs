using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Services;

namespace MintPlayer.Solve.Commands;

[CliCommand("feedback", Description = "Review and resolve PR feedback")]
[CliParentCommand(typeof(SolveCommand))]
public partial class FeedbackCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;
    [Inject] private readonly IClaudeService _claude;

    [CliArgument(0, Name = "pr-url", Required = false, Description = "PR URL or number (detects from branch if omitted)"), NoInterfaceMember]
    public string? PrUrl { get; set; }

    [CliOption("--assess-only", Description = "Only show assessment, don't implement"), NoInterfaceMember]
    public bool AssessOnly { get; set; }

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

        // Get PR info
        var pr = await _github.GetPullRequestForBranchAsync(cancellationToken);

        if (!pr.HasValue && string.IsNullOrEmpty(PrUrl))
        {
            _console.WriteError("Error: No PR found for current branch.");
            _console.WriteLine("Please provide a PR URL/number or ensure you're on a branch with an open PR.");
            return 1;
        }

        var prNumber = pr?.Number ?? 0;
        var prTitle = pr?.Title ?? "Unknown";

        if (!string.IsNullOrEmpty(PrUrl))
        {
            // Parse PR URL/number
            if (int.TryParse(PrUrl.TrimStart('#'), out var parsedNumber))
            {
                prNumber = parsedNumber;
            }
            else if (PrUrl.Contains("/pull/"))
            {
                var parts = PrUrl.Split("/pull/");
                if (parts.Length > 1 && int.TryParse(parts[1].Split('/')[0], out var urlNumber))
                {
                    prNumber = urlNumber;
                }
            }
        }

        if (prNumber == 0)
        {
            _console.WriteError("Error: Could not determine PR number.");
            return 1;
        }

        _console.WriteInfo($"Fetching feedback for PR #{prNumber}...");

        // Get repo info
        var (owner, repo) = await _git.GetRemoteInfoAsync(cancellationToken);

        if (owner == null || repo == null)
        {
            _console.WriteError("Error: Could not determine repository info.");
            return 1;
        }

        // Fetch review threads
        var reviewData = await _github.GetPullRequestReviewsAsync(owner, repo, prNumber, cancellationToken);

        if (string.IsNullOrEmpty(reviewData))
        {
            _console.WriteWarning("No review feedback found or unable to fetch reviews.");
            _console.WriteLine("This could mean:");
            _console.WriteLine("  - The PR has no reviews yet");
            _console.WriteLine("  - All review threads are resolved");
            _console.WriteLine("  - There was an error fetching the data");
            return 0;
        }

        _console.WriteHeader($"\n## PR #{prNumber}: {prTitle}");
        _console.WriteLine();
        _console.WriteLine("Review feedback data retrieved.");
        _console.WriteLine();

        if (AssessOnly)
        {
            _console.WriteLine("Review data (raw):");
            _console.WriteLine(reviewData);
            return 0;
        }

        // Launch Claude to help process the feedback
        if (await _claude.IsAvailableAsync(cancellationToken))
        {
            var prompt = $"""
                You are helping to resolve PR feedback for PR #{prNumber}.

                Here is the review data from GitHub:
                {reviewData}

                Please analyze the unresolved review threads and:
                1. Summarize each unresolved comment/change request
                2. Assess whether each is valid, partially valid, or not valid
                3. Create a development plan for addressing valid feedback
                4. Implement the necessary changes

                Focus only on unresolved threads (where isResolved is false).
                """;

            _console.WriteInfo("Launching Claude Code to help resolve feedback...");
            await _claude.LaunchWithPromptAsync(prompt, cancellationToken);
        }
        else
        {
            _console.WriteWarning("Claude CLI not found.");
            _console.WriteLine("Please review the feedback manually:");
            _console.WriteLine(reviewData);
        }

        return 0;
    }
}
