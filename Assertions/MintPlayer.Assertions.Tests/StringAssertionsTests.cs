namespace MintPlayer.Assertions.Tests;

public class StringAssertionsTests
{
    private static AssertionFailedException Fails(Action act)
    {
        var ex = Record.Exception(act);
        return Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void Be_Passes_On_Equal_Strings()
    {
        var value = "hello";
        value.Should().Be("hello");
    }

    [Fact]
    public void Be_Fails_With_First_Difference_Hint()
    {
        var value = "abcDef";
        var ex = Fails(() => value.Should().Be("abcXef"));
        Assert.Contains("Expected value to be \"abcXef\"", ex.Message);
        Assert.Contains("they differ at index 3", ex.Message);
        Assert.Contains("\"abcDef\"", ex.Message);
        Assert.Contains("\"abcXef\"", ex.Message);
    }

    [Fact]
    public void Be_Fails_With_Windowed_Excerpt_On_Long_Strings()
    {
        var actual = new string('a', 50) + "X" + new string('b', 50);
        var expected = new string('a', 50) + "Y" + new string('b', 50);
        var ex = Fails(() => actual.Should().Be(expected));
        Assert.Contains("they differ at index 50", ex.Message);
        Assert.Contains("…", ex.Message);
    }

    [Fact]
    public void Be_Fails_On_Null_Subject()
    {
        string? value = null;
        var ex = Fails(() => value.Should().Be("hello"));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void Be_Is_CaseSensitive()
    {
        var value = "Hello";
        var ex = Fails(() => value.Should().Be("hello"));
        Assert.Contains("differ at index 0", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_And_Fails()
    {
        "hello".Should().NotBe("world");
        var value = "hello";
        var ex = Fails(() => value.Should().NotBe("hello"));
        Assert.Contains("Did not expect value to be \"hello\"", ex.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_And_Fails()
    {
        "HELLO".Should().BeEquivalentTo("hello");
        var value = "hello";
        var ex = Fails(() => value.Should().BeEquivalentTo("world"));
        Assert.Contains("to be equivalent to \"world\"", ex.Message);
    }

    [Fact]
    public void NotBeEquivalentTo_Passes_And_Fails()
    {
        "hello".Should().NotBeEquivalentTo("world");
        var value = "HELLO";
        var ex = Fails(() => value.Should().NotBeEquivalentTo("hello"));
        Assert.Contains("Did not expect value to be equivalent to \"hello\"", ex.Message);
    }

    [Fact]
    public void BeEmpty_Passes_And_Fails()
    {
        "".Should().BeEmpty();
        var value = "x";
        var ex = Fails(() => value.Should().BeEmpty());
        Assert.Contains("to be empty", ex.Message);
    }

    [Fact]
    public void NotBeEmpty_Passes_And_Fails()
    {
        "x".Should().NotBeEmpty();
        var value = "";
        var ex = Fails(() => value.Should().NotBeEmpty());
        Assert.Contains("to be empty", ex.Message);
    }

    [Fact]
    public void NotBeEmpty_Fails_On_Null()
    {
        string? value = null;
        var ex = Fails(() => value.Should().NotBeEmpty());
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void BeNullOrEmpty_Passes_And_Fails()
    {
        ((string?)null).Should().BeNullOrEmpty();
        "".Should().BeNullOrEmpty();
        var value = "x";
        var ex = Fails(() => value.Should().BeNullOrEmpty());
        Assert.Contains("to be null or empty", ex.Message);
    }

    [Fact]
    public void NotBeNullOrEmpty_Passes_And_Fails()
    {
        "x".Should().NotBeNullOrEmpty();
        string? value = null;
        var ex = Fails(() => value.Should().NotBeNullOrEmpty());
        Assert.Contains("not to be null or empty", ex.Message);
    }

    [Fact]
    public void BeNullOrWhiteSpace_Passes_And_Fails()
    {
        "  \t".Should().BeNullOrWhiteSpace();
        var value = "x";
        var ex = Fails(() => value.Should().BeNullOrWhiteSpace());
        Assert.Contains("to be null or white-space", ex.Message);
    }

    [Fact]
    public void NotBeNullOrWhiteSpace_Passes_And_Fails()
    {
        "x".Should().NotBeNullOrWhiteSpace();
        var value = "   ";
        var ex = Fails(() => value.Should().NotBeNullOrWhiteSpace());
        Assert.Contains("not to be null or white-space", ex.Message);
    }

    [Fact]
    public void HaveLength_Passes_And_Fails()
    {
        "abc".Should().HaveLength(3);
        var value = "abc";
        var ex = Fails(() => value.Should().HaveLength(5));
        Assert.Contains("to have length 5", ex.Message);
        Assert.Contains("has length 3", ex.Message);
    }

    [Fact]
    public void HaveLength_Fails_On_Null()
    {
        string? value = null;
        var ex = Fails(() => value.Should().HaveLength(3));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void StartWith_Passes_And_Fails()
    {
        "hello world".Should().StartWith("hello");
        var value = "hello world";
        var ex = Fails(() => value.Should().StartWith("world"));
        Assert.Contains("to start with \"world\"", ex.Message);
    }

    [Fact]
    public void NotStartWith_Passes_And_Fails()
    {
        "hello world".Should().NotStartWith("world");
        var value = "hello world";
        var ex = Fails(() => value.Should().NotStartWith("hello"));
        Assert.Contains("Did not expect value to start with \"hello\"", ex.Message);
    }

    [Fact]
    public void StartWithEquivalentOf_Passes_And_Fails()
    {
        "Hello world".Should().StartWithEquivalentOf("hello");
        var value = "Hello world";
        var ex = Fails(() => value.Should().StartWithEquivalentOf("world"));
        Assert.Contains("to start with the equivalent of \"world\"", ex.Message);
    }

    [Fact]
    public void EndWith_Passes_And_Fails()
    {
        "hello world".Should().EndWith("world");
        var value = "hello world";
        var ex = Fails(() => value.Should().EndWith("hello"));
        Assert.Contains("to end with \"hello\"", ex.Message);
    }

    [Fact]
    public void NotEndWith_Passes_And_Fails()
    {
        "hello world".Should().NotEndWith("hello");
        var value = "hello world";
        var ex = Fails(() => value.Should().NotEndWith("world"));
        Assert.Contains("Did not expect value to end with \"world\"", ex.Message);
    }

    [Fact]
    public void EndWithEquivalentOf_Passes_And_Fails()
    {
        "hello WORLD".Should().EndWithEquivalentOf("world");
        var value = "hello WORLD";
        var ex = Fails(() => value.Should().EndWithEquivalentOf("hello"));
        Assert.Contains("to end with the equivalent of \"hello\"", ex.Message);
    }

    [Fact]
    public void Contain_Passes_And_Fails()
    {
        "hello world".Should().Contain("lo wo");
        var value = "hello world";
        var ex = Fails(() => value.Should().Contain("xyz"));
        Assert.Contains("to contain \"xyz\"", ex.Message);
    }

    [Fact]
    public void NotContain_Passes_And_Fails()
    {
        "hello world".Should().NotContain("xyz");
        var value = "hello world";
        var ex = Fails(() => value.Should().NotContain("world"));
        Assert.Contains("Did not expect value to contain \"world\"", ex.Message);
    }

    [Fact]
    public void ContainEquivalentOf_Passes_And_Fails()
    {
        "hello WORLD".Should().ContainEquivalentOf("world");
        var value = "hello world";
        var ex = Fails(() => value.Should().ContainEquivalentOf("xyz"));
        Assert.Contains("to contain the equivalent of \"xyz\"", ex.Message);
    }

    [Fact]
    public void NotContainEquivalentOf_Passes_And_Fails()
    {
        "hello world".Should().NotContainEquivalentOf("xyz");
        var value = "hello WORLD";
        var ex = Fails(() => value.Should().NotContainEquivalentOf("world"));
        Assert.Contains("Did not expect value to contain the equivalent of \"world\"", ex.Message);
    }

    [Fact]
    public void ContainAll_Passes_And_Fails()
    {
        "hello world".Should().ContainAll("hello", "world");
        var value = "hello world";
        var ex = Fails(() => value.Should().ContainAll("hello", "xyz"));
        Assert.Contains("to contain all of", ex.Message);
        Assert.Contains("could not find", ex.Message);
        Assert.Contains("\"xyz\"", ex.Message);
    }

    [Fact]
    public void ContainAny_Passes_And_Fails()
    {
        "hello world".Should().ContainAny("xyz", "world");
        var value = "hello world";
        var ex = Fails(() => value.Should().ContainAny("xyz", "abc"));
        Assert.Contains("to contain at least one of", ex.Message);
    }

    [Fact]
    public void Match_Passes_And_Fails()
    {
        "hello world".Should().Match("hello*");
        "hello".Should().Match("h?llo");
        var value = "hello world";
        var ex = Fails(() => value.Should().Match("bye*"));
        Assert.Contains("to match \"bye*\"", ex.Message);
    }

    [Fact]
    public void NotMatch_Passes_And_Fails()
    {
        "hello world".Should().NotMatch("bye*");
        var value = "hello world";
        var ex = Fails(() => value.Should().NotMatch("hello*"));
        Assert.Contains("Did not expect value to match \"hello*\"", ex.Message);
    }

    [Fact]
    public void MatchEquivalentOf_Passes_And_Fails()
    {
        "HELLO world".Should().MatchEquivalentOf("hello*");
        var value = "HELLO world";
        var ex = Fails(() => value.Should().MatchEquivalentOf("bye*"));
        Assert.Contains("to match the equivalent of \"bye*\"", ex.Message);
    }

    [Fact]
    public void MatchRegex_Passes_And_Fails()
    {
        "hello123".Should().MatchRegex(@"^[a-z]+\d+$");
        var value = "hello";
        var ex = Fails(() => value.Should().MatchRegex(@"^\d+$"));
        Assert.Contains("to match regex", ex.Message);
    }

    [Fact]
    public void NotMatchRegex_Passes_And_Fails()
    {
        "hello".Should().NotMatchRegex(@"^\d+$");
        var value = "12345";
        var ex = Fails(() => value.Should().NotMatchRegex(@"^\d+$"));
        Assert.Contains("Did not expect value to match regex", ex.Message);
    }

    [Fact]
    public void BeUpperCased_Passes_And_Fails()
    {
        "HELLO 123!".Should().BeUpperCased();
        var value = "Hello";
        var ex = Fails(() => value.Should().BeUpperCased());
        Assert.Contains("to be upper-cased", ex.Message);
    }

    [Fact]
    public void BeLowerCased_Passes_And_Fails()
    {
        "hello 123!".Should().BeLowerCased();
        var value = "Hello";
        var ex = Fails(() => value.Should().BeLowerCased());
        Assert.Contains("to be lower-cased", ex.Message);
    }

    [Fact]
    public void Because_Clause_Is_Woven_Into_The_Message()
    {
        var value = "hello";
        var ex = Fails(() => value.Should().Be("world", "the greeting {0} is required", "world"));
        Assert.Contains("because the greeting world is required", ex.Message);
    }

    [Fact]
    public void NotHaveLength_Passes_And_Fails()
    {
        "hello".Should().NotHaveLength(4);
        var value = "hello";
        var ex = Fails(() => value.Should().NotHaveLength(5));
        Assert.Equal("Did not expect value to have length 5.", ex.Message);
    }

    [Fact]
    public void NotHaveLength_Passes_On_Null()
    {
        string? value = null;
        value.Should().NotHaveLength(0);
    }

    [Fact]
    public void NotStartWithEquivalentOf_Passes_And_Fails()
    {
        "hello world".Should().NotStartWithEquivalentOf("world");
        // The positive form ignores casing, so the negative must too: "hello" still matches "Hello".
        var value = "Hello world";
        var ex = Fails(() => value.Should().NotStartWithEquivalentOf("hello"));
        Assert.Equal("Did not expect value to start with the equivalent of \"hello\".", ex.Message);
    }

    [Fact]
    public void NotStartWithEquivalentOf_Passes_On_Null_And_Rejects_A_Null_Argument()
    {
        string? value = null;
        value.Should().NotStartWithEquivalentOf("hello");
        var other = "hello";
        Assert.Throws<ArgumentNullException>(() => other.Should().NotStartWithEquivalentOf(null!));
    }

    [Fact]
    public void NotEndWithEquivalentOf_Passes_And_Fails()
    {
        "hello world".Should().NotEndWithEquivalentOf("hello");
        var value = "hello World";
        var ex = Fails(() => value.Should().NotEndWithEquivalentOf("world"));
        Assert.Equal("Did not expect value to end with the equivalent of \"world\".", ex.Message);
    }

    [Fact]
    public void NotEndWithEquivalentOf_Passes_On_Null_And_Rejects_A_Null_Argument()
    {
        string? value = null;
        value.Should().NotEndWithEquivalentOf("world");
        var other = "hello";
        Assert.Throws<ArgumentNullException>(() => other.Should().NotEndWithEquivalentOf(null!));
    }

    [Fact]
    public void NotMatchEquivalentOf_Passes_And_Fails()
    {
        "hello world".Should().NotMatchEquivalentOf("goodbye*");
        // Casing is ignored by MatchEquivalentOf, so "hello*" still matches "Hello World".
        var value = "Hello World";
        var ex = Fails(() => value.Should().NotMatchEquivalentOf("hello*"));
        Assert.Equal("Did not expect value to match the equivalent of \"hello*\", but found \"Hello World\".", ex.Message);
    }

    [Fact]
    public void NotMatchEquivalentOf_Passes_On_Null()
    {
        string? value = null;
        value.Should().NotMatchEquivalentOf("*");
    }

    [Fact]
    public void NotContainAll_Passes_When_One_Is_Missing()
        => "hello world".Should().NotContainAll("hello", "moon");

    [Fact]
    public void NotContainAll_Fails_When_Every_Value_Is_Present()
    {
        var value = "hello world";
        var ex = Fails(() => value.Should().NotContainAll("hello", "world"));
        Assert.Equal(
            "Did not expect value to contain all of {\"hello\", \"world\"}, but found every one of them in \"hello world\".",
            ex.Message);
    }

    [Fact]
    public void NotContainAll_Passes_On_Null_Subject()
    {
        string? value = null;
        value.Should().NotContainAll("hello");
    }

    [Fact]
    public void NotContainAny_Passes_When_None_Are_Present()
        => "hello world".Should().NotContainAny("moon", "sun");

    [Fact]
    public void NotContainAny_Fails_And_Lists_Only_The_Offenders()
    {
        var value = "hello world";
        var ex = Fails(() => value.Should().NotContainAny("moon", "world"));
        Assert.Equal(
            "Did not expect value to contain any of {\"moon\", \"world\"}, but found {\"world\"} in \"hello world\".",
            ex.Message);
    }

    [Fact]
    public void NotContainAny_Passes_On_Null_Subject_And_On_An_Empty_Set()
    {
        string? value = null;
        value.Should().NotContainAny("hello");
        "hello".Should().NotContainAny();
    }

    [Fact]
    public void NotBeUpperCased_Passes_And_Fails()
    {
        "Hello".Should().NotBeUpperCased();
        var value = "HELLO";
        var ex = Fails(() => value.Should().NotBeUpperCased());
        Assert.Equal("Did not expect value to be upper-cased, but found \"HELLO\".", ex.Message);
    }

    [Fact]
    public void NotBeUpperCased_Fails_For_A_String_Without_Letters()
    {
        // BeUpperCased calls "42" upper-cased, so its negation must reject "42" rather than
        // quietly pass on "there are no upper-case letters here".
        var value = "42";
        var ex = Fails(() => value.Should().NotBeUpperCased());
        Assert.Equal("Did not expect value to be upper-cased, but found \"42\".", ex.Message);
    }

    [Fact]
    public void NotBeUpperCased_Passes_On_Null()
    {
        string? value = null;
        value.Should().NotBeUpperCased();
    }

    [Fact]
    public void NotBeLowerCased_Passes_And_Fails()
    {
        "hellO".Should().NotBeLowerCased();
        var value = "hello";
        var ex = Fails(() => value.Should().NotBeLowerCased());
        Assert.Equal("Did not expect value to be lower-cased, but found \"hello\".", ex.Message);
    }

    [Fact]
    public void NotBeLowerCased_Fails_For_A_String_Without_Letters()
    {
        var value = "42";
        var ex = Fails(() => value.Should().NotBeLowerCased());
        Assert.Equal("Did not expect value to be lower-cased, but found \"42\".", ex.Message);
    }

    [Fact]
    public void NotBeLowerCased_Passes_On_Null()
    {
        string? value = null;
        value.Should().NotBeLowerCased();
    }

    [Fact]
    public void Chaining_With_And_Works()
    {
        "hello world".Should().StartWith("hello").And.EndWith("world").And.HaveLength(11);
    }
}
