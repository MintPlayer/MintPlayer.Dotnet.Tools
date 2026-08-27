using Solve.Models;
using Solve.Services;

namespace Solve.Tests;

/// <summary>
/// PlansDirectory and DocsDirectory are cwd-relative consts, so every test that touches disk
/// runs with the process working directory pointed at its own temp folder. That makes these
/// tests mutually exclusive, hence the single non-parallel collection: Directory.SetCurrentDirectory
/// is process-wide.
/// </summary>
[CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
public class WorkingDirectoryCollection;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class PrdGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"solve-prd-{Guid.NewGuid():N}");
    private readonly string _originalDirectory = Directory.GetCurrentDirectory();
    private readonly PrdGenerator _generator = new();

    public PrdGeneratorTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.SetCurrentDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static GitHubIssue Issue(int number = 42, string title = "Add a widget") => new()
    {
        Number = number,
        Title = title,
        Body = "The widget should do widget things.",
        State = "open",
        Author = "someone",
        Url = $"https://github.com/o/r/issues/{number}",
        Labels = ["enhancement"],
    };

    #region Paths

    [Fact]
    public void GetPlanPath_IsUnderTheClaudePlansDirectory()
        => _generator.GetPlanPath(42).Replace('\\', '/').Should().Be(".claude/plans/issue_42.md");

    [Fact]
    public void GetPrdPath_IsUnderDocs()
        => _generator.GetPrdPath(42).Replace('\\', '/').Should().Be("docs/issue_42_PRD.md");

    [Fact]
    public void PlanExists_IsFalseBeforeAnythingIsWritten()
        => _generator.PlanExists(42).Should().BeFalse();

    [Fact]
    public void PrdExists_IsFalseBeforeAnythingIsWritten()
        => _generator.PrdExists(42).Should().BeFalse();

    #endregion

    #region Generation

    [Fact]
    public async Task GeneratePlanAsync_IncludesTheIssueDetails()
    {
        var plan = await _generator.GeneratePlanAsync(Issue());

        plan.Should().Contain("Issue #42");
        plan.Should().Contain("Add a widget");
    }

    [Fact]
    public async Task GeneratePrdAsync_IncludesTheIssueDetails()
    {
        var prd = await _generator.GeneratePrdAsync(Issue());

        prd.Should().Contain("42");
        prd.Should().Contain("Add a widget");
    }

    [Fact]
    public async Task GeneratePlanAsync_ReflectsTheIssueTypeAndPriorityFromItsLabels()
    {
        var bug = Issue();
        bug.Labels = ["bug", "critical"];

        var plan = await _generator.GeneratePlanAsync(bug);

        plan.Should().Contain("Bug Fix");
        plan.Should().Contain("High");
    }

    [Fact]
    public async Task GeneratePrdAsync_StartsAsADraft()
    {
        var prd = await _generator.GeneratePrdAsync(Issue());

        prd.Should().Contain("**Status**: Draft");
    }

    [Fact]
    public async Task GeneratePlanAsync_ProducesNonEmptyMarkdown()
    {
        var plan = await _generator.GeneratePlanAsync(Issue());

        plan.Should().NotBeNullOrWhiteSpace();
        plan.Should().StartWith("#");
    }

    #endregion

    #region Saving

    [Fact]
    public async Task SavePlanAsync_WritesTheFileAndCreatesItsDirectory()
    {
        var path = await _generator.SavePlanAsync(42, "# Plan");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("# Plan");
        _generator.PlanExists(42).Should().BeTrue();
    }

    [Fact]
    public async Task SavePrdAsync_WritesTheFileAndCreatesItsDirectory()
    {
        var path = await _generator.SavePrdAsync(42, "# PRD");

        File.Exists(path).Should().BeTrue();
        _generator.PrdExists(42).Should().BeTrue();
    }

    [Fact]
    public async Task SavePlanAsync_RefusesToOverwriteWithoutForce()
    {
        await _generator.SavePlanAsync(42, "# First");

        var act = async () => await _generator.SavePlanAsync(42, "# Second");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*--force*");
    }

    [Fact]
    public async Task SavePlanAsync_OverwritesWithForce()
    {
        await _generator.SavePlanAsync(42, "# First");

        var path = await _generator.SavePlanAsync(42, "# Second", force: true);

        (await File.ReadAllTextAsync(path)).Should().Be("# Second");
    }

    [Fact]
    public async Task SavePrdAsync_RefusesToOverwriteWithoutForce()
    {
        await _generator.SavePrdAsync(42, "# First");

        var act = async () => await _generator.SavePrdAsync(42, "# Second");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*--force*");
    }

    [Fact]
    public async Task SavePrdAsync_OverwritesWithForce()
    {
        await _generator.SavePrdAsync(42, "# First");

        var path = await _generator.SavePrdAsync(42, "# Second", force: true);

        (await File.ReadAllTextAsync(path)).Should().Be("# Second");
    }

    [Fact]
    public async Task SavedFilesForDifferentIssuesDoNotCollide()
    {
        await _generator.SavePlanAsync(1, "# One");
        await _generator.SavePlanAsync(2, "# Two");

        (await File.ReadAllTextAsync(_generator.GetPlanPath(1))).Should().Be("# One");
        (await File.ReadAllTextAsync(_generator.GetPlanPath(2))).Should().Be("# Two");
    }

    #endregion

    #region ParseWorkStatusAsync

    private async Task WritePrd(string content) => await _generator.SavePrdAsync(42, content, force: true);

    [Fact]
    public async Task ParseWorkStatusAsync_WithNoPrd_ReturnsNull()
        => (await _generator.ParseWorkStatusAsync(42, "Add a widget")).Should().BeNull();

    [Fact]
    public async Task ParseWorkStatusAsync_ReadsTheStatusLine()
    {
        await WritePrd("""
            # PRD

            **Status**: In Progress
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status.Should().NotBeNull();
        status!.PrdStatus.Should().Be("In Progress");
    }

    [Fact]
    public async Task ParseWorkStatusAsync_CountsCheckedAndUncheckedItems()
    {
        await WritePrd("""
            # PRD

            - [x] First done
            - [x] Second done
            - [ ] Still to do
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.CompletedRequirements.Should().Be(2);
        status.TotalRequirements.Should().Be(3);
        status.CompletedItems.Should().Equal(["First done", "Second done"]);
        status.RemainingItems.Should().Equal(["Still to do"]);
    }

    [Fact]
    public async Task ParseWorkStatusAsync_DerivesTheImplementationStatus()
    {
        await WritePrd("""
            - [x] a
            - [ ] b
            - [ ] c
            - [ ] d
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.CompletionPercentage.Should().Be(25);
        status.ImplementationStatus.Should().Be("25% Complete");
    }

    [Fact]
    public async Task ParseWorkStatusAsync_ReportsNotStartedWhenNothingIsDone()
    {
        await WritePrd("""
            - [ ] a
            - [ ] b
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.ImplementationStatus.Should().Be("Not Started");
    }

    [Fact]
    public async Task ParseWorkStatusAsync_ReportsCompleteWhenEverythingIsDone()
    {
        await WritePrd("""
            - [x] a
            - [x] b
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.CompletionPercentage.Should().Be(100);
        status.ImplementationStatus.Should().Be("Complete");
    }

    [Fact]
    public async Task ParseWorkStatusAsync_ExtractsOpenQuestions()
    {
        await WritePrd("""
            # PRD

            - [x] Requirement one

            ## Open Questions

            - [ ] Should it also do X?
            - [ ] What about Y?

            ## Something Else

            - [ ] Not a question
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.OpenQuestions.Should().Equal(["Should it also do X?", "What about Y?"]);
    }

    [Fact]
    public async Task ParseWorkStatusAsync_WithNoOpenQuestionsSection_LeavesThemEmpty()
    {
        await WritePrd("""
            - [x] a
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.OpenQuestions.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseWorkStatusAsync_CarriesTheIssueNumberAndTitle()
    {
        await WritePrd("# PRD");

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.IssueNumber.Should().Be(42);
        status.IssueTitle.Should().Be("Add a widget");
    }

    [Fact]
    public async Task ParseWorkStatusAsync_OnAPrdWithNoCheckboxes_ReportsZeroRequirements()
    {
        await WritePrd("""
            # PRD

            Just prose, no checkboxes.
            """);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status!.TotalRequirements.Should().Be(0);
        status.CompletionPercentage.Should().Be(0);
        status.ImplementationStatus.Should().Be("Not Started");
    }

    [Fact]
    public async Task GenerateThenSaveThenParse_RoundTrips()
    {
        var prd = await _generator.GeneratePrdAsync(Issue());
        await _generator.SavePrdAsync(42, prd);

        var status = await _generator.ParseWorkStatusAsync(42, "Add a widget");

        status.Should().NotBeNull();
        status!.IssueNumber.Should().Be(42);
    }

    #endregion
}
