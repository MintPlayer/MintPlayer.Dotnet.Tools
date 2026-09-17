using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Specialized;

namespace MintPlayer.Assertions;

/// <summary>
/// Should() overloads for streams and value-task-returning delegates, plus the
/// <c>ExecutionTimeOf</c> entry point.
/// </summary>
public static partial class AssertionExtensions
{
    public static StreamAssertions Should(this Stream? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>
    /// Asserts on a <see cref="ValueTask"/>-returning delegate, using the same surface as a
    /// <c>Func&lt;Task&gt;</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The subject is a delegate, not a <c>ValueTask</c>, and that is not a stylistic
    /// preference.</b> A <c>ValueTask</c> may be awaited only ONCE — awaiting a consumed one is
    /// undefined behaviour, not an exception you can assert on — so an assertion surface that took
    /// the value itself would be a trap in a library whose whole point is catching that class of
    /// mistake. Taking the delegate means every assertion produces its own value to await.
    /// <para>
    /// The conversion to <c>Func&lt;Task&gt;</c> uses <c>AsTask()</c>, which is the documented way to
    /// make a ValueTask awaitable more than once and is exactly what a consumer would write by hand.
    /// </para>
    /// </remarks>
    public static AsyncFunctionAssertions Should(this Func<ValueTask>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : () => subject().AsTask(), subjectExpression);

    /// <summary>
    /// Asserts on a <see cref="ValueTask{TResult}"/>-returning delegate. See the remark on the
    /// non-generic overload for why this takes a delegate rather than the value.
    /// </summary>
    public static GenericAsyncFunctionAssertions<TResult> Should<TResult>(this Func<ValueTask<TResult>>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : () => subject().AsTask(), subjectExpression);

    /// <summary>
    /// Measures how long <paramref name="action"/> takes:
    /// <c>ExecutionTimeOf(() =&gt; Work()).Should().BeLessThan(…)</c>.
    /// </summary>
    /// <remarks>
    /// The same assertions as <c>action.Should().ExecutionTime()</c>, reachable without naming the
    /// action first. The action is stored and invoked once per assertion, not here.
    /// </remarks>
    public static ExecutionTimeAssertions ExecutionTimeOf(Action action,
        [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new(action, actionExpression);
    }

    /// <summary>
    /// Wraps a <see cref="TaskCompletionSource{TResult}"/>'s task so it can be asserted on without
    /// the caller writing <c>() =&gt; tcs.Task</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Assert on a completed source, or on one something else is about to complete.</b> These
    /// assertions await the task; on a source nobody completes, that is a hang rather than a failed
    /// test — the same shape MPA0001 exists to prevent elsewhere. <c>NotBeCompleted</c> is the one
    /// that is always safe, because it inspects state instead of awaiting.
    /// </remarks>
    public static GenericAsyncFunctionAssertions<TResult> Should<TResult>(this TaskCompletionSource<TResult>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : () => subject.Task, subjectExpression);
}
