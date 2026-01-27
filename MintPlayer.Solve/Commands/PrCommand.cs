using System.Text;
using System.Text.RegularExpressions;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;
using MintPlayer.Solve.Services;

namespace MintPlayer.Solve.Commands;

[CliCommand("pr", Description = "Create a pull request for the current issue")]
[CliParentCommand(typeof(SolveCommand))]
public partial class PrCommand : ICliCommand
{
    /// <summary>
    /// Standard locations for PR templates (in order of precedence).
    /// </summary>
    private static readonly string[] PrTemplateLocations =
    [
        ".github/PULL_REQUEST_TEMPLATE.md",
        ".github/pull_request_template.md",
        ".github/PULL_REQUEST_TEMPLATE/default.md",
        ".github/PULL_REQUEST_TEMPLATE/pull_request_template.md",
        "docs/PULL_REQUEST_TEMPLATE.md",
        "docs/pull_request_template.md",
        "PULL_REQUEST_TEMPLATE.md",
        "pull_request_template.md"
    ];

    /// <summary>
    /// Paths to check in the organization's .github repository.
    /// </summary>
    private static readonly string[] OrgTemplateLocations =
    [
        ".github/PULL_REQUEST_TEMPLATE.md",
        ".github/pull_request_template.md",
        "PULL_REQUEST_TEMPLATE.md",
        "pull_request_template.md"
    ];

    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly IGitService _git;
    [Inject] private readonly IGitHubService _github;

    [CliArgument(0, Name = "issue-url", Required = false, Description = "GitHub issue URL or reference (detects from branch if omitted)"), NoInterfaceMember]
    public string? IssueUrl { get; set; }

    [CliOption("--draft", "-d", Description = "Create as draft PR"), NoInterfaceMember]
    public bool Draft { get; set; }

    [CliOption("--base", "-b", Description = "Target branch (default: auto-detect)"), NoInterfaceMember]
    public string? BaseBranch { get; set; }

    [CliOption("--title", "-t", Description = "Custom PR title (default: issue title)"), NoInterfaceMember]
    public string? Title { get; set; }

    [CliOption("--no-checklist", Description = "Skip pre-PR checklist"), NoInterfaceMember]
    public bool NoChecklist { get; set; }

    [CliOption("--yes", "-y", Description = "Skip confirmation"), NoInterfaceMember]
    public bool SkipConfirmation { get; set; }

    [CliOption("--no-template", Description = "Ignore repository PR template, use built-in"), NoInterfaceMember]
    public bool NoTemplate { get; set; }

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

        // Check for existing PR
        var existingPr = await _github.GetPullRequestForBranchAsync(cancellationToken);
        if (existingPr.HasValue)
        {
            _console.WriteWarning($"A PR already exists for this branch:");
            _console.WriteLine($"  #{existingPr.Value.Number} - {existingPr.Value.Title}");
            _console.WriteLine($"  {existingPr.Value.Url}");
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
            _console.WriteError("Error: Could not determine issue number.");
            _console.WriteLine("Please provide an issue URL/reference or ensure you're on an issue branch.");
            return 1;
        }

        // Pre-PR checklist
        if (!NoChecklist)
        {
            _console.WriteHeader("\nPre-PR Checklist:");

            if (!_console.Confirm("Have you tested your changes locally?"))
            {
                _console.WriteError("Please test your changes before creating a PR.");
                return 1;
            }

            if (!_console.Confirm("Are there no uncommitted changes you want to include?"))
            {
                _console.WriteError("Please commit or stash your changes before creating a PR.");
                return 1;
            }
        }

        // Fetch issue details
        var issue = owner != null && repo != null
            ? await _github.GetIssueAsync(owner, repo, issueNumber.Value, cancellationToken)
            : await _github.GetIssueAsync(issueNumber.Value, cancellationToken);

        var prTitle = Title ?? issue?.Title ?? $"Fix issue #{issueNumber}";

        // Determine base branch
        var baseBranch = BaseBranch ?? await _git.GetDefaultBranchAsync(cancellationToken) ?? "main";

        // Get commit log and diff for description
        var commitLog = await _git.GetLogAsync(baseBranch, "HEAD", cancellationToken);
        var diff = await _git.GetDiffAsync(baseBranch, "HEAD", cancellationToken);

        // Determine PR type based on branch and labels
        var prType = DeterminePrType(currentBranch, issue?.Labels ?? []);

        // Generate PR body - check for repository template first, then org template
        string prBody;
        string? templateContent = null;
        string? templateSource = null;

