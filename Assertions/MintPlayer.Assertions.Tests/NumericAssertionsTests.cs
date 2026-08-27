using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class NumericAssertionsTests
{
    [Fact]
    public void Be_Passes_WhenEqual() => 42.Should().Be(42);

    [Fact]
    public void Be_Passes_ForNaN() => double.NaN.Should().Be(double.NaN);

    [Fact]
    public void Be_Passes_WhenBothNull()
    {
        int? value = null;
        value.Should().Be(null);
    }

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => 42.Should().Be(43, "we counted {0} items", 43));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be 43", ex.Message);
        Assert.Contains("but found 42", ex.Message);
        Assert.Contains("because we counted 43 items", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => 42.Should().NotBe(43);

    [Fact]
    public void NotBe_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => 42.Should().NotBe(42));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void BePositive_Passes_WhenPositive() => 1.Should().BePositive();

    [Fact]
    public void BePositive_Fails_WhenZero()
    {
        var ex = Record.Exception(() => 0.Should().BePositive());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be positive", ex.Message);
    }

    [Fact]
    public void BeNegative_Passes_WhenNegative() => (-1).Should().BeNegative();

    [Fact]
    public void BeNegative_Fails_WhenPositive()
    {
        var ex = Record.Exception(() => 1.Should().BeNegative());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be negative", ex.Message);
    }

    [Fact]
    public void BeGreaterThan_Passes() => 5.Should().BeGreaterThan(4);

    [Fact]
    public void BeGreaterThan_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => 5.Should().BeGreaterThan(5));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be greater than 5", ex.Message);
    }

    [Fact]
    public void BeGreaterThanOrEqualTo_Passes_WhenEqual() => 5.Should().BeGreaterThanOrEqualTo(5);

    [Fact]
    public void BeGreaterThanOrEqualTo_Fails_WhenLess()
    {
        var ex = Record.Exception(() => 4.Should().BeGreaterThanOrEqualTo(5));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("greater than or equal to 5", ex.Message);
    }

    [Fact]
    public void BeLessThan_Passes() => 4.Should().BeLessThan(5);

    [Fact]
    public void BeLessThan_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => 5.Should().BeLessThan(5));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be less than 5", ex.Message);
    }

    [Fact]
    public void BeLessThanOrEqualTo_Passes_WhenEqual() => 5.Should().BeLessThanOrEqualTo(5);

    [Fact]
    public void BeLessThanOrEqualTo_Fails_WhenGreater()
    {
        var ex = Record.Exception(() => 6.Should().BeLessThanOrEqualTo(5));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("less than or equal to 5", ex.Message);
    }

    [Fact]
    public void BeInRange_Passes_OnBoundary() => 5.Should().BeInRange(5, 10);

    [Fact]
    public void BeInRange_Fails_WhenOutside()
    {
        var ex = Record.Exception(() => 11.Should().BeInRange(5, 10));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be between 5 and 10", ex.Message);
    }

    [Fact]
    public void NotBeInRange_Passes_WhenOutside() => 11.Should().NotBeInRange(5, 10);

    [Fact]
    public void NotBeInRange_Fails_WhenInside()
    {
        var ex = Record.Exception(() => 7.Should().NotBeInRange(5, 10));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("between 5 and 10", ex.Message);
    }

    [Fact]
    public void BeOneOf_Passes_WhenContained() => 2.Should().BeOneOf(1, 2, 3);

    [Fact]
    public void BeOneOf_Fails_WhenNotContained()
    {
        var ex = Record.Exception(() => 4.Should().BeOneOf(1, 2, 3));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be one of", ex.Message);
        Assert.Contains("but found 4", ex.Message);
    }

    [Fact]
    public void BeCloseTo_Passes_WithinDelta() => 10.Should().BeCloseTo(12, 2);

    [Fact]
    public void BeCloseTo_Passes_ForUnsignedWrapCase()
    {
        byte value = 1;
        value.Should().BeCloseTo(3, 5);
    }

    [Fact]
    public void BeCloseTo_Fails_OutsideDelta()
    {
        var ex = Record.Exception(() => 10.Should().BeCloseTo(15, 2));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be within 2 of 15", ex.Message);
    }

    [Fact]
    public void NotBeCloseTo_Passes_OutsideDelta() => 10.Should().NotBeCloseTo(15, 2);

    [Fact]
    public void NotBeCloseTo_Fails_WithinDelta()
    {
        var ex = Record.Exception(() => 10.Should().NotBeCloseTo(12, 2));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("within 2 of 12", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_WhenNotNull()
    {
        int? value = 0;
        value.Should().HaveValue();
    }

    [Fact]
    public void HaveValue_Fails_WhenNull()
    {
        int? value = null;
        var ex = Record.Exception(() => value.Should().HaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_WhenNull()
    {
        double? value = null;
        value.Should().NotHaveValue();
    }

    [Fact]
    public void NotHaveValue_Fails_WhenNotNull()
    {
        double? value = 1.5;
        var ex = Record.Exception(() => value.Should().NotHaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void WorksAcrossNumericTypes()
    {
        ((sbyte)1).Should().BePositive();
        ((byte)1).Should().BePositive();
        ((short)1).Should().BePositive();
        ((ushort)1).Should().BePositive();
        1.Should().BePositive();
        1u.Should().BePositive();
        1L.Should().BePositive();
        1ul.Should().BePositive();
        1f.Should().BePositive();
        1d.Should().BePositive();
        1m.Should().BePositive();
    }

    [Fact]
    public void Chaining_Works() => 7.Should().BePositive().And.BeLessThan(10).And.BeOneOf(6, 7, 8);
}
