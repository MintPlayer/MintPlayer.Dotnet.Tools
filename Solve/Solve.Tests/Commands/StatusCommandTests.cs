using Solve.Commands;
using Solve.Models;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve status</c> — where the work on an issue currently stands.
/// </summary>
/// <remarks>
/// Note the JSON path writes to <see cref="Console"/> directly rather than through
/// <c>IConsoleService</c>, so those tests capture stdout. That is a seam worth closing eventually;
/// the tests document it rather than pretend otherwise.
/// </remarks>
public class StatusCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();
    private readonly FakePrdGenerator _prd = new();

    private StatusCommand Command(string? issueUrl = null) => new(_console, _git, _github, _prd)
    {
        IssueUrl = issueUrl,
    };

    private static async Task<string> CaptureStdout(Func<Task> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Fact]
    public async Task ItDerivesTheIssueNumberFromTheBranchName()
    {
        _git.CurrentBranch = "issues/42-fix-the-widget";
        _github.AddIssue(42, "Fix the widget");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.AllOutput.Should().Contain("42");
        _console.AllOutput.Should().Contain("Fix the widget");
    }

    [Fact]
    public async Task AnExplicitReferenceWinsOverTheBranch()
    {
        _git.CurrentBranch = "issues/42-fix-the-widget";
        _github.AddIssue(7, "Something else");

        await Command(issueUrl: "7").Execute(default);

        _console.AllOutput.Should().Contain("Something else");
    }

    [Fact]
    public async Task ItFailsWhenTheIssueCannotBeDetermined()
    {
        _git.CurrentBranch = "master";

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("Could not determine");
    }

    /// <summary>
    /// An issue with no PRD yet is a normal state, not an error — this is the first thing a user
    /// runs after <c>solve init</c>.
    /// </summary>
    [Fact]
    public async Task ItReportsNotCreatedWhenThereIsNoPrd()
    {
        _git.CurrentBranch = "issues/42-x";
        _github.AddIssue(42);
        _prd.Status = null;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.AllOutput.Should().Contain("Not Created");
        _console.AllOutput.Should().Contain("Not Started");
    }

    [Fact]
    public async Task ItRendersAProgressBarAndCounts()
    {
        _git.CurrentBranch = "issues/42-x";
        _github.AddIssue(42);
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Fix the widget",
            PrdStatus = "Complete",
            ImplementationStatus = "In Progress",
            TotalRequirements = 10,
            CompletedRequirements = 5,
            CompletedItems = ["R1", "R2", "R3", "R4", "R5"],
            RemainingItems = ["R6", "R7"],
        };

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("5/10");
        _console.AllOutput.Should().Contain("#");
        _console.AllOutput.Should().Contain("Completed (5)");
        _console.AllOutput.Should().Contain("Remaining (2)");
    }

    /// <summary>
    /// Long lists are truncated to five with a "and N more" tail, so status stays readable on a
    /// large issue.
    /// </summary>
    [Fact]
    public async Task ItTruncatesLongLists()
    {
        _git.CurrentBranch = "issues/42-x";
        _github.AddIssue(42);
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Big one",
            TotalRequirements = 8,
            CompletedRequirements = 8,
            CompletedItems = ["a", "b", "c", "d", "e", "f", "g", "h"],
        };

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("and 3 more");
    }

    [Fact]
    public async Task ItSurfacesOpenQuestionsAndUncommittedChanges()
    {
        _git.CurrentBranch = "issues/42-x";
        _git.HasUncommittedChanges = true;
        _github.AddIssue(42);
        _prd.Status = new WorkStatus
        {
            IssueNumber = 42,
            IssueTitle = "Fix",
            OpenQuestions = ["Which database?"],
        };

        await Command().Execute(default);

        _console.Warnings.Should().Contain(w => w.Contains("Open Questions"));
        _console.Warnings.Should().Contain(w => w.Contains("Uncommitted"));
    }

    [Fact]
    public async Task ItShowsAnOpenPullRequest()
    {
        _git.CurrentBranch = "issues/42-x";
        _github.AddIssue(42);
        _github.PullRequestForBranch = (7, "Fix the widget", "https://github.com/o/r/pull/7");

        await Command().Execute(default);

        _console.Infos.Should().Contain(i => i.Contains("https://github.com/o/r/pull/7"));
    }

    #region JSON output

    [Fact]
    public async Task JsonOutputEmitsTheStatusAsJson()
    {
        _git.CurrentBranch = "issues/42-x";
        _github.AddIssue(42, "Fix the widget");
        _prd.Status = new WorkStatus { IssueNumber = 42, IssueTitle = "Fix the widget", PrdStatus = "Complete" };

        var command = Command();
        command.JsonOutput = true;

        var stdout = await CaptureStdout(() => command.Execute(default));

        stdout.Should().Contain("\"IssueNumber\": 42");
        stdout.Should().Contain("Complete");
    }

    /// <summary>
    /// The failure path has to stay machine-readable too — a consumer parsing stdout should get
    /// JSON back, not prose it cannot handle.
    /// </summary>
    [Fact]
    public async Task JsonOutputReportsFailuresAsJson()
    {
        _git.CurrentBranch = "master";

        var command = Command();
        command.JsonOutput = true;

        var stdout = await CaptureStdout(() => command.Execute(default));

        stdout.Should().Contain("error");
        _console.Errors.Should().BeEmpty();
    }

    #endregion

    #region Guard clauses

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
