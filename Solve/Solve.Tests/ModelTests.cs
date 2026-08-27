using Solve.Models;

namespace Solve.Tests;

public class IssueReferenceTests
{
    #region Parse

    [Theory]
    [InlineData("https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/123")]
    [InlineData("http://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/123")]
    public void Parse_ReadsAFullUrl(string input)
    {
        var reference = IssueReference.Parse(input);

        reference.Should().NotBeNull();
        reference!.Owner.Should().Be("MintPlayer");
        reference.Repo.Should().Be("MintPlayer.Dotnet.Tools");
        reference.Number.Should().Be(123);
    }

    [Fact]
    public void Parse_IgnoresTrailingUrlSegments()
    {
        var reference = IssueReference.Parse("https://github.com/o/r/issues/7#issuecomment-99");

        reference!.Number.Should().Be(7);
        reference.Owner.Should().Be("o");
        reference.Repo.Should().Be("r");
    }

    [Fact]
    public void Parse_ReadsAShortReference()
    {
        var reference = IssueReference.Parse("MintPlayer/MintPlayer.Dotnet.Tools#42");

        reference.Should().NotBeNull();
        reference!.Owner.Should().Be("MintPlayer");
        reference.Repo.Should().Be("MintPlayer.Dotnet.Tools");
        reference.Number.Should().Be(42);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("#123")]
    public void Parse_ReadsABareNumberAndFallsBackToTheCurrentRepo(string input)
    {
        var reference = IssueReference.Parse(input, "owner", "repo");

        reference.Should().NotBeNull();
        reference!.Owner.Should().Be("owner");
        reference.Repo.Should().Be("repo");
        reference.Number.Should().Be(123);
    }

    [Fact]
    public void Parse_WithABareNumberAndNoContext_LeavesOwnerAndRepoNull()
    {
        var reference = IssueReference.Parse("5");

        reference.Should().NotBeNull();
        reference!.Owner.Should().BeNull();
        reference.Repo.Should().BeNull();
        reference.Number.Should().Be(5);
    }

    [Fact]
    public void Parse_TrimsWhitespace()
        => IssueReference.Parse("  #9  ")!.Number.Should().Be(9);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-reference")]
    [InlineData("owner/repo")]
    [InlineData("https://gitlab.com/o/r/issues/1")]
    [InlineData("#abc")]
    public void Parse_OnUnrecognizedInput_ReturnsNull(string? input)
        => IssueReference.Parse(input).Should().BeNull();

    [Fact]
    public void Parse_PrefersTheUrlOverTheCurrentRepoContext()
    {
        var reference = IssueReference.Parse(
            "https://github.com/other/elsewhere/issues/1", "ignored", "ignored");

        reference!.Owner.Should().Be("other");
        reference.Repo.Should().Be("elsewhere");
    }

    #endregion

    #region ExtractFromBranchName

    [Theory]
    [InlineData("issues/123", 123)]
    [InlineData("issues/#123", 123)]
    [InlineData("issues/123-add-a-thing", 123)]
    [InlineData("feature/issue-123-add-a-thing", 123)]
    [InlineData("feature/issues-123", 123)]
    [InlineData("bugfix/ISSUE-99", 99)]
    public void ExtractFromBranchName_FindsTheNumber(string branch, int expected)
        => IssueReference.ExtractFromBranchName(branch).Should().Be(expected);

    [Theory]
    [InlineData("master")]
    [InlineData("feature/no-number-here")]
    [InlineData("issues/abc")]
    [InlineData("")]
    public void ExtractFromBranchName_WithNoNumber_ReturnsNull(string branch)
        => IssueReference.ExtractFromBranchName(branch).HasValue.Should().BeFalse();

    [Fact]
    public void ExtractFromBranchName_PrefersTheIssuesPrefixPattern()
    {
        // The issues/ pattern is tried first, so a branch matching both resolves to it.
        IssueReference.ExtractFromBranchName("issues/10-fixes-issue-20").Should().Be(10);
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_WithOwnerAndRepo_IsAShortReference()
        => new IssueReference { Owner = "o", Repo = "r", Number = 3 }.ToString().Should().Be("o/r#3");

    [Fact]
    public void ToString_WithoutOwnerOrRepo_IsJustTheNumber()
        => new IssueReference { Number = 3 }.ToString().Should().Be("#3");

    [Fact]
    public void ToString_WithOnlyAnOwner_IsJustTheNumber()
        => new IssueReference { Owner = "o", Number = 3 }.ToString().Should().Be("#3");

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var original = new IssueReference { Owner = "o", Repo = "r", Number = 3 };

        var parsed = IssueReference.Parse(original.ToString());

        parsed!.Owner.Should().Be("o");
        parsed.Repo.Should().Be("r");
        parsed.Number.Should().Be(3);
    }

    #endregion
}

public class GitHubIssueTests
{
    private static GitHubIssue WithLabels(params string[] labels)
        => new() { Labels = [.. labels] };

    #region GetIssueType

