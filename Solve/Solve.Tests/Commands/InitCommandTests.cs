using Solve.Commands;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve init &lt;issue&gt;</c> — fetch the issue, then branch off an up-to-date default branch.
/// </summary>
/// <remarks>
/// The command is constructed directly rather than through the CLI parser: the parser is
/// MintPlayer.CliGenerator's job and is tested there, while what matters here is the decision
/// logic between the injected services. Exit code and what reached the console are the contract —
/// this is a CLI, so those are literally what the user gets.
/// </remarks>
public class InitCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();

    private InitCommand Command(string issueUrl = "42") => new(_console, _git, _github)
    {
        IssueUrl = issueUrl,
    };

    [Fact]
    public async Task ItCreatesABranchNamedAfterTheIssue()
    {
        _github.AddIssue(42, "Fix the widget alignment");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _git.CreatedBranch.Should().StartWith("issues/42-");
    }

    [Fact]
    public async Task ItHonoursTheBranchPrefix()
    {
        _github.AddIssue(42);

        var command = Command();
        command.BranchPrefix = "feature";

        await command.Execute(default);

        _git.CreatedBranch.Should().StartWith("feature/42-");
    }

    /// <summary>
    /// Order is the point, not the individual calls: branching before checkout-and-pull would
    /// branch off whatever happened to be checked out, which is the bug this sequence prevents.
    /// </summary>
    [Fact]
    public async Task ItChecksOutAndPullsTheDefaultBranchBeforeBranching()
    {
        _github.AddIssue(42);
        _git.DefaultBranch = "development";

        await Command().Execute(default);

        _git.Calls.Should().ContainInOrder("checkout development", "pull");
        _git.Calls.IndexOf("pull").Should().BeLessThan(_git.Calls.FindIndex(c => c.StartsWith("branch ")));
    }

    [Fact]
    public async Task ItSkipsThePullWhenAsked()
    {
        _github.AddIssue(42);

        var command = Command();
        command.NoPull = true;

        await command.Execute(default);

        _git.Calls.Should().NotContain("pull");
        _git.CreatedBranch.Should().NotBeNull();
    }

    #region Guard clauses

    [Fact]
    public async Task ItRefusesWithoutAnIssueReference()
    {
        var exitCode = await Command(issueUrl: "").Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("required");
    }

    /// <summary>
    /// The gh checks come before anything is fetched or branched. A missing CLI has to produce
    /// install instructions, not a confusing failure three steps later.
    /// </summary>
    [Fact]
    public async Task ItExplainsHowToInstallGhWhenItIsMissing()
    {
        _github.IsInstalled = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.ShowedGhInstallInstructions.Should().BeTrue();
        _git.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ItExplainsHowToAuthenticateWhenGhIsNotLoggedIn()
    {
        _github.IsAuthenticated = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.ShowedGhAuthInstructions.Should().BeTrue();
        _git.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ItReportsAnUnparseableIssueReference()
    {
        var exitCode = await Command(issueUrl: "not-an-issue").Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("parse");
    }

    [Fact]
    public async Task ItReportsAnIssueThatDoesNotExist()
    {
        var exitCode = await Command(issueUrl: "999").Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("999");
        _git.CreatedBranch.Should().BeNull();
    }

    [Fact]
    public async Task ItReportsAFailedBranchCreation()
    {
        _github.AddIssue(42);
        _git.CreateBranchSucceeds = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().ContainSingle().Which.Should().Contain("Failed to create branch");
    }

    #endregion

    #region Existing branches

    [Fact]
    public async Task ItOffersToSwitchToAnExistingBranch()
    {
        _github.AddIssue(42);
        _git.MatchingBranches = ["issues/42-fix-the-widget"];
        _console.ConfirmResponses.Enqueue(true);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _git.CheckedOut.Should().Contain("issues/42-fix-the-widget");
        _git.CreatedBranch.Should().BeNull();
        _console.Warnings.Should().NotBeEmpty();
    }

    /// <summary>
    /// Declining both questions is a deliberate abort, so it exits non-zero and touches nothing —
    /// answering "no" twice must not fall through into creating the branch anyway.
    /// </summary>
    [Fact]
    public async Task ItAbortsWhenTheUserDeclinesBothOffers()
    {
        _github.AddIssue(42);
        _git.MatchingBranches = ["issues/42-fix-the-widget"];
        _console.ConfirmResponses.Enqueue(false);
        _console.ConfirmResponses.Enqueue(false);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _git.CreatedBranch.Should().BeNull();
        _git.CheckedOut.Should().BeEmpty();
    }

    [Fact]
    public async Task ItCreatesANewBranchWhenTheUserInsists()
    {
        _github.AddIssue(42);
        _git.MatchingBranches = ["issues/42-fix-the-widget"];
        _console.ConfirmResponses.Enqueue(false);
        _console.ConfirmResponses.Enqueue(true);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _git.CreatedBranch.Should().StartWith("issues/42-");
    }

    /// <summary>
    /// --force is there precisely so the command does not stop to ask; if it still prompts, the
    /// flag is useless in the scripted case it exists for.
    /// </summary>
    [Fact]
    public async Task ForceSkipsThePromptEntirely()
    {
        _github.AddIssue(42);
        _git.MatchingBranches = ["issues/42-fix-the-widget"];

        var command = Command();
        command.Force = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _console.ConfirmsAsked.Should().BeEmpty();
        _git.CreatedBranch.Should().StartWith("issues/42-");
    }

    #endregion

    [Fact]
    public async Task ItShowsTheIssueTitleAndLabels()
    {
        _github.AddIssue(42, "Fix the widget alignment", "bug", "ui");

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("Fix the widget alignment");
        _console.AllOutput.Should().Contain("bug, ui");
    }

    [Fact]
    public async Task ItSuggestsWhatToRunNext()
    {
        _github.AddIssue(42);

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("solve prd");
        _console.AllOutput.Should().Contain("solve work");
    }
}
