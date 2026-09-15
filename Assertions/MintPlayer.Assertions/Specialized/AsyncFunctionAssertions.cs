using System.Diagnostics;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="Func{TResult}"/> of <see cref="Task"/>: whether awaiting it throws
/// (a specific exception type, exactly or assignable), does not throw (immediately or after
/// retrying for a while), or completes within a timeout. The function is invoked and awaited by
/// the assertion method itself; awaiting already unwraps <see cref="AggregateException"/>, so the
/// exception observed is the task's original one.
/// </summary>
public class AsyncFunctionAssertions
{
    public AsyncFunctionAssertions(Func<Task>? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "async function" : subjectExpression!;
    }

    /// <summary>The asynchronous function under test.</summary>
    public Func<Task>? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts that awaiting the function throws an exception assignable to <typeparamref name="TException"/>.</summary>
    public ThrownExceptionTask<TException> ThrowAsync<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
        => new(ThrowAsyncCore<TException>(because, becauseArgs));

    private async Task<ExceptionAssertions<TException>> ThrowAsyncCore<TException>(string? because, object?[] becauseArgs)
        where TException : Exception
    {
        var caught = await InvokeAndCatchAsync(because, becauseArgs, typeof(TException)).ConfigureAwait(false);
        var match = ExceptionExtractor.Assignable<TException>(caught);
        Assert().ForCondition(caught is null || match is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(match, SubjectExpression);
    }

    /// <summary>Asserts that awaiting the function throws an exception of exactly type <typeparamref name="TException"/> (not a derived type).</summary>
    public ThrownExceptionTask<TException> ThrowExactlyAsync<TException>(string? because = null, params object?[] becauseArgs)
        where TException : Exception
        => new(ThrowExactlyAsyncCore<TException>(because, becauseArgs));

    private async Task<ExceptionAssertions<TException>> ThrowExactlyAsyncCore<TException>(string? because, object?[] becauseArgs)
        where TException : Exception
    {
        var caught = await InvokeAndCatchAsync(because, becauseArgs, typeof(TException)).ConfigureAwait(false);
        var exact = ExceptionExtractor.Exactly<TException>(caught);
        Assert().ForCondition(caught is null || exact is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw exactly {0}{reason}, but {1} was thrown: {2}.", typeof(TException), caught?.GetType(), caught?.Message);
        return new(exact, SubjectExpression);
    }

    /// <summary>Asserts that awaiting the function does not throw any exception.</summary>
    public async Task NotThrowAsync(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw{reason}, but the function was <null>.");
        if (Subject is null) return;

        Exception? caught = null;
        try { await Subject.Invoke().ConfigureAwait(false); }
        catch (Exception ex) { caught = ex; }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to throw{reason}, but it threw {0}: {1}.{2}", caught?.GetType(), caught?.Message,
                caught is null ? null : Environment.NewLine + caught.StackTrace);
    }

    /// <summary>
    /// Repeatedly invokes and awaits the function every <paramref name="pollInterval"/> until it
    /// stops throwing or <paramref name="waitTime"/> has elapsed. Fails with the last exception
    /// when it never succeeded.
    /// </summary>
    public async Task NotThrowAfterAsync(TimeSpan waitTime, TimeSpan pollInterval, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(waitTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(pollInterval, TimeSpan.Zero);

        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw after {0}{reason}, but the function was <null>.", waitTime);
        if (Subject is null) return;

        var stopwatch = Stopwatch.StartNew();
        Exception? last;
        while (true)
        {
            try
            {
                await Subject.Invoke().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (stopwatch.Elapsed >= waitTime) break;
            await Task.Delay(pollInterval).ConfigureAwait(false);
        }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw after {0}{reason}, but it kept throwing {1}: {2}.", waitTime, last.GetType(), last.Message);
    }

    /// <summary>
    /// Asserts that the task returned by the function completes successfully within
    /// <paramref name="timeout"/>. A task that faults within the timeout fails the assertion with
    /// the task's exception.
    /// </summary>
    public async Task CompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but the function was <null>.", timeout);
        if (Subject is null) return;

        Exception? caught = null;
        var completed = false;
        try
        {
            var task = Subject.Invoke();
            completed = await TimeoutHelper.CompletesWithin(task, timeout).ConfigureAwait(false);
            if (completed) await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete successfully within {0}{reason}, but it threw {1}: {2}.", timeout, caught?.GetType(), caught?.Message)
            .ForCondition(caught is not null || completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it did not.", timeout);
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

    /// <summary>Asserts that awaiting the function throws <b>something</b>, without constraining the type.</summary>
    public ThrownExceptionTask<Exception> ThrowAsync(string? because = null, params object?[] becauseArgs)
        => ThrowAsync<Exception>(because, becauseArgs);

    /// <summary>
    /// Asserts that awaiting the function does not throw <typeparamref name="TException"/>. Other
    /// exceptions are allowed through.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>NotThrowAsync()</c>, which forbids every exception. This one says "whatever
    /// else happens, not this".
    /// </remarks>
    public async Task NotThrowAsync<TException>(string? because = null, object?[]? becauseArgs = null)
        where TException : Exception
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to throw {0}{reason}, but the function was <null>.", typeof(TException));
        if (Subject is null) return;

        Exception? caught = null;
        try { await Subject.Invoke().ConfigureAwait(false); }
        catch (Exception ex) { caught = ex; }

        var match = ExceptionExtractor.Assignable<TException>(caught);
        Assert().ForCondition(match is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to throw {0}{reason}, but it threw {1}: {2}.", typeof(TException), match?.GetType(), match?.Message);
        return;
    }

    /// <summary>
    /// Asserts that awaiting the function throws <typeparamref name="TException"/> <b>and</b> does so
    /// within <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// For code that is supposed to fail fast: a retry loop that gives up, a circuit breaker that
    /// opens. Asserting only that it throws would pass even if it took a minute to get there.
    /// </remarks>
    public async Task<ExceptionAssertions<TException>> ThrowWithinAsync<TException>(TimeSpan timeout, string? because = null, object?[]? becauseArgs = null)
        where TException : Exception
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0} within {1}{reason}, but the function was <null>.", typeof(TException), timeout);
        if (Subject is null) return new(null, SubjectExpression);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? caught = null;
        try { await Subject.Invoke().ConfigureAwait(false); }
        catch (Exception ex) { caught = ex; }
        stopwatch.Stop();

        var match = ExceptionExtractor.Assignable<TException>(caught);
        Assert().ForCondition(match is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0} within {1}{reason}, but it threw {2}.", typeof(TException), timeout, caught?.GetType())
            .ForCondition(match is null || stopwatch.Elapsed <= timeout).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to throw {0} within {1}{reason}, but it took {2}.", typeof(TException), timeout, stopwatch.Elapsed);
        return new(match, SubjectExpression);
    }

    /// <summary>
    /// Asserts that the task returned by the function does <b>not</b> complete within
    /// <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// The assertion for code that is supposed to block: a semaphore that should hold, a consumer
    /// that should wait for a producer. Without it there is no way to state "this must not finish
    /// yet" other than sleeping and hoping.
    /// </remarks>
    public async Task NotCompleteWithinAsync(TimeSpan timeout, string? because = null, object?[]? becauseArgs = null)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but the function was <null>.", timeout);
        if (Subject is null) return;

        var completed = false;
        try
        {
            var task = Subject.Invoke();
            completed = await TimeoutHelper.CompletesWithin(task, timeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A fault IS a completion for this assertion's purposes: the task stopped running
            // inside the window, which is exactly what the caller said must not happen. Swallowed
            // rather than rethrown so the message below is the one reported.
            completed = true;
        }

        Assert().ForCondition(!completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but it did.", timeout);
        return;
    }
}
