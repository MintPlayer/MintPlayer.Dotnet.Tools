using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="Func{T}"/>: whether invoking it throws (a specific exception type,
/// exactly or assignable) or does not throw — in which case the returned value is exposed for
/// further assertions via <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/>.
/// The function is invoked by the assertion method itself.
/// </summary>
public class FuncAssertions<T>
{
    public FuncAssertions(Func<T>? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "function" : subjectExpression!;
    }

    /// <summary>The function under test.</summary>
    public Func<T>? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts that invoking the function throws an exception assignable to <typeparamref name="TException"/>.</summary>
    public ExceptionAssertions<TException> Throw<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var caught = InvokeAndCatch(because, becauseArgs, typeof(TException));
        Assert().ForCondition(caught is null || caught is TException).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(caught as TException, SubjectExpression);
    }

    /// <summary>Asserts that invoking the function throws an exception of exactly type <typeparamref name="TException"/> (not a derived type).</summary>
    public ExceptionAssertions<TException> ThrowExactly<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var caught = InvokeAndCatch(because, becauseArgs, typeof(TException));
        Assert().ForCondition(caught is null || caught.GetType() == typeof(TException)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw exactly {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(caught?.GetType() == typeof(TException) ? (TException?)caught : null, SubjectExpression);
    }

    /// <summary>
    /// Asserts that invoking the function does not throw; the returned value is available via
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/>.
    /// </summary>
    public AndWhichConstraint<FuncAssertions<T>, T> NotThrow(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw{reason}, but the function was <null>.");
        if (Subject is null) return new(this, default!);

        Exception? caught = null;
        T result = default!;
        try { result = Subject.Invoke(); }
        catch (Exception ex) { caught = ex; }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to throw{reason}, but it threw {0}: {1}.{2}", caught?.GetType(), caught?.Message,
                caught is null ? null : Environment.NewLine + caught.StackTrace);
        return new(this, result);
    }

    /// <summary>
    /// Invokes the function and returns the exception it threw, reporting a failure when it threw
    /// nothing (or was null). Shared by <see cref="Throw{TException}"/> and <see cref="ThrowExactly{TException}"/>.
    /// </summary>
    private Exception? InvokeAndCatch(string? because, object?[] becauseArgs, Type expectedType)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but the function was <null>.", expectedType);
        if (Subject is null) return null;

        try { Subject.Invoke(); }
        catch (Exception ex) { return ex; }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but no exception was thrown.", expectedType);
        return null;
    }
}
