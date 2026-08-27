using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="Func{TResult}"/> of <see cref="Task{TResult}"/>: whether awaiting it
/// throws (a specific exception type, exactly or assignable), does not throw, or completes within
/// a timeout — in the latter two cases exposing the task's result for further assertions via
/// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/>. The function is invoked and
/// awaited by the assertion method itself; awaiting already unwraps
/// <see cref="AggregateException"/>, so the exception observed is the task's original one.
/// </summary>
public class GenericAsyncFunctionAssertions<TResult>
{
    public GenericAsyncFunctionAssertions(Func<Task<TResult>>? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "async function" : subjectExpression!;
    }

    /// <summary>The asynchronous function under test.</summary>
    public Func<Task<TResult>>? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts that awaiting the function throws an exception assignable to <typeparamref name="TException"/>.</summary>
    public async Task<ExceptionAssertions<TException>> ThrowAsync<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var caught = await InvokeAndCatchAsync(because, becauseArgs, typeof(TException)).ConfigureAwait(false);
        Assert().ForCondition(caught is null || caught is TException).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(caught as TException, SubjectExpression);
    }

    /// <summary>Asserts that awaiting the function throws an exception of exactly type <typeparamref name="TException"/> (not a derived type).</summary>
    public async Task<ExceptionAssertions<TException>> ThrowExactlyAsync<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var caught = await InvokeAndCatchAsync(because, becauseArgs, typeof(TException)).ConfigureAwait(false);
        Assert().ForCondition(caught is null || caught.GetType() == typeof(TException)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw exactly {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(caught?.GetType() == typeof(TException) ? (TException?)caught : null, SubjectExpression);
    }

    /// <summary>
    /// Asserts that awaiting the function does not throw; the task's result is available via
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/>.
    /// </summary>
    public async Task<AndWhichConstraint<GenericAsyncFunctionAssertions<TResult>, TResult>> NotThrowAsync(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw{reason}, but the function was <null>.");
        if (Subject is null) return new(this, default!);

        Exception? caught = null;
        TResult result = default!;
        try { result = await Subject.Invoke().ConfigureAwait(false); }
        catch (Exception ex) { caught = ex; }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to throw{reason}, but it threw {0}: {1}.{2}", caught?.GetType(), caught?.Message,
                caught is null ? null : Environment.NewLine + caught.StackTrace);
        return new(this, result);
    }

    /// <summary>
    /// Asserts that the task returned by the function completes successfully within
    /// <paramref name="timeout"/>; the task's result is available via
    /// <see cref="AndWhichConstraint{TAssertions, TWhich}.Which"/>. A task that faults within the
    /// timeout fails the assertion with the task's exception.
    /// </summary>
    public async Task<AndWhichConstraint<GenericAsyncFunctionAssertions<TResult>, TResult>> CompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but the function was <null>.", timeout);
        if (Subject is null) return new(this, default!);

        Exception? caught = null;
        TResult result = default!;
        var completed = false;
        try
        {
            var task = Subject.Invoke();
            completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
            if (completed) result = await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete successfully within {0}{reason}, but it threw {1}: {2}.", timeout, caught?.GetType(), caught?.Message)
            .ForCondition(caught is not null || completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it did not.", timeout);
        return new(this, result);
    }

    /// <summary>
    /// Invokes and awaits the function and returns the exception it threw, reporting a failure
    /// when it threw nothing (or was null). Shared by <see cref="ThrowAsync{TException}"/> and
    /// <see cref="ThrowExactlyAsync{TException}"/>.
    /// </summary>
    private async Task<Exception?> InvokeAndCatchAsync(string? because, object?[] becauseArgs, Type expectedType)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but the function was <null>.", expectedType);
        if (Subject is null) return null;

        try { await Subject.Invoke().ConfigureAwait(false); }
        catch (Exception ex) { return ex; }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but no exception was thrown.", expectedType);
        return null;
    }
}
