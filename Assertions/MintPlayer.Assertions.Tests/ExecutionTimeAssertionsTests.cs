namespace MintPlayer.Assertions.Tests;

public class ExecutionTimeAssertionsTests
{
    [Fact]
    public void BeLessThan_Passes_WhenActionIsFastEnough()
    {
        Action act = () => Thread.Sleep(1);

        act.Should().ExecutionTime().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BeLessThan_Fails_WithMeasuredTime_WhenActionIsTooSlow()
    {
        Action act = () => Thread.Sleep(20);

        var ex = Record.Exception(() => act.Should().ExecutionTime().BeLessThan(TimeSpan.Zero));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("execution time of act", failure.Message);
        Assert.Contains("to be less than", failure.Message);
        Assert.Contains("but it took", failure.Message);
    }

    [Fact]
    public void BeLessThanOrEqualTo_Passes_WhenActionIsFastEnough()
    {
        Action act = () => Thread.Sleep(1);

        act.Should().ExecutionTime().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BeLessThanOrEqualTo_Fails_WhenActionIsTooSlow()
    {
        Action act = () => Thread.Sleep(20);

        var ex = Record.Exception(() =>
            act.Should().ExecutionTime().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(1)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be less than or equal to", failure.Message);
    }

    [Fact]
    public void BeGreaterThan_Passes_WhenActionIsSlowEnough()
    {
        Action act = () => Thread.Sleep(20);

        act.Should().ExecutionTime().BeGreaterThan(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void BeGreaterThan_Fails_WhenActionIsTooFast()
    {
        Action act = () => { };

        var ex = Record.Exception(() =>
            act.Should().ExecutionTime().BeGreaterThan(TimeSpan.FromSeconds(10)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be greater than", failure.Message);
        Assert.Contains("but it took", failure.Message);
    }

    [Fact]
    public void BeGreaterThanOrEqualTo_Passes_ForAnyAction()
    {
        Action act = () => { };

        act.Should().ExecutionTime().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void BeGreaterThanOrEqualTo_Fails_WhenActionIsTooFast()
    {
        Action act = () => { };

        var ex = Record.Exception(() =>
            act.Should().ExecutionTime().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be greater than or equal to", failure.Message);
    }

    [Fact]
    public void BeCloseTo_Passes_WithGenerousPrecision()
    {
        Action act = () => Thread.Sleep(1);

        act.Should().ExecutionTime().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void BeCloseTo_Fails_WhenMeasuredTimeIsOutsidePrecision()
    {
        Action act = () => { };

        var ex = Record.Exception(() =>
            act.Should().ExecutionTime().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(1)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be within", failure.Message);
        Assert.Contains("but it took", failure.Message);
    }

    [Fact]
    public void ExecutionTimeAssertions_CanBeChained()
    {
        Action act = () => { };

        act.Should().ExecutionTime()
            .BeLessThan(TimeSpan.FromSeconds(10))
            .And.BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