        if (!NoTemplate)
        {
            // First, check for local repository template
            var localTemplatePath = FindLocalPrTemplate();
            if (localTemplatePath != null)
            {
                templateSource = localTemplatePath;
                templateContent = await File.ReadAllTextAsync(localTemplatePath, cancellationToken);
            }
            // Then, check for organization .github repository template
            else if (owner != null)
            {
                var (orgTemplateContent, orgTemplatePath) = await FindOrgPrTemplateAsync(owner, cancellationToken);
                if (orgTemplateContent != null)
                {
                    templateSource = $"{owner}/.github/{orgTemplatePath}";
                    templateContent = orgTemplateContent;
                }
            }
        }

        if (templateContent != null && templateSource != null)
        {
            _console.WriteInfo($"Using PR template: {templateSource}");
            prBody = ProcessPrTemplate(templateContent, issueNumber.Value, prType, commitLog, issue);
        }
        else
        {
            prBody = GeneratePrBody(issueNumber.Value, prType, commitLog, diff);
        }

        // Show preview
        _console.WriteHeader("\n=== PR Preview ===");
        _console.WriteLine($"Title: {prTitle}");
        _console.WriteLine($"Base: {baseBranch}");
        _console.WriteLine($"Head: {currentBranch}");
        if (Draft)
            _console.WriteLine("Draft: Yes");
        _console.WriteLine();
        _console.WriteLine("Body:");
        _console.WriteLine(prBody);
        _console.WriteLine("=== End Preview ===\n");

        if (!SkipConfirmation && !_console.Confirm("Create this PR?"))
        {
            _console.WriteLine("PR creation cancelled.");
            return 1;
        }

        // Push branch
        _console.WriteInfo("Pushing branch to remote...");
        var pushed = await _git.PushAsync(setUpstream: true, cancellationToken);
        if (!pushed)
        {
            _console.WriteError("Error: Failed to push branch.");
            return 1;
        }

        // Create PR
        _console.WriteInfo("Creating pull request...");
        var prUrl = await _github.CreatePullRequestAsync(prTitle, prBody, baseBranch, Draft, cancellationToken);

        if (string.IsNullOrEmpty(prUrl))
        {
            _console.WriteError("Error: Failed to create pull request.");
            return 1;
        }

        _console.WriteSuccess($"\nPull request created: {prUrl}");

