using Solve.Commands;
using Solve.Tests._Fakes;

namespace Solve.Tests.Commands;

/// <summary>
/// <c>solve feedback</c> — pull unresolved PR review threads and hand them to Claude.
/// </summary>
public class FeedbackCommandTests
{
    private readonly FakeConsoleService _console = new();
    private readonly FakeGitService _git = new();
    private readonly FakeGitHubService _github = new();
    private readonly FakeClaudeService _claude = new();

    private FeedbackCommand Command(string? prUrl = null) => new(_console, _git, _github, _claude)
    {
        PrUrl = prUrl,
    };

    private const string Reviews = """{"threads":[{"isResolved":false,"body":"Rename this"}]}""";

    [Fact]
    public async Task ItLaunchesClaudeWithTheReviewData()
    {
        _github.PullRequestForBranch = (7, "Fix the widget", "https://github.com/o/r/pull/7");
        _github.ReviewsJson = Reviews;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _claude.Prompts.Should().ContainSingle();
        _claude.Prompts[0].Should().Contain("PR #7");
        _claude.Prompts[0].Should().Contain("Rename this");
    }

    /// <summary>
    /// The prompt tells Claude to look only at unresolved threads. Losing that line means
    /// re-litigating feedback the reviewer already accepted.
    /// </summary>
    [Fact]
    public async Task ThePromptScopesClaudeToUnresolvedThreads()
    {
        _github.PullRequestForBranch = (7, "Fix", "url");
        _github.ReviewsJson = Reviews;

        await Command().Execute(default);

        _claude.Prompts[0].Should().Contain("isResolved is false");
    }

    #region Determining the PR number

    [Theory]
    [InlineData("7")]
    [InlineData("#7")]
    [InlineData("https://github.com/owner/repo/pull/7")]
    [InlineData("https://github.com/owner/repo/pull/7/files")]
    public async Task ItAcceptsEveryPrReferenceForm(string reference)
    {
        _github.PullRequestForBranch = (99, "Wrong one", "url");
        _github.ReviewsJson = Reviews;

        await Command(reference).Execute(default);

        _claude.Prompts[0].Should().Contain("PR #7");
    }

    [Fact]
    public async Task ItFailsWithNoPrAndNoReference()
    {
        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("No PR found"));
    }

    /// <summary>
    /// A reference that parses to nothing usable must not silently become PR #0 and fetch reviews
    /// for a PR that does not exist.
    /// </summary>
    [Fact]
    public async Task ItFailsOnAReferenceItCannotParse()
    {
        var exitCode = await Command("not-a-pr").Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("Could not determine PR number"));
    }

    [Fact]
    public async Task ItFailsWhenTheRepositoryIsUnknown()
    {
        _github.PullRequestForBranch = (7, "Fix", "url");
        _git.RemoteInfo = (null, null);

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(1);
        _console.Errors.Should().Contain(e => e.Contains("repository info"));
    }

    #endregion

    /// <summary>
    /// No feedback is a success, not a failure — "nothing to do" is the common case on a PR that
    /// has just been opened, and a non-zero exit would break any script wrapping this.
    /// </summary>
    [Fact]
    public async Task NoFeedbackSucceedsAndExplainsWhy()
    {
        _github.PullRequestForBranch = (7, "Fix", "url");
        _github.ReviewsJson = null;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().Contain(w => w.Contains("No review feedback"));
        _claude.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task AssessOnlyPrintsTheRawDataAndStops()
    {
        _github.PullRequestForBranch = (7, "Fix", "url");
        _github.ReviewsJson = Reviews;

        var command = Command();
        command.AssessOnly = true;

        var exitCode = await command.Execute(default);

        exitCode.Should().Be(0);
        _console.AllOutput.Should().Contain("Rename this");
        _claude.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingClaudeCliPrintsTheFeedbackInstead()
    {
        _github.PullRequestForBranch = (7, "Fix", "url");
        _github.ReviewsJson = Reviews;
        _claude.IsAvailable = false;

        var exitCode = await Command().Execute(default);

        exitCode.Should().Be(0);
        _console.Warnings.Should().Contain(w => w.Contains("Claude CLI not found"));
        _console.AllOutput.Should().Contain("Rename this");
    }

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
