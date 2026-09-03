using Solve.Commands;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve &lt;issue&gt;</c> — the root command: branch, generate the PRD and plan, launch Claude.
/// </summary>
public class SolveCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();
    private readonly FakePrdGenerator _prd = new();
    private readonly FakeClaudeService _claude = new();

    private SolveCommand Command(string? issueUrl = "42") => new(_console, _git, _github, _prd, _claude)
    {
        IssueUrl = issueUrl,
    };

    [Fact]
    public async Task ItRunsTheWholeSetup()
    {
        _github.AddIssue(42, "Fix the widget");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _git.CreatedBranch.Should().StartWith("issues/42-");
        _prd.SavedPlan.Should().NotBeNull();
        _prd.SavedPrd.Should().NotBeNull();
        _claude.LaunchedForIssue.Should().NotBeNull();
    }

    [Fact]
    public async Task ItPassesThePlanAndPrdPathsToClaude()
    {
        _github.AddIssue(42);

        await Command().Execute(default);

        _claude.LaunchedForIssue!.Value.PlanPath.Should().Be("docs/plan-42.md");
        _claude.LaunchedForIssue!.Value.PrdPath.Should().Be("docs/prd-42.md");
    }

    #region Skip flags

    [Fact]
    public async Task SkipPrdLeavesTheDocumentsAlone()
    {
        _github.AddIssue(42);

        var command = Command();
        command.SkipPrd = true;

        await command.Execute(default);

        _prd.SavedPlan.Should().BeNull();
        _prd.SavedPrd.Should().BeNull();
        _claude.LaunchedForIssue.Should().NotBeNull();
    }

    [Fact]
    public async Task SkipClaudeStillDoesTheSetup()
    {
        _github.AddIssue(42);

        var command = Command();
        command.SkipClaude = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _claude.LaunchedForIssue.Should().BeNull();
        _prd.SavedPrd.Should().NotBeNull();
    }

    /// <summary>
    /// A dry run has to be inert. Reporting what it would do while actually creating the branch
    /// would be worse than not having the flag.
    /// </summary>
    [Fact]
    public async Task DryRunChangesNothing()
    {
        _github.AddIssue(42);

        var command = Command();
        command.DryRun = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _git.Calls.Should().BeEmpty();
        _git.CreatedBranch.Should().BeNull();
        _prd.SavedPlan.Should().BeNull();
        _claude.LaunchedForIssue.Should().BeNull();
        _console.AllOutput.Should().Contain("DRY RUN");
    }

    [Fact]
    public async Task DryRunOmitsThePrdStepsWhenPrdIsSkipped()
    {
        _github.AddIssue(42);

        var command = Command();
        command.DryRun = true;
        command.SkipPrd = true;

        await command.Execute(default);

        _console.AllOutput.Should().NotContain("Generate PRD at");
    }

    #endregion

    #region Degraded paths

    /// <summary>
    /// The generator refuses to overwrite an existing document without force. That is a warning,
    /// not a failure — the branch is already made and Claude should still launch.
    /// </summary>
    [Fact]
    public async Task AnExistingPrdWarnsButDoesNotStopTheRun()
    {
        _github.AddIssue(42);
        _prd.SaveThrows = new InvalidOperationException("PRD already exists; pass --force to overwrite");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().ContainSingle().Which.Should().Contain("already exists");
        _claude.LaunchedForIssue.Should().NotBeNull();
    }

    [Fact]
    public async Task AMissingClaudeCliIsAWarningNotAFailure()
    {
        _github.AddIssue(42);
        _claude.IsAvailable = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().Contain(w => w.Contains("Claude CLI not found"));
        _git.CreatedBranch.Should().NotBeNull();
    }

    [Fact]
    public async Task ItOffersToSwitchWhenTheBranchAlreadyExists()
    {
        _github.AddIssue(42);
        _git.BranchExists = true;
        _console.ConfirmResponses.Enqueue(true);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _git.CreatedBranch.Should().BeNull();
        _git.CheckedOut.Should().Contain(b => b.StartsWith("issues/42-"));
    }

    [Fact]
    public async Task DecliningTheExistingBranchAborts()
    {
        _github.AddIssue(42);
        _git.BranchExists = true;
        _console.ConfirmResponses.Enqueue(false);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _prd.SavedPrd.Should().BeNull();
        _claude.LaunchedForIssue.Should().BeNull();
    }

    #endregion

    #region Guard clauses

    [Fact]
    public async Task ItShowsUsageWithoutAnIssue()
    {
        var exitCode = await Command(issueUrl: null).Execute(default);

        exitCode.Should().Be(1);
        _console.AllOutput.Should().Contain("Usage: solve");
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

    [Fact]
    public async Task ItStopsOnAnUnparseableReference()
    {
        (await Command(issueUrl: "nonsense").Execute(default)).Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("parse");
    }

    [Fact]
    public async Task ItStopsWhenTheIssueCannotBeFetched()
    {
        (await Command(issueUrl: "999").Execute(default)).Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("999");
    }

    #endregion
}
