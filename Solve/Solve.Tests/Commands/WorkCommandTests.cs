using Solve.Commands;
using Solve.Models;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve work</c> — show where an issue stands, then hand it to Claude Code.
/// </summary>
public class WorkCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();
    private readonly FakePrdGenerator _prd = new();
    private readonly FakeClaudeService _claude = new();

    private WorkCommand Command(string? issueUrl = "42") => new(_console, _git, _github, _prd, _claude)
    {
        IssueUrl = issueUrl,
    };

    /// <summary>Both documents present — the precondition for doing any work at all.</summary>
    private void WithDocuments()
    {
        _prd.PrdOnDisk = true;
        _prd.PlanOnDisk = true;
    }

    [Fact]
    public async Task ItLaunchesClaudeWithBothDocuments()
    {
        WithDocuments();
        _github.AddIssue(42, "Fix the widget");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _claude.LaunchedForIssue.Should().NotBeNull();
        _claude.LaunchedForIssue!.Value.PlanPath.Should().Be("docs/plan-42.md");
        _claude.LaunchedForIssue!.Value.PrdPath.Should().Be("docs/prd-42.md");
    }

    [Fact]
    public async Task ItDetectsTheIssueFromTheBranch()
    {
        WithDocuments();
        _git.CurrentBranch = "issues/42-fix-the-widget";
        _github.AddIssue(42);

        var exitCode = await Command(issueUrl: null).Execute(default);

        exitCode.Should().Be(0);
        _console.Infos.Should().Contain(i => i.Contains("Detected issue #42"));
    }

    #region Missing documents

    /// <summary>
    /// Without a plan there is nothing for Claude to work from, so the command stops and names the
    /// missing files rather than launching into an empty session.
    /// </summary>
    [Fact]
    public async Task ItRefusesWhenBothDocumentsAreMissing()
    {
        _github.AddIssue(42);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _claude.LaunchedForIssue.Should().BeNull();
        _console.AllOutput.Should().Contain("docs/plan-42.md");
        _console.AllOutput.Should().Contain("docs/prd-42.md");
        _console.AllOutput.Should().Contain("solve prd");
    }

    [Fact]
    public async Task ItNamesOnlyTheDocumentThatIsMissing()
    {
        _github.AddIssue(42);
        _prd.PrdOnDisk = true;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.AllOutput.Should().Contain("docs/plan-42.md");
        _console.AllOutput.Should().NotContain("  - docs/prd-42.md");
    }

    #endregion

    #region Status display

    [Fact]
    public async Task ItShowsProgressAndRemainingWork()
    {
        WithDocuments();
        _github.AddIssue(42, "Fix the widget");
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Fix the widget",
            PrdStatus = "Complete",
            ImplementationStatus = "In Progress",
            TotalRequirements = 4,
            CompletedRequirements = 2,
            CompletedItems = ["R1", "R2"],
            RemainingItems = ["R3", "R4"],
        };

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("2 of 4 completed");
        _console.AllOutput.Should().Contain("- [x] R1");
        _console.AllOutput.Should().Contain("- [ ] R3");
    }

    [Fact]
    public async Task ItSurfacesBlockersAsErrors()
    {
        WithDocuments();
        _github.AddIssue(42);
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Fix",
            Blockers = ["Waiting on the API team"],
            OpenQuestions = ["Which database?"],
        };

        await Command().Execute(default);

        _console.Errors.Should().Contain(e => e.Contains("Blockers"));
        _console.Warnings.Should().Contain(w => w.Contains("Open Questions"));
        _console.AllOutput.Should().Contain("Waiting on the API team");
    }

    [Fact]
    public async Task ItTruncatesLongListsAtTen()
    {
        WithDocuments();
        _github.AddIssue(42);
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Big",
            CompletedItems = [.. Enumerable.Range(1, 13).Select(i => $"R{i}")],
        };

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("and 3 more");
    }

    [Fact]
    public async Task ItWarnsAboutUncommittedChangesAndShowsAnOpenPr()
    {
        WithDocuments();
        _github.AddIssue(42);
        _git.HasUncommittedChanges = true;
        _github.PullRequestForBranch = (7, "Fix the widget", "https://github.com/o/r/pull/7");

        await Command().Execute(default);

        _console.Warnings.Should().Contain(w => w.Contains("uncommitted changes"));
        _console.AllOutput.Should().Contain("https://github.com/o/r/pull/7");
    }

    #endregion

    [Fact]
    public async Task StatusOnlyStopsBeforeLaunchingClaude()
    {
        WithDocuments();
        _github.AddIssue(42);

        var command = Command();
        command.StatusOnly = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _claude.LaunchedForIssue.Should().BeNull();
    }

    /// <summary>
    /// A missing Claude CLI still leaves the user able to proceed by hand, so the command prints
    /// the paths and succeeds rather than failing on something it cannot control.
    /// </summary>
    [Fact]
    public async Task AMissingClaudeCliPrintsThePathsInstead()
    {
        WithDocuments();
        _github.AddIssue(42);
        _claude.IsAvailable = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().Contain(w => w.Contains("Claude CLI not found"));
        _console.AllOutput.Should().Contain("docs/prd-42.md");
    }

    #region Guard clauses

    [Fact]
    public async Task ItFailsWhenTheIssueCannotBeDetermined()
    {
        _git.CurrentBranch = "master";

        (await Command(issueUrl: null).Execute(default)).Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("Could not determine"));
    }

    [Fact]
    public async Task ItStopsWhenGhIsMissing()
    {
        _github.IsInstalled = false;

        (await Command().Execute(default)).Should().Be(1);
        _console.ShowedGhInstallInstructions.Should().BeTrue();
    }

    [Fact]
    public async Task ItStopsWhenGhIsNotAuthenticated()
    {
        _github.IsAuthenticated = false;

        (await Command().Execute(default)).Should().Be(1);
        _console.ShowedGhAuthInstructions.Should().BeTrue();
    }

    #endregion
}
