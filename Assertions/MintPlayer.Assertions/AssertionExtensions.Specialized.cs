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
}
