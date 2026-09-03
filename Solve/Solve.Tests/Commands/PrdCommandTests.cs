using Solve.Commands;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve prd</c> — generate the PRD and development plan for an issue.
/// </summary>
public class PrdCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();
    private readonly FakePrdGenerator _prd = new();

    private PrdCommand Command(string? issueUrl = "42") => new(_console, _git, _github, _prd)
    {
        IssueUrl = issueUrl,
    };

    /// <summary>
    /// A body long enough, with an expectation in it, reads as clear — used wherever a test is not
    /// about the clarity check itself.
    /// </summary>
    private const string ClearBody =
        "The widget should align to the left when the sidebar is collapsed, but it does not. " +
        "Expected: left-aligned. Actual: centered.";

    [Fact]
    public async Task ItGeneratesBothDocuments()
    {
        _github.AddIssue(42).Body = ClearBody;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _prd.SavedPlan.Should().Be("# Plan");
        _prd.SavedPrd.Should().Be("# PRD");
    }

    /// <summary>
    /// The overwrite decision is made by this command's own prompt, so by the time it saves it has
    /// already decided — it always passes force:true and must not be second-guessed lower down.
    /// </summary>
    [Fact]
    public async Task ItSavesWithForceHavingAlreadyAsked()
    {
        _github.AddIssue(42).Body = ClearBody;

        await Command().Execute(default);

        _prd.SavedPlanWithForce.Should().BeTrue();
        _prd.SavedPrdWithForce.Should().BeTrue();
    }

    [Fact]
    public async Task ItDetectsTheIssueFromTheBranch()
    {
        _git.CurrentBranch = "issues/42-fix-the-widget";
        _github.AddIssue(42).Body = ClearBody;

        var exitCode = await Command(issueUrl: null).Execute(default);

        exitCode.Should().Be(0);
        _console.Infos.Should().Contain(i => i.Contains("Detected issue #42"));
    }

    [Fact]
    public async Task ItFailsWhenTheIssueCannotBeDetermined()
    {
        _git.CurrentBranch = "master";

        var exitCode = await Command(issueUrl: null).Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("Could not determine");
    }

    [Fact]
    public async Task ItShowsTheIssueTypeAndPriority()
    {
        _github.AddIssue(42, "Fix it", "bug", "priority: high").Body = ClearBody;

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("Type:");
        _console.AllOutput.Should().Contain("Priority:");
    }

    #region Overwrite prompts

    [Fact]
    public async Task ItAsksBeforeOverwritingAnExistingPrd()
    {
        _github.AddIssue(42).Body = ClearBody;
        _prd.PrdOnDisk = true;
        _console.ConfirmResponses.Enqueue(true);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.ConfirmsAsked.Should().ContainSingle();
        _console.Warnings.Should().Contain(w => w.Contains("PRD already exists"));
    }

    [Fact]
    public async Task DecliningTheOverwriteAborts()
    {
        _github.AddIssue(42).Body = ClearBody;
        _prd.PrdOnDisk = true;
        _console.ConfirmResponses.Enqueue(false);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _prd.SavedPrd.Should().BeNull();
    }

    [Fact]
    public async Task ForceSkipsTheOverwritePrompts()
    {
        _github.AddIssue(42).Body = ClearBody;
        _prd.PrdOnDisk = true;
        _prd.PlanOnDisk = true;

        var command = Command();
        command.Force = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _console.ConfirmsAsked.Should().BeEmpty();
    }

    #endregion

    #region Issue clarity

    /// <summary>
    /// The clarity heuristic needs both length and either an expectation or acceptance criteria.
    /// A one-line issue therefore reads as unclear, which is the case worth prompting on.
    /// </summary>
    [Fact]
    public async Task ItWarnsWhenTheIssueLooksUnclear()
    {
        _github.AddIssue(42).Body = "broken";

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().Contain(w => w.Contains("unclear or incomplete"));
    }

    [Fact]
    public async Task ItStaysQuietWhenTheIssueHasAcceptanceCriteria()
    {
        _github.AddIssue(42).Body =
            "The alignment is wrong when the sidebar collapses and it needs fixing properly.\n- [ ] left aligned";

        await Command().Execute(default);

        _console.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyBodyIsUnclear()
    {
        _github.AddIssue(42).Body = "";

        await Command().Execute(default);

        _console.Warnings.Should().Contain(w => w.Contains("unclear"));
    }

    /// <summary>
    /// The clarification is appended to the issue body on GitHub AND to the in-memory issue, so
    /// the PRD generated moments later includes it. Losing the second half would silently produce
    /// a PRD from the un-clarified text.
    /// </summary>
    [Fact]
    public async Task UpdateIssueAppendsTheClarificationAndUsesIt()
    {
        var issue = _github.AddIssue(42);
        issue.Body = "broken";
        _console.ConfirmResponses.Enqueue(true);
        _console.PromptResponses.Enqueue("It should align left.");

        var command = Command();
        command.UpdateIssue = true;

        await command.Execute(default);

        _github.UpdatedIssueBody.Should().Contain("Additional Context");
        _github.UpdatedIssueBody.Should().Contain("It should align left.");
        issue.Body.Should().Contain("It should align left.");
    }

    [Fact]
    public async Task UpdateIssueDoesNothingWhenTheUserDeclines()
    {
        _github.AddIssue(42).Body = "broken";
        _console.ConfirmResponses.Enqueue(false);

        var command = Command();
        command.UpdateIssue = true;

        await command.Execute(default);

        _github.UpdatedIssueBody.Should().BeNull();
    }

    [Fact]
    public async Task UpdateIssueDoesNothingOnAnEmptyClarification()
    {
        _github.AddIssue(42).Body = "broken";
        _console.ConfirmResponses.Enqueue(true);
        _console.PromptResponses.Enqueue("");

        var command = Command();
        command.UpdateIssue = true;

        await command.Execute(default);

        _github.UpdatedIssueBody.Should().BeNull();
    }

    #endregion

    #region Guard clauses

    [Fact]
    public async Task ItStopsWhenTheIssueCannotBeFetched()
    {
        (await Command(issueUrl: "999").Execute(default)).Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("999");
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
