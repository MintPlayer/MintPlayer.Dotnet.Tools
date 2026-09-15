using System.Text.RegularExpressions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// M4: occurrence-constrained containment, line assertions, pre-built Regex overloads, and the
/// replacement of greedy unordered-collection matching with a maximum bipartite matching.
/// </summary>
public class Phase2StringAndMatchingTests
{
    #region Occurrence constraints

    [Fact]
    public void Contain_WithAnExactCount()
    {
        "a-b-a-c-a".Should().Contain("a", Exactly.Thrice());
        "a-b-a-c-a".Should().Contain("a", AtLeast.Twice());
        "a-b-a-c-a".Should().Contain("a", AtMost.Thrice());
        "a-b-a-c-a".Should().Contain("a", MoreThan.Twice());
        "a-b-a-c-a".Should().Contain("a", LessThan.Times(4));
    }

    [Fact]
    public void Contain_WithACount_Fails_AndReportsBothNumbers()
    {
        var ex = Record.Exception(() => "a-b-a".Should().Contain("a", Exactly.Thrice()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("exactly 3 time(s)", ex.Message);
        Assert.Contains("found it 2 time(s)", ex.Message);
    }

    /// <summary>
    /// Overlapping matches are not counted: the scan resumes past the whole match, so "aa" occurs
    /// once in "aaa". That is what a test means by "appears twice", and it needs pinning because
    /// the other reading is equally defensible in the abstract.
    /// </summary>
    [Fact]
    public void OccurrenceCounting_DoesNotCountOverlaps()
        => "aaa".Should().Contain("aa", Exactly.Once());

    [Fact]
    public void ContainEquivalentOf_WithACount_IgnoresCase()
        => "Ab-aB-AB".Should().ContainEquivalentOf("ab", Exactly.Thrice());

    [Fact]
    public void MatchRegex_WithACount()
    {
        "a1 b2 c3".Should().MatchRegex(@"\d", Exactly.Thrice());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "a1".Should().MatchRegex(@"\d", Exactly.Twice())));
    }

    /// <summary>A default(OccurrenceConstraint) has no comparison and must say so, not silently pass.</summary>
    [Fact]
    public void ADefaultOccurrenceConstraint_Throws()
    {
        var ex = Record.Exception(() => "abc".Should().Contain("a", default(OccurrenceConstraint)));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("never given a comparison", ex.Message);
    }

    [Fact]
    public void ANegativeOccurrenceCount_Throws()
        => Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => Exactly.Times(-1)));

    #endregion

    #region Pre-built Regex

    private static readonly Regex Digits = new(@"\d+", RegexOptions.None);

    [Fact]
    public void MatchRegex_AcceptsAPreBuiltRegex()
    {
        "abc123".Should().MatchRegex(Digits);
        "abc".Should().NotMatchRegex(Digits);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "abc".Should().MatchRegex(Digits)));
    }

    [Fact]
    public void MatchRegex_AcceptsAPreBuiltRegexWithACount()
        => "a1 b22 c333".Should().MatchRegex(Digits, Exactly.Thrice());

    #endregion

    #region Lines

    private static readonly string ThreeLines = string.Join(Environment.NewLine, "first", "second", "third");

    [Fact]
    public void HaveLineCount()
    {
        ThreeLines.Should().HaveLineCount(3);
        ThreeLines.Should().NotHaveLineCount(4);

        var ex = Record.Exception(() => ThreeLines.Should().HaveLineCount(2));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have 2 line(s)", ex.Message);
        Assert.Contains("but found 3", ex.Message);
    }

    [Fact]
    public void ContainLine_ComparesWholeLines()
    {
        ThreeLines.Should().ContainLine("second");
        ThreeLines.Should().NotContainLine("seco");

        // The distinction Contain cannot make: "seco" IS a substring, but it is not a line.
        ThreeLines.Should().Contain("seco");
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => ThreeLines.Should().ContainLine("seco")));
    }

    /// <summary>
    /// Line endings are not part of the comparison, so a fixture written with LF matches output
    /// produced with CRLF. Without this, the same assertion passes on one OS and fails on another.
    /// </summary>
    [Fact]
    public void LineAssertions_AreIndifferentToNewlineStyle()
    {
        "a\nb\nc".Should().HaveLineCount(3).And.ContainLine("b");
        "a\r\nb\r\nc".Should().HaveLineCount(3).And.ContainLine("b");
    }

    #endregion

    #region Unordered collection matching

    private sealed class Item(string name)
    {
        public string Name { get; } = name;
    }

    /// <summary>
    /// The case greedy first-fit gets WRONG. Expectations [wildcard-ish A, B] against subjects
    /// [X, Y]: A is equivalent to both, B only to X. Greedy hands X to A, leaves B unmatched and
    /// reports a difference — even though the perfect matching A→Y, B→X exists. Maximum matching
    /// finds it.
    /// </summary>
    [Fact]
    public void UnorderedMatching_FindsAPerfectMatchingGreedyWouldMiss()
    {
        // Subjects: two items. Expectation order is what traps greedy: the expectation that can
        // match either comes first.
        var subject = new[] { new Item("x"), new Item("y") };
        var expectation = new[] { new Item("y"), new Item("x") };

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void UnorderedMatching_StillReportsAGenuineMismatch()
    {
        var subject = new[] { new Item("x"), new Item("y") };
        var expectation = new[] { new Item("x"), new Item("z") };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void UnorderedMatching_ReportsExtraSubjectItems()
    {
        var subject = new[] { new Item("x"), new Item("y") };
        var expectation = new[] { new Item("x") };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("unexpected item", ex.Message);
    }

    #endregion
}
