using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class GuidAssertionsTests
{
    private static readonly Guid SampleGuid = new("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Be_Passes_WhenEqual() => SampleGuid.Should().Be(new Guid("11111111-2222-3333-4444-555555555555"));

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => SampleGuid.Should().Be(Guid.Empty));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be 00000000-0000-0000-0000-000000000000", ex.Message);
        Assert.Contains("but found 11111111-2222-3333-4444-555555555555", ex.Message);
    }

    [Fact]
    public void Be_String_Passes_WhenEqual() => SampleGuid.Should().Be("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Be_String_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => SampleGuid.Should().Be("99999999-2222-3333-4444-555555555555"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be 99999999-2222-3333-4444-555555555555", ex.Message);
    }

    [Fact]
    public void Be_String_Fails_WhenNotAGuid()
    {
        var ex = Record.Exception(() => SampleGuid.Should().Be("not-a-guid"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("not a valid GUID", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => SampleGuid.Should().NotBe(Guid.Empty);

    [Fact]
    public void NotBe_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => SampleGuid.Should().NotBe(SampleGuid));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void NotBe_String_Passes_WhenDifferent() => SampleGuid.Should().NotBe("99999999-2222-3333-4444-555555555555");

    [Fact]
    public void NotBe_String_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => SampleGuid.Should().NotBe("11111111-2222-3333-4444-555555555555"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void BeEmpty_Passes_WhenEmpty() => Guid.Empty.Should().BeEmpty();

    [Fact]
    public void BeEmpty_Fails_WhenNotEmpty()
    {
        var ex = Record.Exception(() => SampleGuid.Should().BeEmpty());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be empty", ex.Message);
    }

    [Fact]
    public void NotBeEmpty_Passes_WhenNotEmpty() => SampleGuid.Should().NotBeEmpty();

    [Fact]
    public void NotBeEmpty_Fails_WhenEmpty()
    {
        var ex = Record.Exception(() => Guid.Empty.Should().NotBeEmpty());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to be empty", ex.Message);
    }

    [Fact]
    public void HaveValue_Passes_WhenNotNull()
    {
        Guid? value = SampleGuid;
        value.Should().HaveValue();
    }

    [Fact]
    public void HaveValue_Fails_WhenNull()
    {
        Guid? value = null;
        var ex = Record.Exception(() => value.Should().HaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have a value", ex.Message);
    }

    [Fact]
    public void NotHaveValue_Passes_WhenNull()
    {
        Guid? value = null;
        value.Should().NotHaveValue();
    }

    [Fact]
    public void NotHaveValue_Fails_WhenNotNull()
    {
        Guid? value = SampleGuid;
        var ex = Record.Exception(() => value.Should().NotHaveValue());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void Chaining_Works() => SampleGuid.Should().NotBeEmpty().And.Be(SampleGuid);
}
