using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class BooleanAssertionsTests
{
    [Fact]
    public void BeTrue_Passes_WhenTrue() => true.Should().BeTrue();

    [Fact]
    public void BeTrue_Fails_WhenFalse()
    {
        var ex = Record.Exception(() => false.Should().BeTrue("we say so"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be true", ex.Message);
        Assert.Contains("because we say so", ex.Message);
    }

    [Fact]
    public void BeTrue_Fails_WhenNull()
    {
        bool? value = null;
        var ex = Record.Exception(() => value.Should().BeTrue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void BeFalse_Passes_WhenFalse() => false.Should().BeFalse();

    [Fact]
    public void BeFalse_Fails_WhenTrue()
    {
        var ex = Record.Exception(() => true.Should().BeFalse());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be false", ex.Message);
    }

    [Fact]
    public void Be_Passes_WhenEqual() => true.Should().Be(true);

    [Fact]
    public void Be_Passes_WhenBothNull()
    {
        bool? value = null;
        value.Should().Be(null);
    }

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => true.Should().Be(false));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be false", ex.Message);
        Assert.Contains("but found true", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => true.Should().NotBe(false);

    [Fact]
    public void NotBe_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => true.Should().NotBe(true));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_WhenNotNull()
    {
        bool? value = false;
        value.Should().HaveValue();
    }

    [Fact]
    public void HaveValue_Fails_WhenNull()
    {
        bool? value = null;
        var ex = Record.Exception(() => value.Should().HaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_WhenNull()
    {
        bool? value = null;
        value.Should().NotHaveValue();
    }

    [Fact]
    public void NotHaveValue_Fails_WhenNotNull()
    {
        bool? value = true;
        var ex = Record.Exception(() => value.Should().NotHaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void FailureMessage_UsesCallerExpression()
    {
        var flag = false;
        var ex = Record.Exception(() => flag.Should().BeTrue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("flag", ex.Message);
    }

    [Fact]
    public void Chaining_Works()
    {
        bool? value = true;
        value.Should().HaveValue().And.BeTrue();
    }
}
