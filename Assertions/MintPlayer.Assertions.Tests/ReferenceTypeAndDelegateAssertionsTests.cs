using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// ReferenceTypeAssertions is the base every object-ish assertion inherits from, so these 10
/// members are reachable from a large part of the public API — and had no direct tests.
/// Driven through ObjectAssertions, which is the concrete type consumers actually get.
/// </summary>
public class ReferenceTypeAssertionsTests
{
    private class Animal { }
    private sealed class Dog : Animal { }

    #region Subject / expression plumbing

    [Fact]
    public void Subject_ExposesTheValueUnderTest()
    {
        var value = new object();

        value.Should().Subject.Should().BeSameAs(value);
    }

    [Fact]
    public void SubjectExpression_IsCapturedFromTheCallSite()
    {
        var myVariable = new object();

        myVariable.Should().SubjectExpression.Should().Contain("myVariable");
    }

    #endregion

    #region BeNull / NotBeNull

    [Fact]
    public void BeNull_PassesForNull()
    {
        object? value = null;
        value.Should().BeNull();
    }

    [Fact]
    public void BeNull_FailsForANonNullValue()
    {
        var act = () => new object().Should().BeNull();

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void NotBeNull_PassesForANonNullValue()
        => new object().Should().NotBeNull();

    [Fact]
    public void NotBeNull_FailsForNull()
    {
        object? value = null;
        var act = () => value.Should().NotBeNull();

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void BeNull_IncludesTheBecauseReason()
    {
        var act = () => new object().Should().BeNull("we cleared it {0}", "already");

        act.Should().Throw<AssertionFailedException>().WithMessage("*because we cleared it already*");
    }

    #endregion

    #region BeSameAs / NotBeSameAs

    [Fact]
    public void BeSameAs_ComparesByReference()
    {
        var value = new object();

        value.Should().BeSameAs(value);
    }

    [Fact]
    public void BeSameAs_FailsForAnEqualButDistinctInstance()
    {
        var first = new string(['a']);
        var second = new string(['a']);

        var act = () => first.Should().BeSameAs(second);

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void NotBeSameAs_PassesForDistinctInstances()
    {
        var first = new string(['a']);
        var second = new string(['a']);

        ((object)first).Should().NotBeSameAs(second);
    }

    [Fact]
    public void NotBeSameAs_FailsForTheSameInstance()
    {
        var value = new object();

        var act = () => value.Should().NotBeSameAs(value);

        act.Should().Throw<AssertionFailedException>();
    }

    #endregion

    #region BeOfType / NotBeOfType

    [Fact]
    public void BeOfType_PassesForTheExactType()
        => ((object)new Dog()).Should().BeOfType<Dog>();

    [Fact]
    public void BeOfType_FailsForABaseType()
    {
        var act = () => ((object)new Dog()).Should().BeOfType<Animal>();

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void BeOfType_DrillsIntoTheTypedSubject()
    {
        object value = "hello";

        value.Should().BeOfType<string>().Which.Should().Be("hello");
    }

    [Fact]
    public void NotBeOfType_PassesForADifferentType()
        => ((object)new Dog()).Should().NotBeOfType<Animal>();

    [Fact]
    public void NotBeOfType_FailsForTheExactType()
    {
        var act = () => ((object)new Dog()).Should().NotBeOfType<Dog>();

        act.Should().Throw<AssertionFailedException>();
    }

    #endregion

    #region BeAssignableTo / NotBeAssignableTo

    [Fact]
    public void BeAssignableTo_PassesForABaseType()
        => ((object)new Dog()).Should().BeAssignableTo<Animal>();

    [Fact]
    public void BeAssignableTo_PassesForTheExactType()
        => ((object)new Dog()).Should().BeAssignableTo<Dog>();

    [Fact]
    public void BeAssignableTo_DrillsIntoTheTypedSubject()
    {
        object value = new Dog();

        value.Should().BeAssignableTo<Animal>().Which.Should().NotBeNull();
    }

    [Fact]
    public void BeAssignableTo_FailsForAnUnrelatedType()
    {
        var act = () => ((object)new Dog()).Should().BeAssignableTo<string>();

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void NotBeAssignableTo_PassesForAnUnrelatedType()
        => ((object)new Dog()).Should().NotBeAssignableTo<string>();

    [Fact]
    public void NotBeAssignableTo_FailsForABaseType()
    {
        var act = () => ((object)new Dog()).Should().NotBeAssignableTo<Animal>();

        act.Should().Throw<AssertionFailedException>();
    }

    #endregion

    #region Match

    [Fact]
    public void Match_PassesWhenThePredicateHolds()
        => ((object)"hello").Should().Match(v => v is string { Length: 5 });

    [Fact]
    public void Match_FailsWhenThePredicateDoesNot()
    {
        var act = () => ((object)"hello").Should().Match(v => v is string { Length: 3 });

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void Match_ReceivesNull()
    {
        object? value = null;

        value.Should().Match(v => v is null);
    }

    [Fact]
    public void NotMatch_PassesWhenThePredicateDoesNotHold()
        => ((object)"hello").Should().NotMatch(v => v is string { Length: 3 });

    [Fact]
    public void NotMatch_FailsWhenThePredicateHolds()
    {
        var value = (object)"hello";

        var ex = Record.Exception(() => value.Should().NotMatch(v => v is string { Length: 5 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Equal("Did not expect value to match the given predicate, but \"hello\" did.", ex.Message);
    }

    [Fact]
    public void NotMatch_ReceivesNull_AndDoesNotPassItAutomatically()
    {
        object? value = null;

        // Unlike the other negatives, a null subject is handed to the predicate rather than
        // short-circuited to a pass — so a predicate that accepts null makes this fail.
        var ex = Record.Exception(() => value.Should().NotMatch(v => v is null));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Equal("Did not expect value to match the given predicate, but <null> did.", ex.Message);
    }

    [Fact]
    public void NotMatch_ThrowsWhenThePredicateIsNull()
    {
        var value = new object();

        Assert.Throws<ArgumentNullException>(() => value.Should().NotMatch(null!));
    }

    #endregion

    #region Chaining

    [Fact]
    public void TheAssertionsChainThroughAnd()
    {
        object value = "hello";

        value.Should().NotBeNull()
            .And.BeOfType<string>();
    }

    #endregion
}

/// <summary>
/// ActionAssertions and FuncAssertions had zero direct tests despite being the entry point for
/// every exception assertion in the library.
/// </summary>
public class ActionAssertionsTests
{
    [Fact]
    public void Throw_PassesForTheDeclaredException()
    {
        Action act = () => throw new InvalidOperationException("boom");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throw_PassesForADerivedException()
    {
        Action act = () => throw new ArgumentNullException("param");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Throw_FailsWhenNothingIsThrown()
    {
        Action act = () => { };

        var outer = () => act.Should().Throw<InvalidOperationException>();

        outer.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void Throw_FailsForAnUnrelatedException()
    {
        Action act = () => throw new InvalidOperationException();

        var outer = () => act.Should().Throw<ArgumentException>();

        outer.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void ThrowExactly_RejectsADerivedException()
    {
        Action act = () => throw new ArgumentNullException("param");

        var outer = () => act.Should().ThrowExactly<ArgumentException>();

        outer.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void ThrowExactly_PassesForTheExactType()
    {
        Action act = () => throw new ArgumentException("nope");

        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void NotThrow_PassesWhenNothingIsThrown()
    {
        Action act = () => { };

        act.Should().NotThrow();
    }

    [Fact]
    public void NotThrow_FailsAndSurfacesTheException()
    {
        Action act = () => throw new InvalidOperationException("boom");

        var outer = () => act.Should().NotThrow();

        outer.Should().Throw<AssertionFailedException>().WithMessage("*boom*");
    }

    [Fact]
    public void Throw_ExposesTheExceptionForFurtherAssertions()
    {
        Action act = () => throw new InvalidOperationException("the specific message");

        act.Should().Throw<InvalidOperationException>().WithMessage("*specific*");
    }

    [Fact]
    public void ExecutionTime_MeasuresTheAction()
    {
        Action act = () => Thread.Sleep(1);

        act.Should().ExecutionTime().Should().NotBeNull();
    }
}

public class FuncAssertionsTests
{
    [Fact]
    public void Throw_PassesForTheDeclaredException()
    {
        Func<int> func = () => throw new InvalidOperationException("boom");

        func.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throw_FailsWhenTheFuncReturnsNormally()
    {
        Func<int> func = () => 42;

        var outer = () => func.Should().Throw<InvalidOperationException>();

        outer.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void ThrowExactly_RejectsADerivedException()
    {
        Func<int> func = () => throw new ArgumentNullException("param");

        var outer = () => func.Should().ThrowExactly<ArgumentException>();

        outer.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void ThrowExactly_PassesForTheExactType()
    {
        Func<int> func = () => throw new ArgumentException("nope");

        func.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void NotThrow_PassesAndExposesTheResult()
    {
        Func<int> func = () => 42;

        func.Should().NotThrow().Which.Should().Be(42);
    }

    [Fact]
    public void NotThrow_FailsAndSurfacesTheException()
    {
        Func<int> func = () => throw new InvalidOperationException("boom");

        var outer = () => func.Should().NotThrow();

        outer.Should().Throw<AssertionFailedException>().WithMessage("*boom*");
    }

    [Fact]
    public void Throw_ExposesTheExceptionForFurtherAssertions()
    {
        Func<int> func = () => throw new InvalidOperationException("the specific message");

        func.Should().Throw<InvalidOperationException>().WithMessage("*specific*");
    }

    [Fact]
    public void NotThrow_WorksForAReferenceTypeResult()
    {
        Func<string> func = () => "hello";

        func.Should().NotThrow().Which.Should().Be("hello");
    }
}
