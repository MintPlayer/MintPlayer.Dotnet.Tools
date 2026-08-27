using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Base class for assertions on reference-type subjects: null checks, reference identity,
/// type checks and predicate matching. <typeparamref name="TSelf"/> is the concrete assertions
/// class so chained constraints keep their specific type.
/// </summary>
public abstract class ReferenceTypeAssertions<TSubject, TSelf>
    where TSubject : class
    where TSelf : ReferenceTypeAssertions<TSubject, TSelf>
{
    protected ReferenceTypeAssertions(TSubject? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public TSubject? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    public AndConstraint<TSelf> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be null{reason}, but found {0}.", Subject);
        return new((TSelf)this);
    }

    public AndConstraint<TSelf> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be null{reason}.");
        return new((TSelf)this);
    }

    public AndConstraint<TSelf> BeSameAs(TSubject? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(ReferenceEquals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to refer to {0}{reason}, but found {1}.", expected, Subject);
        return new((TSelf)this);
    }

    public AndConstraint<TSelf> NotBeSameAs(TSubject? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!ReferenceEquals(Subject, unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to refer to {0}{reason}.", unexpected);
        return new((TSelf)this);
    }

    /// <summary>Asserts the subject is exactly of type <typeparamref name="T"/> (not a derived type).</summary>
    public AndWhichConstraint<TSelf, T> BeOfType<T>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be of type {0}{reason}, but found <null>.", typeof(T))
            .ForCondition(Subject is null || Subject.GetType() == typeof(T)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be of type {0}{reason}, but found {1}.", typeof(T), Subject?.GetType());
        return new((TSelf)this, Subject is T t ? t : default!);
    }

    /// <summary>Asserts the subject is assignable to <typeparamref name="T"/>.</summary>
    public AndWhichConstraint<TSelf, T> BeAssignableTo<T>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is T).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be assignable to {0}{reason}, but found {1}.", typeof(T), Subject?.GetType());
        return new((TSelf)this, Subject is T t ? t : default!);
    }

    public AndConstraint<TSelf> NotBeOfType<T>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject.GetType() != typeof(T)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be of type {0}{reason}.", typeof(T));
        return new((TSelf)this);
    }

    public AndConstraint<TSelf> NotBeAssignableTo<T>(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not T).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be assignable to {0}{reason}, but it was.", typeof(T));
        return new((TSelf)this);
    }

    /// <summary>Asserts the subject matches an arbitrary predicate; the predicate text appears in the failure.</summary>
    public AndConstraint<TSelf> Match(Func<TSubject?, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Assert().ForCondition(predicate(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match the given predicate{reason}, but {0} did not.", Subject);
        return new((TSelf)this);
    }
}