    [Theory]
    [InlineData("bug", "Bug Fix")]
    [InlineData("bugfix", "Bug Fix")]
    [InlineData("enhancement", "Feature")]
    [InlineData("feature", "Feature")]
    [InlineData("documentation", "Documentation")]
    [InlineData("docs", "Documentation")]
    [InlineData("refactor", "Refactor")]
    [InlineData("refactoring", "Refactor")]
    [InlineData("test", "Test")]
    [InlineData("testing", "Test")]
    public void GetIssueType_MapsEachKnownLabel(string label, string expected)
        => WithLabels(label).GetIssueType().Should().Be(expected);

    [Fact]
    public void GetIssueType_IsCaseInsensitive()
        => WithLabels("BUG").GetIssueType().Should().Be("Bug Fix");

    [Fact]
    public void GetIssueType_WithNoLabels_DefaultsToFeature()
        => WithLabels().GetIssueType().Should().Be("Feature");

    [Fact]
    public void GetIssueType_WithAnUnknownLabel_DefaultsToFeature()
        => WithLabels("wontfix").GetIssueType().Should().Be("Feature");

    [Fact]
    public void GetIssueType_PrefersBugOverEverythingElse()
    {
        // The checks are ordered, so a bug label wins over a docs label.
        WithLabels("documentation", "bug").GetIssueType().Should().Be("Bug Fix");
    }

    #endregion

    #region GetPriority

    [Theory]
    [InlineData("critical", "High")]
    [InlineData("urgent", "High")]
    [InlineData("p0", "High")]
    [InlineData("priority: high", "High")]
    [InlineData("p1", "High")]
    [InlineData("low priority", "Low")]
    [InlineData("p3", "Low")]
    public void GetPriority_MapsEachKnownLabel(string label, string expected)
        => WithLabels(label).GetPriority().Should().Be(expected);

    [Fact]
    public void GetPriority_MatchesOnSubstrings()
        => WithLabels("something-critical-here").GetPriority().Should().Be("High");

    [Fact]
    public void GetPriority_WithNoLabels_DefaultsToMedium()
        => WithLabels().GetPriority().Should().Be("Medium");

    [Fact]
    public void GetPriority_PrefersHighOverLow()
        => WithLabels("low", "critical").GetPriority().Should().Be("High");

    #endregion

    #region GetSlug

    [Theory]
    [InlineData("Add a widget", "add-a-widget")]
    [InlineData("Add   a   widget", "add-a-widget")]
    [InlineData("Fix: the thing (again)!", "fix-the-thing-again")]
    [InlineData("UPPERCASE TITLE", "uppercase-title")]
    [InlineData("with123numbers", "with123numbers")]
    public void GetSlug_ProducesAUrlSafeSlug(string title, string expected)
        => new GitHubIssue { Title = title }.GetSlug().Should().Be(expected);

    [Fact]
    public void GetSlug_TruncatesToTheMaxLength()
    {
        var slug = new GitHubIssue { Title = "a very long issue title that keeps going and going" }.GetSlug(20);

        slug.Length.Should().BeLessThanOrEqualTo(20);
        slug.Should().NotEndWith("-");
    }

    [Fact]
    public void GetSlug_HonoursACustomMaxLength()
        => new GitHubIssue { Title = "abcdefghij" }.GetSlug(5).Should().Be("abcde");

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("---")]
    public void GetSlug_WithNothingUsable_FallsBackToIssue(string title)
        => new GitHubIssue { Title = title }.GetSlug().Should().Be("issue");

    [Fact]
    public void GetSlug_DoesNotStartOrEndWithAHyphen()
    {
        var slug = new GitHubIssue { Title = "  !leading and trailing!  " }.GetSlug();

        slug.Should().NotStartWith("-");
        slug.Should().NotEndWith("-");
    }

    #endregion
}

public class WorkStatusTests
{
    [Theory]
    [InlineData(10, 5, 50)]
    [InlineData(3, 1, 33)]
    [InlineData(4, 4, 100)]
    [InlineData(7, 0, 0)]
    public void CompletionPercentage_IsIntegerDivision(int total, int completed, int expected)
        => new WorkStatus { TotalRequirements = total, CompletedRequirements = completed }
            .CompletionPercentage.Should().Be(expected);

    [Fact]
    public void CompletionPercentage_WithNoRequirements_IsZeroRatherThanAThrow()
    {
        // Guarded, unlike PaginationResponse.TotalPages was: worth pinning so it stays guarded.
        new WorkStatus().CompletionPercentage.Should().Be(0);
    }

    [Fact]
    public void CompletionPercentage_CanExceedOneHundredIfDataIsInconsistent()
    {
        // Characterization: nothing clamps it. Documented rather than "fixed", because the
        // right answer for completed > total is a data problem, not a formula problem.
        new WorkStatus { TotalRequirements = 2, CompletedRequirements = 4 }
            .CompletionPercentage.Should().Be(200);
    }

    [Fact]
    public void TheDefaultsAreSafeToRead()
    {
        var status = new WorkStatus();

        status.IssueTitle.Should().BeEmpty();
        status.PrdStatus.Should().Be("Not Started");
        status.ImplementationStatus.Should().Be("Not Started");
        status.CompletedItems.Should().BeEmpty();
        status.RemainingItems.Should().BeEmpty();
        status.OpenQuestions.Should().BeEmpty();
        status.Blockers.Should().BeEmpty();
        status.HasUncommittedChanges.Should().BeFalse();
        status.OpenPrUrl.Should().BeNull();
    }
}
