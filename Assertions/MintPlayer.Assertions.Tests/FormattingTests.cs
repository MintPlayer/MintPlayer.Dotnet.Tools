using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Formatter renders every value that appears in a failure message, so a bug here degrades
/// every assertion's diagnostics at once. It had no direct tests.
/// </summary>
public class FormatterTests
{
    private enum Colour { Red, Blue }

    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;
        public Node? Next { get; set; }
    }

    [Fact]
    public void Format_RendersNullDistinctly()
        => Formatter.Format(null).Should().Be("<null>");

    [Fact]
    public void Format_QuotesStrings()
        => Formatter.Format("abc").Should().Contain("abc").And.Contain("\"");

    [Fact]
    public void Format_QuotesCharsWithSingleQuotes()
        => Formatter.Format('x').Should().Be("'x'");

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Format_RendersBooleansLowercase(bool value, string expected)
        => Formatter.Format(value).Should().Be(expected);

    [Fact]
    public void Format_QualifiesEnumsWithTheirTypeName()
        => Formatter.Format(Colour.Blue).Should().Be("Colour.Blue");

    [Fact]
    public void Format_UsesRoundTripFormatForDateTime()
        => Formatter.Format(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc))
            .Should().StartWith("2026-08-27T12:00:00");

    [Fact]
    public void Format_UsesRoundTripFormatForDateOnly()
        => Formatter.Format(new DateOnly(2026, 8, 27)).Should().Be("2026-08-27");

    [Fact]
    public void Format_RendersNumbersInvariantly()
        => Formatter.Format(1234.5m).Should().Contain("1234.5");

    [Fact]
    public void Format_RendersACollection()
    {
        var text = Formatter.Format(new[] { 1, 2, 3 });

        text.Should().Contain("1");
        text.Should().Contain("2");
        text.Should().Contain("3");
    }

    [Fact]
    public void Format_TruncatesALongCollection()
    {
        var text = Formatter.Format(Enumerable.Range(0, 500).ToArray());

        // Capped at 32 items, so the output must not be 500 entries long.
        text.Length.Should().BeLessThan(2000);
    }

    [Fact]
    public void Format_TruncatesAVeryLongString()
    {
        var text = Formatter.Format(new string('a', 5000));

        text.Length.Should().BeLessThan(1000);
    }

    [Fact]
    public void Format_SurvivesACycle()
    {
        var first = new Node { Name = "first" };
        var second = new Node { Name = "second", Next = first };
        first.Next = second;

        // Cycle-safe by contract: this must terminate rather than stack-overflow.
        var act = () => Formatter.Format(first);

        act.Should().NotThrow();
    }

    [Fact]
    public void Format_IsDepthLimited()
    {
        var deepest = new Node { Name = "deep" };
        var node = deepest;
        for (var i = 0; i < 20; i++)
            node = new Node { Name = $"level{i}", Next = node };

        var text = Formatter.Format(node);

        // MaxDepth is 3, so the innermost name must not appear.
        text.Should().NotContain("deep\"");
    }

    [Fact]
    public void Format_NeverReturnsNull()
    {
        Formatter.Format(new object()).Should().NotBeNull();
        Formatter.Format(Array.Empty<int>()).Should().NotBeNull();
    }
}

/// <summary>
/// StringDifference is internal; reached through the InternalsVisibleTo on
/// MintPlayer.Assertions. It is what turns "the strings are not equal" into a message that
/// points at the offending character.
/// </summary>
public class StringDifferenceTests
{
    [Theory]
    [InlineData("abc", "abd", 2)]
    [InlineData("abc", "xbc", 0)]
    [InlineData("abc", "abc", 3)]
    [InlineData("abc", "abcdef", 3)]
    [InlineData("abcdef", "abc", 3)]
    [InlineData("", "abc", 0)]
    [InlineData("", "", 0)]
    public void IndexOfFirstMismatch_FindsTheFirstDifferingIndex(string left, string right, int expected)
        => StringDifference.IndexOfFirstMismatch(left, right).Should().Be(expected);

    [Fact]
    public void IndexOfFirstMismatch_IsOrdinal()
    {
        // Not culture-aware: 'a' and 'A' differ at index 0.
        StringDifference.IndexOfFirstMismatch("abc", "Abc").Should().Be(0);
    }

    [Fact]
    public void IndexOfFirstMismatch_IsSymmetric()
        => StringDifference.IndexOfFirstMismatch("abc", "abd")
            .Should().Be(StringDifference.IndexOfFirstMismatch("abd", "abc"));

    [Fact]
    public void Describe_NamesTheIndexAndShowsBothSides()
    {
        var description = StringDifference.Describe("abcDef", "abcXef");

        description.Should().Contain("index 3");
        description.Should().Contain("vs");
    }

    [Fact]
    public void Describe_WindowsALongStringAroundTheMismatch()
    {
        var actual = new string('a', 200) + "X" + new string('b', 200);
        var expected = new string('a', 200) + "Y" + new string('b', 200);

        var description = StringDifference.Describe(actual, expected);

        // The whole point of the window: a 400-character string must not be dumped whole.
        description.Length.Should().BeLessThan(200);
        description.Should().Contain("index 200");
    }

    [Fact]
    public void Describe_HandlesAPrefixRelationship()
    {
        var description = StringDifference.Describe("abc", "abcdef");

        description.Should().Contain("index 3");
    }
}
