using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Specialized;

namespace MintPlayer.Assertions;

/// <summary>
/// Should() overloads for delegates (actions, functions and asynchronous functions), plus the
/// Invoking/Awaiting helpers that wrap a subject into such a delegate:
/// <c>sut.Invoking(s =&gt; s.Do()).Should().Throw&lt;X&gt;()</c>.
/// </summary>
public static partial class AssertionExtensions
{
    public static ActionAssertions Should(this Action? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    public static FuncAssertions<T> Should<T>(this Func<T>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    // Overload resolution: for a Func<Task> subject both this overload and Should<T>(Func<T>)
    // (with T = Task) are applicable as exact matches, and a non-generic method is preferred
    // over a generic one by the betterness rules — so Func<Task> lands here, not in
    // FuncAssertions<Task>. A Func<Task<TResult>> is convertible to Func<Task> (covariance),
    // but Should<TResult>(Func<Task<TResult>>) matches it exactly and therefore wins below.
    public static AsyncFunctionAssertions Should(this Func<Task>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    public static GenericAsyncFunctionAssertions<TResult> Should<TResult>(this Func<Task<TResult>>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on a <see cref="ValueTask"/>-returning function.</summary>
    /// <remarks>
    /// <para>
    /// <c>ValueTask</c> had no support at all: a <c>Func&lt;ValueTask&gt;</c> bound to
    /// <c>Should&lt;T&gt;(Func&lt;T&gt;)</c> and produced <c>FuncAssertions&lt;ValueTask&gt;</c>,
    /// which can only assert that <i>creating</i> the ValueTask throws — never that awaiting it
    /// does. Since the whole point of ValueTask is that the synchronous path allocates nothing, the
    /// interesting exception is almost always on the await. Tests written that way passed for the
    /// wrong reason.
    /// </para>
    /// <para>
    /// Adapting to <c>Task</c> rather than duplicating the assertion classes: <c>AsTask()</c> is the
    /// documented conversion, and every async assertion then works unchanged. It costs one Task
    /// allocation per assertion — irrelevant here, because these assertions are about an exception
    /// being thrown, which allocates far more than the adapter does. ValueTask's allocation-free
    /// promise is about production hot paths, not about the assertion that observes them.
    /// </para>
    /// </remarks>
    public static AsyncFunctionAssertions Should(this Func<ValueTask>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : () => subject().AsTask(), subjectExpression);

    /// <inheritdoc cref="Should(Func{ValueTask}, string)"/>
    public static GenericAsyncFunctionAssertions<TResult> Should<TResult>(this Func<ValueTask<TResult>>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : () => subject().AsTask(), subjectExpression);

    /// <summary>Asserts on a <see cref="Stream"/>: its capabilities, length and position.</summary>
    public static StreamAssertions Should(this Stream? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>
    /// Asserts on a <see cref="BufferedStream"/>. A separate overload so <c>HaveBufferSize</c> is
    /// reachable without a cast; a <c>BufferedStream</c> would otherwise bind to the
    /// <see cref="Stream"/> overload above and lose it.
    /// </summary>
    public static BufferedStreamAssertions Should(this BufferedStream? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on a <see cref="TaskCompletionSource"/>: whether something completes it in time.</summary>
    public static TaskCompletionSourceAssertions Should(this TaskCompletionSource? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <inheritdoc cref="Should(TaskCompletionSource, string)"/>
    public static TaskCompletionSourceAssertions<TResult> Should<TResult>(this TaskCompletionSource<TResult>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Wraps an action on the subject so it can be asserted: <c>sut.Invoking(s =&gt; s.Do()).Should().Throw&lt;X&gt;()</c>.</summary>
    public static Action Invoking<T>(this T subject, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => action(subject);
    }

    /// <summary>Wraps a function on the subject so it can be asserted: <c>sut.Invoking(s =&gt; s.Get()).Should().NotThrow()</c>.</summary>
    public static Func<TResult> Invoking<T, TResult>(this T subject, Func<T, TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return () => func(subject);
    }

    /// <summary>Wraps an asynchronous action on the subject so it can be asserted: <c>await sut.Awaiting(s =&gt; s.DoAsync()).Should().ThrowAsync&lt;X&gt;()</c>.</summary>
    public static Func<Task> Awaiting<T>(this T subject, Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => action(subject);
    }

    /// <summary>Wraps an asynchronous function on the subject so it can be asserted: <c>await sut.Awaiting(s =&gt; s.GetAsync()).Should().NotThrowAsync()</c>.</summary>
    public static Func<Task<TResult>> Awaiting<T, TResult>(this T subject, Func<T, Task<TResult>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return () => func(subject);
    }

    /// <summary>Wraps a <see cref="ValueTask"/>-returning member on the subject so it can be asserted.</summary>
    public static Func<ValueTask> Awaiting<T>(this T subject, Func<T, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => action(subject);
    }

    /// <summary>Wraps a <see cref="ValueTask{TResult}"/>-returning member on the subject so it can be asserted.</summary>
    public static Func<ValueTask<TResult>> Awaiting<T, TResult>(this T subject, Func<T, ValueTask<TResult>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return () => func(subject);
    }
}
