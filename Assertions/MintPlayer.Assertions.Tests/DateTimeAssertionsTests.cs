namespace MintPlayer.Assertions.Tests;

public class DateTimeAssertionsTests
{
    private static readonly DateTime Sample = new(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc);

    private static AssertionFailedException Fails(Action act)
    {
        var ex = Record.Exception(act);
        return Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void Be_Passes_And_Fails()
    {
        Sample.Should().Be(new DateTime(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc));
        var value = Sample;
        var ex = Fails(() => value.Should().Be(Sample.AddDays(1)));
        Assert.Contains("Expected value to be", ex.Message);
        Assert.Contains("but found", ex.Message);
    }

    [Fact]
    public void Be_Fails_On_Null_Subject()
    {
        DateTime? value = null;
        var ex = Fails(() => value.Should().Be(Sample));
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_And_Fails()
    {
        Sample.Should().NotBe(Sample.AddSeconds(1));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBe(Sample));
        Assert.Contains("Did not expect value to be", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Passes_And_Fails()
    {
        Sample.Should().BeCloseTo(Sample.AddSeconds(30), TimeSpan.FromMinutes(1));
        var value = Sample;
        var ex = Fails(() => value.Should().BeCloseTo(Sample.AddHours(2), TimeSpan.FromMinutes(1)));
        Assert.Contains("to be within", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Throws_On_Negative_Precision()
    {
        var value = Sample;
        Assert.Throws<ArgumentOutOfRangeException>(() => value.Should().BeCloseTo(Sample, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void NotBeCloseTo_Passes_And_Fails()
    {
        Sample.Should().NotBeCloseTo(Sample.AddHours(2), TimeSpan.FromMinutes(1));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBeCloseTo(Sample.AddSeconds(10), TimeSpan.FromMinutes(1)));
        Assert.Contains("Did not expect value to be within", ex.Message);
    }

    [Fact]
    public void BeBefore_Passes_And_Fails()
    {
        Sample.Should().BeBefore(Sample.AddDays(1));
        var value = Sample;
        var ex = Fails(() => value.Should().BeBefore(Sample));
        Assert.Contains("to be before", ex.Message);
    }

    [Fact]
    public void BeOnOrBefore_Passes_And_Fails()
    {
        Sample.Should().BeOnOrBefore(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrBefore(Sample.AddDays(-1)));
        Assert.Contains("to be on or before", ex.Message);
    }

    [Fact]
    public void BeAfter_Passes_And_Fails()
    {
        Sample.Should().BeAfter(Sample.AddDays(-1));
        var value = Sample;
        var ex = Fails(() => value.Should().BeAfter(Sample));
        Assert.Contains("to be after", ex.Message);
    }

    [Fact]
    public void BeOnOrAfter_Passes_And_Fails()
    {
        Sample.Should().BeOnOrAfter(Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOnOrAfter(Sample.AddDays(1)));
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
        DateTime? value = null;
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
    public void HaveHour_Passes_And_Fails()
    {
        Sample.Should().HaveHour(10);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveHour(11));
        Assert.Contains("to have hour 11", ex.Message);
    }

    [Fact]
    public void HaveMinute_Passes_And_Fails()
    {
        Sample.Should().HaveMinute(30);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveMinute(31));
        Assert.Contains("to have minute 31", ex.Message);
    }

    [Fact]
    public void HaveSecond_Passes_And_Fails()
    {
        Sample.Should().HaveSecond(45);
        var value = Sample;
        var ex = Fails(() => value.Should().HaveSecond(46));
        Assert.Contains("to have second 46", ex.Message);
    }

    [Fact]
    public void BeSameDateAs_Passes_And_Fails()
    {
        Sample.Should().BeSameDateAs(new DateTime(2024, 3, 15, 23, 59, 59));
        var value = Sample;
        var ex = Fails(() => value.Should().BeSameDateAs(new DateTime(2024, 3, 16)));
        Assert.Contains("to be on", ex.Message);
    }

    [Fact]
    public void BeIn_Passes_And_Fails()
    {
        Sample.Should().BeIn(DateTimeKind.Utc);
        var value = Sample;
        var ex = Fails(() => value.Should().BeIn(DateTimeKind.Local));
        Assert.Contains("to be in DateTimeKind.Local", ex.Message);
        Assert.Contains("but found DateTimeKind.Utc", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_And_Fails()
    {
        DateTime? some = Sample;
        some.Should().HaveValue();
        DateTime? none = null;
        var ex = Fails(() => none.Should().HaveValue());
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_And_Fails()
    {
        DateTime? none = null;
        none.Should().NotHaveValue();
        DateTime? some = Sample;
        var ex = Fails(() => some.Should().NotHaveValue());
        Assert.Contains("Did not expect some to have a value", ex.Message);
    }

    [Fact]
    public void BeOneOf_Passes_And_Fails()
    {
        Sample.Should().BeOneOf(Sample.AddDays(-1), Sample);
        var value = Sample;
        var ex = Fails(() => value.Should().BeOneOf(Sample.AddDays(1), Sample.AddDays(2)));
        Assert.Contains("to be one of", ex.Message);
    }

    [Fact]
    public void NotBeOneOf_Passes_And_Fails()
    {
        Sample.Should().NotBeOneOf(Sample.AddDays(1), Sample.AddDays(2));
        var value = Sample;
        var ex = Fails(() => value.Should().NotBeOneOf(Sample.AddDays(-1), Sample));
        Assert.Equal(
            "Did not expect value to be one of {2024-03-14T10:30:45.0000000Z, 2024-03-15T10:30:45.0000000Z}, "
            + "but found 2024-03-15T10:30:45.0000000Z.", ex.Message);
    }

    [Fact]
    public void NotBeOneOf_Passes_On_Null_Subject()
    {
        DateTime? value = null;
        value.Should().NotBeOneOf(Sample);
    }

    [Fact]
    public void NotBeOneOf_Throws_On_Null_Values()
    {
        var value = Sample;
        Assert.Throws<ArgumentNullException>(() => value.Should().NotBeOneOf((DateTime[])null!, because: null));
    }

    [Fact]
    public void NotHaveYear_Passes_And_Fails()
    {
        Sample.Should().NotHaveYear(2023);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveYear(2024));
        Assert.Equal("Did not expect value to have year 2024.", ex.Message);
    }

    [Fact]
    public void NotHaveMonth_Passes_And_Fails()
    {
        Sample.Should().NotHaveMonth(4);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveMonth(3));
        Assert.Equal("Did not expect value to have month 3.", ex.Message);
    }

    [Fact]
    public void NotHaveDay_Passes_And_Fails()
    {
        Sample.Should().NotHaveDay(16);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveDay(15));
        Assert.Equal("Did not expect value to have day 15.", ex.Message);
    }

    [Fact]
    public void NotHaveHour_Passes_And_Fails()
    {
        Sample.Should().NotHaveHour(11);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveHour(10));
        Assert.Equal("Did not expect value to have hour 10.", ex.Message);
    }

    [Fact]
    public void NotHaveMinute_Passes_And_Fails()
    {
        Sample.Should().NotHaveMinute(31);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveMinute(30));
        Assert.Equal("Did not expect value to have minute 30.", ex.Message);
    }

    [Fact]
    public void NotHaveSecond_Passes_And_Fails()
    {
        Sample.Should().NotHaveSecond(46);
        var value = Sample;
        var ex = Fails(() => value.Should().NotHaveSecond(45));
        Assert.Equal("Did not expect value to have second 45.", ex.Message);
    }

    [Fact]
    public void NotHave_Component_Family_Passes_On_Null_Subject()
    {
        DateTime? value = null;
        value.Should().NotHaveYear(2024).And.NotHaveMonth(3).And.NotHaveDay(15)
            .And.NotHaveHour(10).And.NotHaveMinute(30).And.NotHaveSecond(45);
    }

    [Fact]
    public void NotBeSameDateAs_Passes_And_Fails()
    {
        Sample.Should().NotBeSameDateAs(Sample.AddDays(1));
        // Same calendar day, different clock time: the time of day is ignored, so this must fail.
        var value = Sample;
        var ex = Fails(() => value.Should().NotBeSameDateAs(Sample.AddHours(2)));
        Assert.Equal(
            "Did not expect value to be on 2024-03-15T00:00:00.0000000Z, but found 2024-03-15T10:30:45.0000000Z.",
            ex.Message);
    }

    [Fact]
    public void NotBeSameDateAs_Passes_On_Null_Subject()
    {
        DateTime? value = null;
        value.Should().NotBeSameDateAs(Sample);
    }

    [Fact]
    public void NotBeIn_Passes_And_Fails()
    {
        Sample.Should().NotBeIn(DateTimeKind.Local);
        var value = Sample;
        var ex = Fails(() => value.Should().NotBeIn(DateTimeKind.Utc));
        Assert.Equal("Did not expect value to be in DateTimeKind.Utc.", ex.Message);
    }

    [Fact]
    public void NotBeIn_Passes_On_Null_Subject()
    {
        DateTime? value = null;
        value.Should().NotBeIn(DateTimeKind.Utc);
    }
}
