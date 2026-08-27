using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// WithMessage is case-sensitive by default and takes an explicit <see cref="StringComparison"/>
/// when it should not be. Previously it silently ignored case, which the call site gave no hint of.
/// </summary>
public class ExceptionMessageCasingTests
{
    private static Action Throwing() => () => throw new InvalidOperationException("The Widget Was Not Found");

    [Fact]
    public void WithMessage_MatchesExactCasing()
        => Throwing().Should().Throw<InvalidOperationException>().WithMessage("*Widget Was Not Found*");

    [Fact]
    public void WithMessage_IsCaseSensitive()
    {
        var ex = Record.Exception(() =>
            Throwing().Should().Throw<InvalidOperationException>().WithMessage("*widget was not found*"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to have a message matching", failure.Message);
    }

    [Fact]
    public void WithMessage_IgnoringCase_IgnoresCase()
        => Throwing().Should().Throw<InvalidOperationException>().WithMessage("*widget was not found*", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void WithMessage_IgnoringCase_StillFailsOnADifferentMessage()
    {
        var ex = Record.Exception(() =>
            Throwing().Should().Throw<InvalidOperationException>().WithMessage("*sprocket*", StringComparison.OrdinalIgnoreCase));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("ignoring case", failure.Message);
    }

    [Fact]
    public async Task AsyncChain_SupportsBothCasingModes()
    {
        var act = () => Task.FromException(new InvalidOperationException("The Widget Was Not Found"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Widget*");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*widget*", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOnlyItemEqualsAValue_ReadsAsEqual()
    {
        // The terse form of ContainSingle().Which.Should().Be(x) — see ContainSingle's remarks.
        var found = new[] { "/tmp/a.sln" };

        found.Should().Equal("/tmp/a.sln");
        found.Should().ContainSingle().Which.Should().Be("/tmp/a.sln");
    }
}
