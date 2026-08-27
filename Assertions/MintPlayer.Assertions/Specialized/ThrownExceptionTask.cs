using System.Runtime.CompilerServices;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// The awaitable returned by <c>ThrowAsync</c>/<c>ThrowExactlyAsync</c>: await it for the
/// <see cref="ExceptionAssertions{TException}"/>, or keep chaining assertions on the thrown
/// exception first.
/// </summary>
/// <remarks>
/// This exists so the chain does not force callers to restate a type the compiler already knows.
/// As extension methods over <c>Task&lt;ExceptionAssertions&lt;TException&gt;&gt;</c>, drilling
/// into an inner exception had to be written
/// <c>WithInnerException&lt;HttpRequestException, SocketException&gt;()</c>: C# takes explicit
/// type arguments all-or-nothing, so the un-inferable <c>TInner</c> dragged the perfectly
/// inferable <c>TException</c> along with it. Here <c>TException</c> comes from the receiver, so
/// only the genuinely new type is named — matching the synchronous surface exactly:
/// <c>WithInnerException&lt;SocketException&gt;()</c>.
/// </remarks>
public readonly struct ThrownExceptionTask<TException> where TException : Exception
{
    private readonly Task<ExceptionAssertions<TException>> task;

    internal ThrownExceptionTask(Task<ExceptionAssertions<TException>> task) => this.task = task;

    /// <summary>Makes this directly awaitable, yielding the assertions on the thrown exception.</summary>
    public TaskAwaiter<ExceptionAssertions<TException>> GetAwaiter() => task.GetAwaiter();

    /// <summary>Asserts the thrown exception's message matches a wildcard pattern, case-sensitively.</summary>
    public ThrownExceptionTask<TException> WithMessage(string wildcardPattern, string? because = null, params object?[] becauseArgs)
        => WithMessage(wildcardPattern, StringComparison.Ordinal, because, becauseArgs);

    /// <summary>Asserts the thrown exception's message matches a wildcard pattern using an explicit comparison.</summary>
    public ThrownExceptionTask<TException> WithMessage(string wildcardPattern, StringComparison comparison, string? because = null, params object?[] becauseArgs)
        => Continue(assertions => assertions.WithMessage(wildcardPattern, comparison, because, becauseArgs));

    /// <summary>Asserts the thrown exception's ParamName matches (for ArgumentException and friends).</summary>
    public ThrownExceptionTask<TException> WithParameterName(string expectedParameterName, string? because = null, params object?[] becauseArgs)
        => Continue(assertions => assertions.WithParameterName(expectedParameterName, because, becauseArgs));

    /// <summary>Asserts the thrown exception satisfies a predicate.</summary>
    public ThrownExceptionTask<TException> Where(Func<TException, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
        => Continue(assertions => assertions.Where(predicate, because, becauseArgs, predicateExpression));

    /// <summary>Asserts the thrown exception has an inner exception assignable to <typeparamref name="TInner"/>, and drills into it.</summary>
    public ThrownExceptionTask<TInner> WithInnerException<TInner>(string? because = null, params object?[] becauseArgs)
        where TInner : Exception
        => Drill(assertions => assertions.WithInnerException<TInner>(because, becauseArgs));

    /// <summary>Asserts the thrown exception has an inner exception of exactly <typeparamref name="TInner"/>, and drills into it.</summary>
    public ThrownExceptionTask<TInner> WithInnerExactly<TInner>(string? because = null, params object?[] becauseArgs)
        where TInner : Exception
        => Drill(assertions => assertions.WithInnerExactly<TInner>(because, becauseArgs));

    private ThrownExceptionTask<TException> Continue(Action<ExceptionAssertions<TException>> apply)
        => new(Applied(apply));

    private async Task<ExceptionAssertions<TException>> Applied(Action<ExceptionAssertions<TException>> apply)
    {
        var assertions = await task.ConfigureAwait(false);
        apply(assertions);
        return assertions;
    }

    private ThrownExceptionTask<TInner> Drill<TInner>(Func<ExceptionAssertions<TException>, ExceptionAssertions<TInner>> drill)
        where TInner : Exception
        => new(Drilled(drill));

    private async Task<ExceptionAssertions<TInner>> Drilled<TInner>(Func<ExceptionAssertions<TException>, ExceptionAssertions<TInner>> drill)
        where TInner : Exception
    {
        var assertions = await task.ConfigureAwait(false);
        return drill(assertions);
    }
}