        return 0;
    }

    private static string DeterminePrType(string? branchName, List<string> labels)
    {
        var labelSet = new HashSet<string>(labels.Select(l => l.ToLowerInvariant()));

        if (labelSet.Contains("bug") || labelSet.Contains("bugfix"))
            return "Bug Fix";
        if (labelSet.Contains("documentation") || labelSet.Contains("docs"))
            return "Documentation Update";
        if (labelSet.Contains("refactor"))
            return "Code Refactor";
        if (labelSet.Contains("test") || labelSet.Contains("testing"))
            return "Test";

        if (branchName != null)
        {
            var branch = branchName.ToLowerInvariant();
            if (branch.Contains("fix") || branch.Contains("bug"))
                return "Bug Fix";
            if (branch.Contains("doc"))
                return "Documentation Update";
            if (branch.Contains("refactor"))
                return "Code Refactor";
            if (branch.Contains("test"))
                return "Test";
        }

        return "Feature";
    }

    private static string GeneratePrBody(int issueNumber, string prType, string? commitLog, string? diff)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## What type of PR is this?");
        sb.AppendLine();
        sb.AppendLine(prType == "Feature" ? "- [x] Feature" : "- [ ] Feature");
        sb.AppendLine(prType == "Bug Fix" ? "- [x] Bug Fix" : "- [ ] Bug Fix");
        sb.AppendLine(prType == "Documentation Update" ? "- [x] Documentation Update" : "- [ ] Documentation Update");
        sb.AppendLine(prType == "Code Refactor" ? "- [x] Code Refactor" : "- [ ] Code Refactor");
        sb.AppendLine(prType == "Test" ? "- [x] Test" : "- [ ] Test");
        sb.AppendLine("- [ ] Chore");
        sb.AppendLine();

        sb.AppendLine("## Description");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(commitLog))
        {
            sb.AppendLine("### Changes");
            foreach (var line in commitLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10))
            {
                sb.AppendLine($"- {line.Trim()}");
            }
        }
        else
        {
            sb.AppendLine("<!-- Describe your changes here -->");
        }
        sb.AppendLine();

        sb.AppendLine("## Related Tickets & Documents");
        sb.AppendLine();
        sb.AppendLine($"Fixes #{issueNumber}");
        sb.AppendLine();

        sb.AppendLine("## Checklist");
        sb.AppendLine();
        sb.AppendLine("- [ ] Tested locally");
        sb.AppendLine("- [ ] Updated documentation (if applicable)");
        sb.AppendLine("- [ ] No breaking changes");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("*Created with [solve](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools)*");

        return sb.ToString();
    }

    /// <summary>
    /// Finds a PR template in the local repository.
    /// </summary>
    private static string? FindLocalPrTemplate()
    {
        foreach (var location in PrTemplateLocations)
        {
            if (File.Exists(location))
            {
                return location;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds a PR template in the organization's .github repository.
    /// </summary>
    private async Task<(string? Content, string? Path)> FindOrgPrTemplateAsync(string owner, CancellationToken cancellationToken)
    {
        foreach (var location in OrgTemplateLocations)
        {
            var content = await _github.GetFileContentsAsync(owner, ".github", location, cancellationToken);
            if (content != null)
            {
                return (content, location);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Processes a PR template by filling in placeholders.
    /// </summary>
    private static string ProcessPrTemplate(
        string template,
        int issueNumber,
        string prType,
        string? commitLog,
        GitHubIssue? issue)
    {

        // Build changes summary from commit log
        var changesSummary = new StringBuilder();
        if (!string.IsNullOrEmpty(commitLog))
        {
            foreach (var line in commitLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10))
            {
                changesSummary.AppendLine($"- {line.Trim()}");
            }
        }

        // Replace common placeholders
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Issue-related
            ["{issue_number}"] = issueNumber.ToString(),
            ["{issue-number}"] = issueNumber.ToString(),
            ["{{issue_number}}"] = issueNumber.ToString(),
            ["{{issue-number}}"] = issueNumber.ToString(),
            ["{issueNumber}"] = issueNumber.ToString(),
            ["#issue_number"] = $"#{issueNumber}",

            // Issue title
            ["{issue_title}"] = issue?.Title ?? "",
            ["{issue-title}"] = issue?.Title ?? "",
            ["{{issue_title}}"] = issue?.Title ?? "",
            ["{issueTitle}"] = issue?.Title ?? "",

            // PR type
            ["{pr_type}"] = prType,
            ["{pr-type}"] = prType,
            ["{{pr_type}}"] = prType,
            ["{prType}"] = prType,
            ["{type}"] = prType,

            // Changes/commits
            ["{changes}"] = changesSummary.ToString().TrimEnd(),
            ["{commits}"] = changesSummary.ToString().TrimEnd(),
            ["{{changes}}"] = changesSummary.ToString().TrimEnd(),
            ["{commit_log}"] = commitLog ?? "",
            ["{commitLog}"] = commitLog ?? "",

            // Labels
            ["{labels}"] = issue != null ? string.Join(", ", issue.Labels) : "",
            ["{{labels}}"] = issue != null ? string.Join(", ", issue.Labels) : "",

            // Author
            ["{author}"] = issue?.Author ?? "",
            ["{{author}}"] = issue?.Author ?? "",
        };

        // Apply replacements
        var result = template;
        foreach (var (placeholder, value) in replacements)
        {
            result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
        }

        // Handle "Fixes #X" or "Closes #X" patterns - ensure issue number is filled in
        result = Regex.Replace(
            result,
            @"(Fixes|Closes|Resolves|Related to)\s+#(\s*\n|$)",
            $"$1 #{issueNumber}$2",
            RegexOptions.IgnoreCase);

        // Also handle empty brackets like "Fixes #[]" or "Fixes #[issue-number]"
        result = Regex.Replace(
            result,
            @"(Fixes|Closes|Resolves|Related to)\s+#\[.*?\]",
            $"$1 #{issueNumber}",
            RegexOptions.IgnoreCase);

        // Auto-check the appropriate PR type checkbox if present
        result = AutoCheckPrType(result, prType);

        return result;
    }

    /// <summary>
    /// Auto-checks the appropriate PR type checkbox in the template.
    /// </summary>
    private static string AutoCheckPrType(string template, string prType)
    {
        var typePatterns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Feature"] = ["feature", "feat", "enhancement"],
            ["Bug Fix"] = ["bug", "bugfix", "bug fix", "fix"],
            ["Documentation Update"] = ["documentation", "docs", "doc"],
            ["Code Refactor"] = ["refactor", "refactoring", "code refactor"],
            ["Test"] = ["test", "testing", "tests"],
            ["Chore"] = ["chore", "maintenance", "release"]
        };

        foreach (var (type, patterns) in typePatterns)
        {
            if (type.Equals(prType, StringComparison.OrdinalIgnoreCase))
            {
                // Find and check the checkbox for this type
                foreach (var pattern in patterns)
                {
                    // Match "- [ ] Feature" or "- [ ] 🍕 Feature" style checkboxes
                    var regex = new Regex(
                        @"^(\s*-\s*)\[\s*\](\s*[^\w]*\s*" + Regex.Escape(pattern) + @")",
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);

                    if (regex.IsMatch(template))
                    {
                        template = regex.Replace(template, "$1[x]$2");
                        return template; // Only check one
                    }
                }
            }
        }

        return template;
    }
}
