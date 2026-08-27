using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// WildcardPattern is what backs string Match() and exception WithMessage(), so a bug here
/// silently changes what a large fraction of the library's assertions accept. It had zero
/// direct tests.
/// </summary>
public class WildcardPatternTests
{
    #region Literals

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("", "", true)]
    [InlineData("abc", "", false)]
    [InlineData("", "abc", false)]
    public void IsMatch_ComparesLiteralsExactly(string input, string pattern, bool expected)
        => WildcardPattern.IsMatch(input, pattern).Should().Be(expected);

    #endregion

    #region Star

    [Theory]
    [InlineData("abc", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("abc", "a*", true)]
    [InlineData("abc", "*c", true)]
    [InlineData("abc", "a*c", true)]
    [InlineData("ac", "a*c", true)]
    [InlineData("abc", "*b*", true)]
    [InlineData("abc", "*d*", false)]
    [InlineData("abc", "a*d", false)]
    public void IsMatch_TreatsStarAsAnySequenceIncludingEmpty(string input, string pattern, bool expected)
        => WildcardPattern.IsMatch(input, pattern).Should().Be(expected);

    [Fact]
    public void IsMatch_StarSpansNewlines()
        => WildcardPattern.IsMatch("first\nsecond", "first*second").Should().BeTrue();

    [Fact]
    public void IsMatch_HandlesConsecutiveStars()
        => WildcardPattern.IsMatch("abc", "a**c").Should().BeTrue();

    [Fact]
    public void IsMatch_HandlesTrailingStars()
        => WildcardPattern.IsMatch("abc", "abc***").Should().BeTrue();

    [Fact]
    public void IsMatch_BacktracksOverTheLastStar()
    {
        // The classic case a naive greedy matcher gets wrong.
        WildcardPattern.IsMatch("aaa", "*a").Should().BeTrue();
        WildcardPattern.IsMatch("aaab", "*a").Should().BeFalse();
        WildcardPattern.IsMatch("abcabc", "*abc").Should().BeTrue();
    }

    [Fact]
    public void IsMatch_HandlesAPatternOfOnlyStars()
        => WildcardPattern.IsMatch("anything at all", "****").Should().BeTrue();

    #endregion

    #region Question mark

    [Theory]
    [InlineData("abc", "a?c", true)]
    [InlineData("abc", "???", true)]
    [InlineData("abc", "??", false)]
    [InlineData("ab", "???", false)]
    [InlineData("a\nc", "a?c", true)]
    public void IsMatch_TreatsQuestionMarkAsExactlyOneCharacter(string input, string pattern, bool expected)
        => WildcardPattern.IsMatch(input, pattern).Should().Be(expected);

    [Fact]
    public void IsMatch_CombinesStarAndQuestionMark()
    {
        WildcardPattern.IsMatch("timed out after 30s", "*out after ??s").Should().BeTrue();
        WildcardPattern.IsMatch("timed out after 5s", "*out after ??s").Should().BeFalse();
    }

    #endregion

    #region Case sensitivity

    [Fact]
    public void IsMatch_IsCaseSensitiveByDefault()
        => WildcardPattern.IsMatch("ABC", "abc").Should().BeFalse();

    [Fact]
    public void IsMatch_WithIgnoreCase_MatchesRegardlessOfCase()
        => WildcardPattern.IsMatch("ABC", "abc", ignoreCase: true).Should().BeTrue();

    [Fact]
    public void IsMatch_WithIgnoreCase_StillHonoursStructure()
        => WildcardPattern.IsMatch("ABC", "a?", ignoreCase: true).Should().BeFalse();

    [Theory]
    [InlineData(StringComparison.Ordinal, false)]
    [InlineData(StringComparison.OrdinalIgnoreCase, true)]
    [InlineData(StringComparison.InvariantCulture, false)]
    [InlineData(StringComparison.InvariantCultureIgnoreCase, true)]
    [InlineData(StringComparison.CurrentCulture, false)]
    [InlineData(StringComparison.CurrentCultureIgnoreCase, true)]
    public void IsMatch_HonoursEveryStringComparison(StringComparison comparison, bool expected)
    {
        // All three IgnoreCase values must actually ignore case — the documented reason this
        // overload exists is that a caller asking for CurrentCultureIgnoreCase must not
        // silently get a case-sensitive match.
        WildcardPattern.IsMatch("ABC", "abc", comparison).Should().Be(expected);
    }

    #endregion

    #region Null

    [Fact]
    public void IsMatch_OnNullInput_IsFalse()
    {
        WildcardPattern.IsMatch(null, "*").Should().BeFalse();
        WildcardPattern.IsMatch(null, "").Should().BeFalse();
    }

    #endregion
}
