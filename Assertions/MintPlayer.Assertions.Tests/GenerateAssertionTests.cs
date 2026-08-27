namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Plain predicates: the source generator turns each one into a fluent assertion on the matching
/// assertions class (IsEven → BeEven on NumericAssertions&lt;int&gt;).
/// </summary>
public static class NumberPredicates
{
    [GenerateAssertion]
    public static bool IsEven(int value) => value % 2 == 0;

    [GenerateAssertion]
    public static bool IsDivisibleBy(int value, int divisor) => divisor != 0 && value % divisor == 0;

    [GenerateAssertion(Name = "BeAPalindrome")]
    public static bool IsPalindrome(string value) => value.SequenceEqual(value.Reverse());
}

public class GenerateAssertionTests
{
    [Fact]
    public void GeneratedAssertion_Passes_ForMatchingSubject()
    {
        4.Should().BeEven();
    }

    [Fact]
    public void GeneratedAssertion_Fails_ForNonMatchingSubject()
    {
        var ex = Record.Exception(() => 3.Should().BeEven());

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be even", ex!.Message);
        Assert.Contains("3", ex!.Message);
    }

    [Fact]
    public void GeneratedAssertion_Weaves_TheReason()
    {
        var ex = Record.Exception(() => 3.Should().BeEven("we only accept {0} numbers", "even"));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("because we only accept even numbers", ex!.Message);
    }

    [Fact]
    public void GeneratedAssertion_ForwardsExtraParameters()
    {
        9.Should().BeDivisibleBy(3);

        var ex = Record.Exception(() => 9.Should().BeDivisibleBy(2));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be divisible by", ex!.Message);
    }

    [Fact]
    public void GeneratedAssertion_UsesTheExplicitName_OnStringSubjects()
    {
        "racecar".Should().BeAPalindrome();

        var ex = Record.Exception(() => "assertion".Should().BeAPalindrome());

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be a palindrome", ex!.Message);
    }
}
