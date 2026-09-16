using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a <see cref="TaskCompletionSource"/>: whether something else completed it within a
/// window.
/// </summary>
/// <remarks>
/// <para>
/// The subject here is not a function the assertion invokes — the <c>Task</c> already exists and is
/// already running (or already finished). That is the whole reason this type needs its own
/// assertions rather than reusing <see cref="AsyncFunctionAssertions"/>: wrapping it as
/// <c>() =&gt; tcs.Task</c> works, but reads as though the assertion starts the work, and
/// <c>NotCompleteWithinAsync</c> would then be claiming something about a delegate nobody called.
/// </para>
/// <para>
/// A completion source is the shape a test reaches for when it has to observe a signal raised by
/// code it does not control — a callback, an event handler, a background consumer. The assertion
/// that matters is "something set this, in time", which is what these two methods say.
/// </para>
/// </remarks>
public class TaskCompletionSourceAssertions
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public TaskCompletionSourceAssertions(TaskCompletionSource? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "completion source" : subjectExpression!;
    }

    /// <summary>The completion source under test.</summary>
    public TaskCompletionSource? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the completion source is completed within <paramref name="timeout"/>.</summary>
    /// <remarks>
    /// A faulted or cancelled source is reported as a failure with the exception it carries, not as a
    /// timeout — the two are different problems and deserve different messages.
    /// </remarks>
    public async Task CompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it was <null>.", timeout);
        if (Subject is null) return;

        Exception? caught = null;
        var completed = await TimeoutHelper.CompletesWithin(Subject.Task, timeout).ConfigureAwait(false);
        if (completed)
        {
            try
            {
                await Subject.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete successfully within {0}{reason}, but it ended with {1}: {2}.", timeout, caught?.GetType(), caught?.Message)
            .ForCondition(caught is not null || completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it did not.", timeout);
    }

    /// <summary>Asserts the completion source is still not completed after <paramref name="timeout"/>.</summary>
    /// <remarks>
    /// This one costs the full <paramref name="timeout"/> in wall-clock on the passing path, by
    /// definition: proving nothing happened means waiting. Keep the window short.
    /// </remarks>
    public async Task NotCompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but it was <null>.", timeout);
        if (Subject is null) return;

        var completed = await TimeoutHelper.CompletesWithin(Subject.Task, timeout).ConfigureAwait(false);

        Assert().ForCondition(!completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but it did.", timeout);
    }
}

/// <summary>
/// Assertions on a <see cref="TaskCompletionSource{TResult}"/>. Like the non-generic form, plus the
/// result: <c>CompleteWithinAsync</c> hands it back through <c>Which</c> so the value the source was
/// set with can be asserted on directly.
/// </summary>
public class TaskCompletionSourceAssertions<TResult>
{
    /// <inheritdoc cref="TaskCompletionSourceAssertions(TaskCompletionSource, string)"/>
    public TaskCompletionSourceAssertions(TaskCompletionSource<TResult>? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "completion source" : subjectExpression!;
    }

    /// <summary>The completion source under test.</summary>
    public TaskCompletionSource<TResult>? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>
    /// Asserts the completion source is completed within <paramref name="timeout"/>, and exposes the
    /// result it was set with via <c>Which</c>.
    /// </summary>
    public async Task<AndWhichConstraint<TaskCompletionSourceAssertions<TResult>, TResult>> CompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it was <null>.", timeout);
        if (Subject is null) return new(this, default!);

        Exception? caught = null;
        var result = default(TResult)!;
        var completed = await TimeoutHelper.CompletesWithin(Subject.Task, timeout).ConfigureAwait(false);
        if (completed)
        {
            try
            {
                result = await Subject.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }

        Assert().ForCondition(caught is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete successfully within {0}{reason}, but it ended with {1}: {2}.", timeout, caught?.GetType(), caught?.Message)
            .ForCondition(caught is not null || completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to complete within {0}{reason}, but it did not.", timeout);

        return new(this, result);
    }

    /// <inheritdoc cref="TaskCompletionSourceAssertions.NotCompleteWithinAsync"/>
    public async Task NotCompleteWithinAsync(TimeSpan timeout, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but it was <null>.", timeout);
        if (Subject is null) return;

        var completed = await TimeoutHelper.CompletesWithin(Subject.Task, timeout).ConfigureAwait(false);

        Assert().ForCondition(!completed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to complete within {0}{reason}, but it did.", timeout);
    }
}
