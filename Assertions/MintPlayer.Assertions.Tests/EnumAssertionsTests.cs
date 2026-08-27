using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class EnumAssertionsTests
{
    private enum Color { Red, Green, Blue }

    [Flags]
    private enum Access { None = 0, Read = 1, Write = 2, ReadWrite = Read | Write }

    [Fact]
    public void Be_Passes_WhenEqual() => Color.Green.Should().Be(Color.Green);

    [Fact]
    public void Be_Passes_WhenBothNull()
    {
        Color? value = null;
        value.Should().Be(null);
    }

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => Color.Green.Should().Be(Color.Blue));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be Color.Blue", ex.Message);
        Assert.Contains("but found Color.Green", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => Color.Green.Should().NotBe(Color.Blue);

    [Fact]
    public void NotBe_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => Color.Green.Should().NotBe(Color.Green));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void HaveFlag_Passes_WhenFlagSet() => Access.ReadWrite.Should().HaveFlag(Access.Read);

    [Fact]
    public void HaveFlag_Fails_WhenFlagNotSet()
    {
        var ex = Record.Exception(() => Access.Read.Should().HaveFlag(Access.Write));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have flag Access.Write", ex.Message);
    }

    [Fact]
    public void NotHaveFlag_Passes_WhenFlagNotSet() => Access.Read.Should().NotHaveFlag(Access.Write);

    [Fact]
    public void NotHaveFlag_Fails_WhenFlagSet()
    {
        var ex = Record.Exception(() => Access.ReadWrite.Should().NotHaveFlag(Access.Write));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to have flag Access.Write", ex.Message);
    }

    [Fact]
    public void BeDefined_Passes_ForDeclaredMember() => Color.Blue.Should().BeDefined();

    [Fact]
    public void BeDefined_Fails_ForUndeclaredValue()
    {
        var ex = Record.Exception(() => ((Color)42).Should().BeDefined());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be defined in", ex.Message);
    }

    [Fact]
    public void BeOneOf_Passes_WhenContained() => Color.Green.Should().BeOneOf(Color.Red, Color.Green);

    [Fact]
    public void BeOneOf_Fails_WhenNotContained()
    {
        var ex = Record.Exception(() => Color.Blue.Should().BeOneOf(Color.Red, Color.Green));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be one of", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_WhenNotNull()
    {
        Color? value = Color.Red;
        value.Should().HaveValue();
    }

    [Fact]
    public void HaveValue_Fails_WhenNull()
    {
        Color? value = null;
        var ex = Record.Exception(() => value.Should().HaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_WhenNull()
    {
        Color? value = null;
        value.Should().NotHaveValue();
    }

    [Fact]
    public void NotHaveValue_Fails_WhenNotNull()
    {
        Color? value = Color.Red;
        var ex = Record.Exception(() => value.Should().NotHaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void Chaining_Works() => Access.ReadWrite.Should().HaveFlag(Access.Read).And.HaveFlag(Access.Write).And.BeDefined();
}
