using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class ComparableAssertionsTests
{
    private readonly struct Money : IComparable<Money>
    {
        public Money(decimal amount) => Amount = amount;
        public decimal Amount { get; }
        public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
        public override string ToString() => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Be_Passes_WhenComparesEqual() => new Version(1, 2).Should().Be(new Version(1, 2));

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => new Version(1, 2).Should().Be(new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be 2.0", ex.Message);
        Assert.Contains("but found 1.2", ex.Message);
    }

    [Fact]
    public void Be_Fails_WhenSubjectIsNull()
    {
        Version? version = null;
        var ex = Record.Exception(() => version.Should().Be(new Version(1, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => new Version(1, 2).Should().NotBe(new Version(2, 0));

    [Fact]
    public void NotBe_Fails_WhenComparesEqual()
    {
        var ex = Record.Exception(() => new Version(1, 2).Should().NotBe(new Version(1, 2)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void BeLessThan_Passes() => new Version(1, 0).Should().BeLessThan(new Version(2, 0));

    [Fact]
    public void BeLessThan_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => new Version(2, 0).Should().BeLessThan(new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be less than 2.0", ex.Message);
    }

    [Fact]
    public void BeLessThanOrEqualTo_Passes_WhenEqual() => new Version(2, 0).Should().BeLessThanOrEqualTo(new Version(2, 0));

    [Fact]
    public void BeLessThanOrEqualTo_Fails_WhenGreater()
    {
        var ex = Record.Exception(() => new Version(3, 0).Should().BeLessThanOrEqualTo(new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("less than or equal to 2.0", ex.Message);
    }

    [Fact]
    public void BeGreaterThan_Passes() => new Version(3, 0).Should().BeGreaterThan(new Version(2, 0));

    [Fact]
    public void BeGreaterThan_Fails_WhenLess()
    {
        var ex = Record.Exception(() => new Version(1, 0).Should().BeGreaterThan(new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be greater than 2.0", ex.Message);
    }

    [Fact]
    public void BeGreaterThanOrEqualTo_Passes_WhenEqual() => new Version(2, 0).Should().BeGreaterThanOrEqualTo(new Version(2, 0));

    [Fact]
    public void BeGreaterThanOrEqualTo_Fails_WhenLess()
    {
        var ex = Record.Exception(() => new Version(1, 0).Should().BeGreaterThanOrEqualTo(new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("greater than or equal to 2.0", ex.Message);
    }

    [Fact]
    public void BeInRange_Passes_OnBoundary() => new Version(1, 0).Should().BeInRange(new Version(1, 0), new Version(2, 0));

    [Fact]
    public void BeInRange_Fails_WhenOutside()
    {
        var ex = Record.Exception(() => new Version(3, 0).Should().BeInRange(new Version(1, 0), new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be between 1.0 and 2.0", ex.Message);
    }

    [Fact]
    public void WorksForValueTypeSubjects()
    {
        new Money(5m).Should().BeGreaterThan(new Money(4m));
        new Money(0m).Should().Be(new Money(0m));
    }

    [Fact]
    public void ValueTypeSubject_Fails_WithMessage()
    {
        var ex = Record.Exception(() => new Money(5m).Should().BeLessThan(new Money(4m)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be less than 4", ex.Message);
    }

    [Fact]
    public void NotBeInRange_Passes_WhenOutsideTheRange()
    {
        new Version(3, 0).Should().NotBeInRange(new Version(1, 0), new Version(2, 0));
        new Version(0, 9).Should().NotBeInRange(new Version(1, 0), new Version(2, 0));
    }

    [Fact]
    public void NotBeInRange_Fails_WhenInsideTheRange()
    {
        var version = new Version(1, 5);
        var ex = Record.Exception(() => version.Should().NotBeInRange(new Version(1, 0), new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Equal("Did not expect version to be between 1.0 and 2.0, but found 1.5.", ex.Message);
    }

    [Fact]
    public void NotBeInRange_Fails_OnTheInclusiveBounds()
    {
        // BeInRange treats both ends as included, so the negative must reject them too.
        var version = new Version(2, 0);
        var ex = Record.Exception(() => version.Should().NotBeInRange(new Version(1, 0), new Version(2, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Equal("Did not expect version to be between 1.0 and 2.0, but found 2.0.", ex.Message);
    }

    [Fact]
    public void NotBeInRange_Passes_WhenSubjectIsNull()
    {
        Version? version = null;
        version.Should().NotBeInRange(new Version(1, 0), new Version(2, 0));
    }

    [Fact]
    public void NotBeInRange_Works_ForAValueTypeSubject()
    {
        new Money(9m).Should().NotBeInRange(new Money(1m), new Money(5m));
        var money = new Money(3m);
        var ex = Record.Exception(() => money.Should().NotBeInRange(new Money(1m), new Money(5m)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Equal("Did not expect money to be between 1 and 5, but found 3.", ex.Message);
    }

    [Fact]
    public void Chaining_Works() => new Version(1, 5).Should().BeGreaterThan(new Version(1, 0)).And.BeLessThan(new Version(2, 0));
}
