using Solve.Commands;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve pr</c> — build a PR body, preview it, push, and open the pull request.
/// </summary>
/// <remarks>
/// The template tests all go through the ORGANISATION template path
/// (<c>owner/.github/PULL_REQUEST_TEMPLATE.md</c>, served by the fake GitHub) rather than the
/// local-file path. <c>FindLocalPrTemplate</c> probes relative paths with
/// <see cref="File.Exists"/>, so exercising it would mean changing the process working directory —
/// shared mutable state that xUnit's parallel classes would race on. The org path reaches exactly
/// the same <c>ProcessPrTemplate</c> and <c>AutoCheckPrType</c> logic without that risk; the
/// unreachable half is the eight-entry path list, noted rather than contorted around.
/// </remarks>
public class PrCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();

    public PrCommandTests()
    {
        // The checklist is two prompts before anything happens; default to answering both yes so
        // each test states only the decision it is actually about.
        _console.DefaultConfirm = true;
        _git.CurrentBranch = "issues/42-fix-the-widget";
    }

    private PrCommand Command(string? issueUrl = null) => new(_console, _git, _github)
    {
        IssueUrl = issueUrl,
        SkipConfirmation = true,
    };

    [Fact]
    public async Task ItCreatesThePullRequest()
    {
        _github.AddIssue(42, "Fix the widget");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _github.CreatedPullRequest.Should().NotBeNull();
        _github.CreatedPullRequest!.Value.Title.Should().Be("Fix the widget");
        _console.AllOutput.Should().Contain("https://github.com/o/r/pull/1");
    }

    /// <summary>
    /// Pushing before creating matters: the PR cannot reference a branch the remote has not seen,
    /// and creating first would fail with a confusing GitHub-side error.
    /// </summary>
    [Fact]
    public async Task ItPushesTheBranchBeforeCreating()
    {
        _github.AddIssue(42);

        await Command().Execute(default);

        _git.Calls.Should().Contain("push -u");
        _github.CreatedPullRequest.Should().NotBeNull();
    }

    [Fact]
    public async Task ItStopsWhenThePushFails()
    {
        _github.AddIssue(42);
        _git.PushSucceeds = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("Failed to push"));
        _github.CreatedPullRequest.Should().BeNull();
    }

    [Fact]
    public async Task ItReportsAFailedCreation()
    {
        _github.AddIssue(42);
        _github.CreatedPullRequestUrl = null;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("Failed to create pull request"));
    }

    #region Options

    [Fact]
    public async Task ACustomTitleWinsOverTheIssueTitle()
    {
        _github.AddIssue(42, "Fix the widget");

        var command = Command();
        command.Title = "Something clearer";

        await command.Execute(default);

        _github.CreatedPullRequest!.Value.Title.Should().Be("Something clearer");
    }

    [Fact]
    public async Task ItFallsBackToTheDefaultBranch()
    {
        _github.AddIssue(42);
        _git.DefaultBranch = "development";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.BaseBranch.Should().Be("development");
    }

    [Fact]
    public async Task AnExplicitBaseWinsOverTheDefault()
    {
        _github.AddIssue(42);
        _git.DefaultBranch = "master";

        var command = Command();
        command.BaseBranch = "release/1.0";

        await command.Execute(default);

        _github.CreatedPullRequest!.Value.BaseBranch.Should().Be("release/1.0");
    }

    [Fact]
    public async Task DraftIsPassedThrough()
    {
        _github.AddIssue(42);

        var command = Command();
        command.Draft = true;

        await command.Execute(default);

        _github.CreatedPullRequest!.Value.Draft.Should().BeTrue();
        _console.AllOutput.Should().Contain("Draft: Yes");
    }

    [Fact]
    public async Task ItPreviewsBeforeCreating()
    {
        _github.AddIssue(42, "Fix the widget");

        await Command().Execute(default);

        _console.AllOutput.Should().Contain("=== PR Preview ===");
        _console.AllOutput.Should().Contain("issues/42-fix-the-widget");
    }

    #endregion

    #region Confirmation and checklist

    [Fact]
    public async Task DecliningTheFinalConfirmationCreatesNothing()
    {
        _github.AddIssue(42);

        var command = new PrCommand(_console, _git, _github) { SkipConfirmation = false };
        _console.ConfirmResponses.Enqueue(true);   // tested locally
        _console.ConfirmResponses.Enqueue(true);   // no uncommitted changes
        _console.ConfirmResponses.Enqueue(false);  // create this PR?

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(1);
        _github.CreatedPullRequest.Should().BeNull();
        _git.Calls.Should().NotContain("push -u");
    }

    [Fact]
    public async Task FailingTheTestedLocallyCheckAborts()
    {
        _github.AddIssue(42);

        var command = new PrCommand(_console, _git, _github) { SkipConfirmation = true };
        _console.ConfirmResponses.Enqueue(false);

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("test your changes"));
    }

    [Fact]
    public async Task FailingTheUncommittedChangesCheckAborts()
    {
        _github.AddIssue(42);

        var command = new PrCommand(_console, _git, _github) { SkipConfirmation = true };
        _console.ConfirmResponses.Enqueue(true);
        _console.ConfirmResponses.Enqueue(false);

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("commit or stash"));
    }

    [Fact]
    public async Task NoChecklistSkipsBothQuestions()
    {
        _github.AddIssue(42);

        var command = Command();
        command.NoChecklist = true;

        await command.Execute(default);

        _console.ConfirmsAsked.Should().BeEmpty();
    }

    #endregion

    #region PR type detection

    [Theory]
    [InlineData("bug", "Bug Fix")]
    [InlineData("bugfix", "Bug Fix")]
    [InlineData("documentation", "Documentation Update")]
    [InlineData("docs", "Documentation Update")]
    [InlineData("refactor", "Code Refactor")]
    [InlineData("test", "Test")]
    public async Task ItDerivesThePrTypeFromLabels(string label, string expectedType)
    {
        _github.AddIssue(42, "Fix", label);

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain($"- [x] {expectedType}");
    }

    /// <summary>
    /// Labels are authoritative; the branch name is only consulted when they say nothing. A branch
    /// called <c>issues/42-fix-...</c> would otherwise override an explicit "documentation" label.
    /// </summary>
    [Fact]
    public async Task LabelsBeatTheBranchName()
    {
        _git.CurrentBranch = "issues/42-fix-the-thing";
        _github.AddIssue(42, "Docs", "documentation");

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("- [x] Documentation Update");
    }

    [Theory]
    [InlineData("issues/42-fix-alignment", "Bug Fix")]
    [InlineData("issues/42-update-docs", "Documentation Update")]
    [InlineData("issues/42-refactor-core", "Code Refactor")]
    [InlineData("issues/42-add-tests", "Test")]
    [InlineData("issues/42-add-widgets", "Feature")]
    public async Task ItFallsBackToTheBranchName(string branch, string expectedType)
    {
        _git.CurrentBranch = branch;
        _github.AddIssue(42, "Something");

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain($"- [x] {expectedType}");
    }

    #endregion

    #region Generated body

    [Fact]
    public async Task TheBodyClosesTheIssue()
    {
        _github.AddIssue(42);

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("Fixes #42");
    }

    [Fact]
    public async Task TheBodyListsTheCommits()
    {
        _github.AddIssue(42);
        _git.Log = "first commit\nsecond commit";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("- first commit");
        _github.CreatedPullRequest!.Value.Body.Should().Contain("- second commit");
    }

    /// <summary>
    /// Ten is the cap; a 40-commit branch must not paste 40 lines into the PR body.
    /// </summary>
    [Fact]
    public async Task TheBodyCapsTheCommitListAtTen()
    {
        _github.AddIssue(42);
        _git.Log = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"commit {i}"));

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("- commit 10");
        _github.CreatedPullRequest!.Value.Body.Should().NotContain("- commit 11");
    }

    [Fact]
    public async Task TheBodyPromptsForADescriptionWhenThereAreNoCommits()
    {
        _github.AddIssue(42);
        _git.Log = "";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("<!-- Describe your changes here -->");
    }

    #endregion

    #region Repository template

    [Fact]
    public async Task ItUsesTheOrganisationTemplateWhenThereIsOne()
    {
        _github.AddIssue(42, "Fix the widget");
        _github.FileContents = "## Summary\n\nFixes #\n";

        await Command().Execute(default);

        _console.Infos.Should().Contain(i => i.Contains("Using PR template"));
        _github.CreatedPullRequest!.Value.Body.Should().Contain("## Summary");
    }

    /// <summary>
    /// A bare <c>Fixes #</c> in a template is the common hand-written form; leaving it unfilled
    /// means the merged PR never closes the issue.
    /// </summary>
    [Fact]
    public async Task ItFillsInABareIssueReference()
    {
        _github.AddIssue(42);
        _github.FileContents = "Fixes #\n";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("Fixes #42");
    }

    [Fact]
    public async Task ItFillsInAPlaceholderIssueReference()
    {
        _github.AddIssue(42);
        _github.FileContents = "Closes #[issue number]\n";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("Closes #42");
    }

    /// <summary>
    /// Every advertised placeholder form, single and doubled brace.
    /// </summary>
    /// <remarks>
    /// The doubled-brace cases are the ones that caught a real defect: substitution ran in
    /// dictionary insertion order, so <c>{issue_number}</c> matched the inside of
    /// <c>{{issue_number}}</c> and produced <c>{42}</c>. Every doubled form we document was
    /// broken. Fixed by substituting longest-placeholder-first.
    /// </remarks>
    [Theory]
    [InlineData("{issue_number}", "42")]
    [InlineData("{{issue_number}}", "42")]
    [InlineData("{{issue-number}}", "42")]
    [InlineData("{issueNumber}", "42")]
    [InlineData("{issue-number}", "42")]
    [InlineData("{issue_title}", "Fix the widget")]
    [InlineData("{{issue_title}}", "Fix the widget")]
    [InlineData("{issueTitle}", "Fix the widget")]
    [InlineData("{pr_type}", "Feature")]
    [InlineData("{{pr_type}}", "Feature")]
    [InlineData("{type}", "Feature")]
    [InlineData("{author}", "someone")]
    [InlineData("{{author}}", "someone")]
    public async Task ItSubstitutesEveryPlaceholderForm(string placeholder, string expected)
    {
        _git.CurrentBranch = "issues/42-add-widgets";
        _github.AddIssue(42, "Fix the widget");
        _github.FileContents = $"Value: {placeholder}";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain($"Value: {expected}");
    }

    [Fact]
    public async Task ItSubstitutesTheCommitListIntoATemplate()
    {
        _github.AddIssue(42);
        _git.Log = "first commit";
        _github.FileContents = "Changes:\n{changes}";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("- first commit");
    }

    [Fact]
    public async Task ItSubstitutesTheLabels()
    {
        _github.AddIssue(42, "Fix", "bug", "ui");
        _github.FileContents = "Labels: {labels}";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("Labels: bug, ui");
    }

    /// <summary>
    /// A template's own type checklist gets ticked to match the detected type, so the author does
    /// not have to remember to.
    /// </summary>
    [Fact]
    public async Task ItTicksTheTemplatesTypeCheckbox()
    {
        _github.AddIssue(42, "Fix", "bug");
        _github.FileContents = "- [ ] Feature\n- [ ] Bug Fix\n- [ ] Chore\n";

        await Command().Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().Contain("- [x] Bug Fix");
        _github.CreatedPullRequest!.Value.Body.Should().Contain("- [ ] Feature");
    }

    [Fact]
    public async Task NoTemplateForcesTheBuiltInBody()
    {
        _github.AddIssue(42);
        _github.FileContents = "## Custom template";

        var command = Command();
        command.NoTemplate = true;

        await command.Execute(default);

        _github.CreatedPullRequest!.Value.Body.Should().NotContain("## Custom template");
        _github.CreatedPullRequest!.Value.Body.Should().Contain("What type of PR is this?");
    }

    #endregion

    #region Guard clauses

    /// <summary>
    /// An existing PR is the single most likely mistake here — running <c>solve pr</c> twice — and
    /// it must not produce a duplicate.
    /// </summary>
    [Fact]
    public async Task ItRefusesWhenAPrAlreadyExists()
    {
        _github.AddIssue(42);
        _github.PullRequestForBranch = (7, "Fix the widget", "https://github.com/o/r/pull/7");

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Warnings.Should().Contain(w => w.Contains("already exists"));
        _github.CreatedPullRequest.Should().BeNull();
    }

    [Fact]
    public async Task ItFailsWhenTheIssueCannotBeDetermined()
    {
        _git.CurrentBranch = "master";

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("Could not determine"));
    }

    /// <summary>
    /// An issue the API cannot return is not fatal — the branch name gave us the number, so the PR
    /// can still be opened with a fallback title.
    /// </summary>
    [Fact]
    public async Task AnUnfetchableIssueStillProducesAPr()
    {
        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _github.CreatedPullRequest!.Value.Title.Should().Be("Fix issue #42");
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
