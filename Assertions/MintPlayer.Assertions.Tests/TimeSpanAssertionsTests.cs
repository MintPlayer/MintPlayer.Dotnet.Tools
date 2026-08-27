namespace MintPlayer.Assertions.Tests;

public class TimeSpanAssertionsTests
{
    private static readonly TimeSpan Sample = TimeSpan.FromMinutes(90);

    private static AssertionFailedException Fails(Action act)
    {
        var ex = Record.Exception(act);
        return Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void Be_Passes_And_Fails()
    {
        Sample.Should().Be(TimeSpan.FromMinutes(90));
        var value = Sample;
        var ex = Fails(() => value.Should().Be(TimeSpan.FromMinutes(91)));
        Assert.Contains("Expected value to be", ex.Message);
    }

    [Fact]
    public void Be_Fails_On_Null_Subject()
    {
        TimeSpan? value = null;
        var ex = Fails(() => value.Should().Be(Sample));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_And_Fails()
    {
        Sample.Should().NotBe(TimeSpan.FromMinutes(91));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBe(Sample));
        Assert.Contains("Did not expect value to be", ex.Message);
    }

    [Fact]
    public void BePositive_Passes_And_Fails()
    {
        Sample.Should().BePositive();
        var value = TimeSpan.FromMinutes(-5);
        var ex = Fails(() => value.Should().BePositive());
        Assert.Contains("to be positive", ex.Message);
    }

    [Fact]
    public void BePositive_Fails_On_Zero()
    {
        var value = TimeSpan.Zero;
        var ex = Fails(() => value.Should().BePositive());
        Assert.Contains("to be positive", ex.Message);
    }

    [Fact]
    public void BeNegative_Passes_And_Fails()
    {
        TimeSpan.FromMinutes(-5).Should().BeNegative();
        var value = Sample;
        var ex = Fails(() => value.Should().BeNegative());
        Assert.Contains("to be negative", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Passes_And_Fails()
    {
        Sample.Should().BeCloseTo(TimeSpan.FromMinutes(91), TimeSpan.FromMinutes(2));
        var value = Sample;
        var ex = Fails(() => value.Should().BeCloseTo(TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(2)));
        Assert.Contains("to be within", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Throws_On_Negative_Precision()
    {
        var value = Sample;
        Assert.Throws<ArgumentOutOfRangeException>(() => value.Should().BeCloseTo(Sample, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void BeLessThan_Passes_And_Fails()
    {
        Sample.Should().BeLessThan(TimeSpan.FromMinutes(91));
        var value = Sample;
        var ex = Fails(() => value.Should().BeLessThan(Sample));
        Assert.Contains("to be less than", ex.Message);
    }

    [Fact]
    public void BeLessThanOrEqualTo_Passes_And_Fails()
    {
        Sample.Should().BeLessThanOrEqualTo(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(89)));
        Assert.Contains("to be less than or equal to", ex.Message);
    }

    [Fact]
    public void BeGreaterThan_Passes_And_Fails()
    {
        Sample.Should().BeGreaterThan(TimeSpan.FromMinutes(89));
        var value = Sample;
        var ex = Fails(() => value.Should().BeGreaterThan(Sample));
        Assert.Contains("to be greater than", ex.Message);
    }

    [Fact]
    public void BeGreaterThanOrEqualTo_Passes_And_Fails()
    {
        Sample.Should().BeGreaterThanOrEqualTo(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(91)));
        Assert.Contains("to be greater than or equal to", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_And_Fails()
    {
        TimeSpan? some = Sample;
        some.Should().HaveValue();
        TimeSpan? none = null;
        var ex = Fails(() => none.Should().HaveValue());
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_And_Fails()
    {
        TimeSpan? none = null;
        none.Should().NotHaveValue();
        TimeSpan? some = Sample;
        var ex = Fails(() => some.Should().NotHaveValue());
        Assert.Contains("Did not expect some to have a value", ex.Message);
    }
}
