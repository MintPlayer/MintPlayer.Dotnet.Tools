using MintPlayer.StringExtensions;

namespace MintPlayer.StringExtensions.Tests;

public class CasingTests
{
    #region UcFirst / LcFirst

    [Theory]
    [InlineData("hello", "Hello")]
    [InlineData("Hello", "Hello")]
    [InlineData("h", "H")]
    [InlineData("1abc", "1abc")]
    [InlineData("hello world", "Hello world")]
    public void UcFirst_UpperCasesOnlyTheFirstCharacter(string input, string expected)
        => input.UcFirst().Should().Be(expected);

    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("hello", "hello")]
    [InlineData("H", "h")]
    [InlineData("HELLO", "hELLO")]
    public void LcFirst_LowerCasesOnlyTheFirstCharacter(string input, string expected)
        => input.LcFirst().Should().Be(expected);

    [Fact]
    public void UcFirst_OnEmptyOrNull_ReturnsEmpty()
    {
        string.Empty.UcFirst().Should().Be(string.Empty);
        ((string)null!).UcFirst().Should().Be(string.Empty);
    }

    [Fact]
    public void LcFirst_OnEmptyOrNull_ReturnsEmpty()
    {
        string.Empty.LcFirst().Should().Be(string.Empty);
        ((string)null!).LcFirst().Should().Be(string.Empty);
    }

    [Fact]
    public void UcFirst_IsCultureInvariant()
    {
        // Turkish dotless i: ToUpper in tr-TR would give U+0130, ToUpperInvariant gives 'I'.
        "istanbul".UcFirst().Should().Be("Istanbul");
    }

    #endregion

    #region camel -> snake / kebab

    [Theory]
    [InlineData("helloWorld", "hello_world")]
    [InlineData("HelloWorld", "hello_world")]
    [InlineData("hello", "hello")]
    [InlineData("h", "h")]
    [InlineData("aBCd", "a_b_cd")]
    public void Camel2Snake_InsertsUnderscoresBeforeCapitals(string input, string expected)
        => input.Camel2Snake().Should().Be(expected);

    [Theory]
    [InlineData("helloWorld", "hello-world")]
    [InlineData("HelloWorld", "hello-world")]
    [InlineData("hello", "hello")]
    [InlineData("aBCd", "a-b-cd")]
    public void Camel2Kebab_InsertsHyphensBeforeCapitals(string input, string expected)
        => input.Camel2Kebab().Should().Be(expected);

    [Fact]
    public void Camel2Snake_OnEmptyOrNull_ReturnsEmpty()
    {
        string.Empty.Camel2Snake().Should().Be(string.Empty);
        ((string)null!).Camel2Snake().Should().Be(string.Empty);
    }

    [Fact]
    public void Camel2Kebab_OnSingleCharacter_ReturnsItUnchanged()
        => "A".Camel2Kebab().Should().Be("A");

    #endregion

    #region snake -> camel / kebab

    [Theory]
    [InlineData("hello_world", "helloWorld")]
    [InlineData("HELLO_WORLD", "helloWorld")]
    [InlineData("hello", "hello")]
    [InlineData("hello__world", "helloWorld")]
    [InlineData("_hello_world", "helloWorld")]
    public void Snake2Camel_LowerCasesTheFirstWordAndCapitalizesTheRest(string input, string expected)
        => input.Snake2Camel().Should().Be(expected);

    [Fact]
    public void Snake2Camel_OnEmptyOrNull_ReturnsEmpty()
    {
        string.Empty.Snake2Camel().Should().Be(string.Empty);
        ((string)null!).Snake2Camel().Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("hello_world", "hello-world")]
    [InlineData("a_b_c", "a-b-c")]
    [InlineData("hello", "hello")]
    public void Snake2Kebab_SwapsTheSeparator(string input, string expected)
        => input.Snake2Kebab().Should().Be(expected);

    #endregion

    #region kebab -> camel / snake

    /// <summary>
    /// Regression for D1 in docs/PRD-TestCoverage.md. Kebab2Camel used to UcFirst every
    /// word including the first, so it returned PascalCase from a method named Camel —
    /// and disagreed with its own sibling Snake2Camel on identical input.
    /// </summary>
    [Theory]
    [InlineData("hello-world", "helloWorld")]
    [InlineData("HELLO-WORLD", "helloWorld")]
    [InlineData("hello", "hello")]
    [InlineData("hello--world", "helloWorld")]
    [InlineData("-hello-world", "helloWorld")]
    [InlineData("a-b-c", "aBC")]
    public void Kebab2Camel_LowerCasesTheFirstWord(string input, string expected)
        => input.Kebab2Camel().Should().Be(expected);

    [Fact]
    public void Kebab2Camel_AgreesWithSnake2Camel_OnEquivalentInput()
        => "some-long-name".Kebab2Camel().Should().Be("some_long_name".Snake2Camel());

    [Fact]
    public void Kebab2Camel_OnEmptyOrNull_ReturnsEmpty()
    {
        string.Empty.Kebab2Camel().Should().Be(string.Empty);
        ((string)null!).Kebab2Camel().Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("hello-world", "hello_world")]
    [InlineData("a-b-c", "a_b_c")]
    [InlineData("hello", "hello")]
    public void Kebab2Snake_SwapsTheSeparator(string input, string expected)
        => input.Kebab2Snake().Should().Be(expected);

    #endregion

    #region Round-trips

    [Theory]
    [InlineData("helloWorld")]
    [InlineData("someLongerPropertyName")]
    public void Camel2Snake_RoundTripsThroughSnake2Camel(string input)
        => input.Camel2Snake().Snake2Camel().Should().Be(input);

    [Theory]
    [InlineData("helloWorld")]
    [InlineData("someLongerPropertyName")]
    public void Camel2Kebab_RoundTripsThroughKebab2Camel(string input)
        => input.Camel2Kebab().Kebab2Camel().Should().Be(input);

    #endregion

    #region NthIndexOf

    /// <summary>
    /// Regression for D1 in docs/PRD-TestCoverage.md. This method was completely broken:
    /// it started at index -1, and String.IndexOf(char, -1) throws
    /// ArgumentOutOfRangeException — so EVERY call threw, for every input. Had that been
    /// fixed to 0 in isolation it would then have looped without advancing past the match,
    /// returning the index of the first occurance for every requested occurance.
    /// </summary>
    [Theory]
    [InlineData("a-b-c-d", '-', 1, 1)]
    [InlineData("a-b-c-d", '-', 2, 3)]
    [InlineData("a-b-c-d", '-', 3, 5)]
    [InlineData("a-b-c-d", '-', 4, -1)]
    [InlineData("---", '-', 1, 0)]
    [InlineData("---", '-', 2, 1)]
    [InlineData("---", '-', 3, 2)]
    [InlineData("abc", 'a', 1, 0)]
    [InlineData("abc", 'c', 1, 2)]
    public void NthIndexOf_ReturnsTheIndexOfTheNthOccurance(string input, char c, int occurance, int expected)
        => input.NthIndexOf(c, occurance).Should().Be(expected);

    [Fact]
    public void NthIndexOf_WhenTheCharacterIsAbsent_ReturnsMinusOne()
        => "abc".NthIndexOf('z', 1).Should().Be(-1);

    [Fact]
    public void NthIndexOf_OnEmptyString_ReturnsMinusOne()
        => string.Empty.NthIndexOf('a', 1).Should().Be(-1);

    [Fact]
    public void NthIndexOf_DoesNotReturnTheFirstIndexForEveryOccurance()
    {
        // The distinguishing assertion: a non-advancing loop returns 1 for all three.
        var indices = new[] { 1, 2, 3 }.Select(n => "a-b-c-d".NthIndexOf('-', n)).ToList();
        indices.Should().Equal([1, 3, 5]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NthIndexOf_WithNonPositiveOccurance_Throws(int occurance)
    {
        var act = () => "abc".NthIndexOf('b', occurance);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NthIndexOf_OnNull_Throws()
    {
        var act = () => ((string)null!).NthIndexOf('a', 1);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Scramble

    [Fact]
    public void Scramble_PreservesLengthAndCharacterCounts()
    {
        const string input = "abcdefghij";
        var result = input.Scramble();

        result.Should().HaveLength(input.Length);
        result.OrderBy(c => c).Should().Equal(input.OrderBy(c => c));
    }

    [Fact]
    public void Scramble_OnEmptyString_ReturnsEmpty()
        => string.Empty.Scramble().Should().Be(string.Empty);

    [Fact]
    public void Scramble_OnSingleCharacter_ReturnsItUnchanged()
        => "x".Scramble().Should().Be("x");

    [Fact]
    public void Scramble_PreservesRepeatedCharacters()
        => "aaab".Scramble().OrderBy(c => c).Should().Equal("aaab".OrderBy(c => c));

    #endregion
}
