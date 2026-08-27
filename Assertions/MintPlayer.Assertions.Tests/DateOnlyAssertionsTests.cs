namespace MintPlayer.Assertions.Tests;

public class DateOnlyAssertionsTests
{
    private static readonly DateOnly Sample = new(2024, 3, 15);

    private static AssertionFailedException Fails(Action act)
    {
        var ex = Record.Exception(act);
        return Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void Be_Passes_And_Fails()
    {
        Sample.Should().Be(new DateOnly(2024, 3, 15));
        var value = Sample;
        var ex = Fails(() => value.Should().Be(new DateOnly(2024, 3, 16)));
        Assert.Contains("Expected value to be", ex.Message);
    }

    [Fact]
    public void Be_Fails_On_Null_Subject()
    {
        DateOnly? value = null;
        var ex = Fails(() => value.Should().Be(Sample));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_And_Fails()
    {
        Sample.Should().NotBe(new DateOnly(2024, 3, 16));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBe(Sample));
        Assert.Contains("Did not expect value to be", ex.Message);
    }

    [Fact]
    public void BeBefore_Passes_And_Fails()
    {
        Sample.Should().BeBefore(new DateOnly(2024, 3, 16));
        var value = Sample;
        var ex = Fails(() => value.Should().BeBefore(Sample));
        Assert.Contains("to be before", ex.Message);
    }

    [Fact]
    public void BeOnOrBefore_Passes_And_Fails()
    {
        Sample.Should().BeOnOrBefore(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrBefore(new DateOnly(2024, 3, 14)));
        Assert.Contains("to be on or before", ex.Message);
    }

    [Fact]
    public void BeAfter_Passes_And_Fails()
    {
        Sample.Should().BeAfter(new DateOnly(2024, 3, 14));
        var value = Sample;
        var ex = Fails(() => value.Should().BeAfter(Sample));
        Assert.Contains("to be after", ex.Message);
    }

    [Fact]
    public void BeOnOrAfter_Passes_And_Fails()
    {
        Sample.Should().BeOnOrAfter(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrAfter(new DateOnly(2024, 3, 16)));
        Assert.Contains("to be on or after", ex.Message);
    }

    [Fact]
    public void HaveYear_Passes_And_Fails()
    {
        Sample.Should().HaveYear(2024);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveYear(2023));
        Assert.Contains("to have year 2023", ex.Message);
        Assert.Contains("but found 2024", ex.Message);
    }

    [Fact]
    public void HaveYear_Fails_On_Null()
    {
        DateOnly? value = null;
        var ex = Fails(() => value.Should().HaveYear(2024));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void HaveMonth_Passes_And_Fails()
    {
        Sample.Should().HaveMonth(3);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveMonth(4));
        Assert.Contains("to have month 4", ex.Message);
    }

    [Fact]
    public void HaveDay_Passes_And_Fails()
    {
        Sample.Should().HaveDay(15);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveDay(16));
        Assert.Contains("to have day 16", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_And_Fails()
    {
        DateOnly? some = Sample;
        some.Should().HaveValue();
        DateOnly? none = null;
        var ex = Fails(() => none.Should().HaveValue());
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_And_Fails()
    {
        DateOnly? none = null;
        none.Should().NotHaveValue();
        DateOnly? some = Sample;
        var ex = Fails(() => some.Should().NotHaveValue());
        Assert.Contains("Did not expect some to have a value", ex.Message);
    }

    [Fact]
    public void BeOneOf_Passes_And_Fails()
    {
        Sample.Should().BeOneOf(new DateOnly(2024, 3, 14), Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOneOf(new DateOnly(2024, 3, 16), new DateOnly(2024, 3, 17)));
        Assert.Contains("to be one of", ex.Message);
    }
}
