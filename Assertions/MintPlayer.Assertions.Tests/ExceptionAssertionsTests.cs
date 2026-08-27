namespace MintPlayer.Assertions.Tests;

public class ExceptionAssertionsTests
{
    [Fact]
    public void Throw_Passes_WhenExpectedExceptionIsThrown()
    {
        Action act = () => throw new InvalidOperationException("boom");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throw_Passes_WhenDerivedExceptionIsThrown()
    {
        Action act = () => throw new ArgumentNullException("param");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Throw_ExposesExceptionViaWhich()
    {
        Action act = () => throw new InvalidOperationException("boom");

        var which = act.Should().Throw<InvalidOperationException>().Which;

        Assert.Equal("boom", which.Message);
    }

    [Fact]
    public void Throw_Fails_WhenNoExceptionIsThrown()
    {
        Action act = () => { };

        var ex = Record.Exception(() => act.Should().Throw<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected act to throw System.InvalidOperationException", failure.Message);
        Assert.Contains("no exception was thrown", failure.Message);
    }

    [Fact]
    public void Throw_Fails_WhenWrongExceptionTypeIsThrown()
    {
        Action act = () => throw new ArgumentException("wrong one");

        var ex = Record.Exception(() => act.Should().Throw<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("System.ArgumentException", failure.Message);
        Assert.Contains("wrong one", failure.Message);
    }

    [Fact]
    public void ThrowExactly_Passes_WhenExactExceptionTypeIsThrown()
    {
        Action act = () => throw new ArgumentException("boom");

        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void ThrowExactly_Fails_WhenDerivedExceptionIsThrown()
    {
        Action act = () => throw new ArgumentNullException("param");

        var ex = Record.Exception(() => act.Should().ThrowExactly<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to throw exactly System.ArgumentException", failure.Message);
        Assert.Contains("System.ArgumentNullException", failure.Message);
    }

    [Fact]
    public void NotThrow_Passes_WhenNoExceptionIsThrown()
    {
        Action act = () => { };

        act.Should().NotThrow();
    }

    [Fact]
    public void NotThrow_Fails_WithTypeAndMessage_WhenExceptionIsThrown()
    {
        Action act = () => throw new InvalidOperationException("kaboom");

        var ex = Record.Exception(() => act.Should().NotThrow());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect act to throw", failure.Message);
        Assert.Contains("System.InvalidOperationException", failure.Message);
        Assert.Contains("kaboom", failure.Message);
    }

    [Fact]
    public void WithMessage_Passes_WhenWildcardMatchesIgnoringCase()
    {
        Action act = () => throw new InvalidOperationException("Something went BOOM today");

        act.Should().Throw<InvalidOperationException>().WithMessage("*went boom*");
    }

    [Fact]
    public void WithMessage_Fails_WhenPatternDoesNotMatch()
    {
        Action act = () => throw new InvalidOperationException("actual message");

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().WithMessage("expected*"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected*", failure.Message);
        Assert.Contains("actual message", failure.Message);
    }

    [Fact]
    public void WithInnerException_Passes_WhenInnerIsAssignable()
    {
        Action act = () => throw new InvalidOperationException("outer", new ArgumentNullException("param"));

        act.Should().Throw<InvalidOperationException>().WithInnerException<ArgumentException>();
    }

    [Fact]
    public void WithInnerException_ExposesInnerViaWhich()
    {
        Action act = () => throw new InvalidOperationException("outer", new ArgumentException("inner message"));

        var which = act.Should().Throw<InvalidOperationException>().WithInnerException<ArgumentException>().Which;

        Assert.Equal("inner message", which.Message);
    }

    [Fact]
    public void WithInnerException_Fails_WhenThereIsNoInnerException()
    {
        Action act = () => throw new InvalidOperationException("outer");

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().WithInnerException<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("inner exception of type System.ArgumentException", failure.Message);
        Assert.Contains("it has none", failure.Message);
    }

    [Fact]
    public void WithInnerException_Fails_WhenInnerHasWrongType()
    {
        Action act = () => throw new InvalidOperationException("outer", new FormatException("bad format"));

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().WithInnerException<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("System.FormatException", failure.Message);
        Assert.Contains("bad format", failure.Message);
    }

    [Fact]
    public void WithInnerExactly_Passes_WhenInnerHasExactType()
    {
        Action act = () => throw new InvalidOperationException("outer", new ArgumentException("inner"));

        act.Should().Throw<InvalidOperationException>().WithInnerExactly<ArgumentException>();
    }

    [Fact]
    public void WithInnerExactly_Fails_WhenInnerIsDerivedType()
    {
        Action act = () => throw new InvalidOperationException("outer", new ArgumentNullException("param"));

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().WithInnerExactly<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("exactly type System.ArgumentException", failure.Message);
        Assert.Contains("System.ArgumentNullException", failure.Message);
    }

    [Fact]
    public void WithParameterName_Passes_WhenParamNameMatches()
    {
        Action act = () => throw new ArgumentNullException("myParam");

        act.Should().Throw<ArgumentNullException>().WithParameterName("myParam");
    }

    [Fact]
    public void WithParameterName_Fails_WhenParamNameDiffers()
    {
        Action act = () => throw new ArgumentNullException("actualParam");

        var ex = Record.Exception(() =>
            act.Should().Throw<ArgumentNullException>().WithParameterName("expectedParam"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expectedParam", failure.Message);
        Assert.Contains("actualParam", failure.Message);
    }

    [Fact]
    public void WithParameterName_Fails_WhenExceptionIsNotAnArgumentException()
    {
        Action act = () => throw new InvalidOperationException("boom");

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().WithParameterName("param"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("is not an ArgumentException", failure.Message);
        Assert.Contains("System.InvalidOperationException", failure.Message);
    }

    [Fact]
    public void Where_Passes_WhenPredicateHolds()
    {
        Action act = () => throw new InvalidOperationException("boom");

        act.Should().Throw<InvalidOperationException>().Where(e => e.Message == "boom");
    }

    [Fact]
    public void Where_Fails_WithPredicateText_WhenPredicateDoesNotHold()
    {
        Action act = () => throw new InvalidOperationException("boom");

        var ex = Record.Exception(() =>
            act.Should().Throw<InvalidOperationException>().Where(e => e.Message.Length > 100));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("e => e.Message.Length > 100", failure.Message);
    }

    [Fact]
    public void FuncThrow_Passes_WhenExceptionIsThrown()
    {
        Func<int> func = () => throw new InvalidOperationException("boom");

        func.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FuncThrow_Fails_WhenNoExceptionIsThrown()
    {
        Func<int> func = () => 42;

        var ex = Record.Exception(() => func.Should().Throw<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no exception was thrown", failure.Message);
    }

    [Fact]
    public void FuncThrowExactly_Fails_WhenDerivedExceptionIsThrown()
    {
        Func<int> func = () => throw new ArgumentNullException("param");

        var ex = Record.Exception(() => func.Should().ThrowExactly<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to throw exactly System.ArgumentException", failure.Message);
    }

    [Fact]
    public void FuncNotThrow_ExposesReturnValueViaWhich()
    {
        Func<int> func = () => 42;

        var which = func.Should().NotThrow().Which;

        Assert.Equal(42, which);
    }

    [Fact]
    public void FuncNotThrow_Fails_WhenExceptionIsThrown()
    {
        Func<int> func = () => throw new InvalidOperationException("kaboom");

        var ex = Record.Exception(() => func.Should().NotThrow());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("kaboom", failure.Message);
    }

    [Fact]
    public void Invoking_WrapsSubjectIntoAnAssertableAction()
    {
        var list = new List<int>();

        list.Invoking(l => l.RemoveAt(5)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Invoking_WithResult_WrapsSubjectIntoAnAssertableFunc()
    {
        var list = new List<int> { 7 };

        var which = list.Invoking(l => l[0]).Should().NotThrow().Which;

        Assert.Equal(7, which);
    }
}
