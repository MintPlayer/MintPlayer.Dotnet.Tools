namespace MintPlayer.Assertions.Tests;

public class TimeOnlyAssertionsTests
{
    private static readonly TimeOnly Sample = new(10, 30, 45, 500);

    private static AssertionFailedException Fails(Action act)
    {
        var ex = Record.Exception(act);
        return Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void Be_Passes_And_Fails()
    {
        Sample.Should().Be(new TimeOnly(10, 30, 45, 500));
        var value = Sample;
        var ex = Fails(() => value.Should().Be(new TimeOnly(11, 0)));
        Assert.Contains("Expected value to be", ex.Message);
    }

    [Fact]
    public void Be_Fails_On_Null_Subject()
    {
        TimeOnly? value = null;
        var ex = Fails(() => value.Should().Be(Sample));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_And_Fails()
    {
        Sample.Should().NotBe(new TimeOnly(11, 0));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBe(Sample));
        Assert.Contains("Did not expect value to be", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Passes_And_Fails()
    {
        Sample.Should().BeCloseTo(Sample.AddMinutes(1), TimeSpan.FromMinutes(2));
        var value = Sample;
        var ex = Fails(() => value.Should().BeCloseTo(Sample.AddHours(3), TimeSpan.FromMinutes(1)));
        Assert.Contains("to be within", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Wraps_Around_Midnight()
    {
        var value = new TimeOnly(23, 59);
        value.Should().BeCloseTo(new TimeOnly(0, 1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void BeBefore_Passes_And_Fails()
    {
        Sample.Should().BeBefore(new TimeOnly(11, 0));
        var value = Sample;
        var ex = Fails(() => value.Should().BeBefore(Sample));
        Assert.Contains("to be before", ex.Message);
    }

    [Fact]
    public void BeOnOrBefore_Passes_And_Fails()
    {
        Sample.Should().BeOnOrBefore(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrBefore(new TimeOnly(9, 0)));
        Assert.Contains("to be on or before", ex.Message);
    }

    [Fact]
    public void BeAfter_Passes_And_Fails()
    {
        Sample.Should().BeAfter(new TimeOnly(9, 0));
        var value = Sample;
        var ex = Fails(() => value.Should().BeAfter(Sample));
        Assert.Contains("to be after", ex.Message);
    }

    [Fact]
    public void BeOnOrAfter_Passes_And_Fails()
    {
        Sample.Should().BeOnOrAfter(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrAfter(new TimeOnly(11, 0)));
        Assert.Contains("to be on or after", ex.Message);
    }

    [Fact]
    public void HaveHours_Passes_And_Fails()
    {
        Sample.Should().HaveHours(10);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveHours(11));
        Assert.Contains("to have hours 11", ex.Message);
        Assert.Contains("but found 10", ex.Message);
    }

    [Fact]
    public void HaveHours_Fails_On_Null()
    {
        TimeOnly? value = null;
        var ex = Fails(() => value.Should().HaveHours(10));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void HaveMinutes_Passes_And_Fails()
    {
        Sample.Should().HaveMinutes(30);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveMinutes(31));
        Assert.Contains("to have minutes 31", ex.Message);
    }

    [Fact]
    public void HaveSeconds_Passes_And_Fails()
    {
        Sample.Should().HaveSeconds(45);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveSeconds(46));
        Assert.Contains("to have seconds 46", ex.Message);
    }

    [Fact]
    public void HaveMilliseconds_Passes_And_Fails()
    {
        Sample.Should().HaveMilliseconds(500);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveMilliseconds(501));
        Assert.Contains("to have milliseconds 501", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_And_Fails()
    {
        TimeOnly? some = Sample;
        some.Should().HaveValue();
        TimeOnly? none = null;
        var ex = Fails(() => none.Should().HaveValue());
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_And_Fails()
    {
        TimeOnly? none = null;
        none.Should().NotHaveValue();
        TimeOnly? some = Sample;
        var ex = Fails(() => some.Should().NotHaveValue());
        Assert.Contains("Did not expect some to have a value", ex.Message);
    }

    [Fact]
    public void NotBeCloseTo_Passes_And_Fails()
    {
        Sample.Should().NotBeCloseTo(Sample.AddHours(3), TimeSpan.FromMinutes(1));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBeCloseTo(Sample.AddMinutes(1), TimeSpan.FromMinutes(2)));
        Assert.Equal("Did not expect value to be within 00:02:00 of 10:31:45.5000000, but found 10:30:45.5000000.", ex.Message);
    }

    [Fact]
    public void NotBeCloseTo_Measures_Around_Midnight_Like_BeCloseTo()
    {
        // 00:01 is two minutes from 23:59 the short way round. A negative that naively subtracted
        // would see 23h58m and wrongly pass, so this must fail.
        var value = new TimeOnly(23, 59);
        var ex = Fails(() => value.Should().NotBeCloseTo(new TimeOnly(0, 1), TimeSpan.FromMinutes(5)));
        Assert.Equal("Did not expect value to be within 00:05:00 of 00:01:00.0000000, but found 23:59:00.0000000.", ex.Message);
    }

    [Fact]
    public void NotBeCloseTo_Passes_On_Null_Subject()
    {
        TimeOnly? value = null;
        value.Should().NotBeCloseTo(Sample, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void NotBeCloseTo_Rejects_Negative_Precision()
    {
        var value = Sample;
        Assert.Throws<ArgumentOutOfRangeException>(() => value.Should().NotBeCloseTo(Sample, TimeSpan.FromMinutes(-1)));
    }
}
