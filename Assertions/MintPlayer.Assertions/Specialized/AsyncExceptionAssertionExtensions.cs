using System.Runtime.CompilerServices;

// Root namespace, not MintPlayer.Assertions.Specialized: chaining onto an async throw assertion
// is common enough that it must work off the single `using MintPlayer.Assertions;`, the same way
// the equivalency extensions do.
namespace MintPlayer.Assertions;

using MintPlayer.Assertions.Specialized;

/// <summary>
/// Lets the assertions on a thrown exception be chained straight onto an async throw assertion:
/// <c>await act.Should().ThrowAsync&lt;T&gt;().WithMessage("*timed out*")</c>.
/// </summary>
/// <remarks>
/// <see cref="AsyncFunctionAssertions.ThrowAsync{TException}"/> returns
/// <c>Task&lt;ExceptionAssertions&lt;TException&gt;&gt;</c>, so without these the caller has to
/// await first and parenthesise — <c>(await act.Should().ThrowAsync&lt;T&gt;()).WithMessage(…)</c>
/// — which is easy to get wrong and reads poorly. Each method here awaits the antecedent and
/// applies the assertion, so the whole chain stays one awaited expression.
/// </remarks>
public static class AsyncExceptionAssertionExtensions
{
    /// <summary>Asserts the thrown exception's message matches a wildcard pattern (case-insensitive).</summary>
    public static async Task<ExceptionAssertions<TException>> WithMessage<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        string wildcardPattern, string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var awaited = await assertions.ConfigureAwait(false);
        awaited.WithMessage(wildcardPattern, because, becauseArgs);
        return awaited;
    }

    /// <summary>Asserts the thrown exception's ParamName matches (for ArgumentException and friends).</summary>
    public static async Task<ExceptionAssertions<TException>> WithParameterName<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        string expectedParameterName, string? because = null, params object?[] becauseArgs)
        where TException : Exception
    {
        var awaited = await assertions.ConfigureAwait(false);
        awaited.WithParameterName(expectedParameterName, because, becauseArgs);
        return awaited;
    }

    /// <summary>Asserts the thrown exception has an inner exception assignable to <typeparamref name="TInner"/>.</summary>
    public static async Task<ExceptionAssertions<TInner>> WithInnerException<TException, TInner>(
        this Task<ExceptionAssertions<TException>> assertions,
        string? because = null, params object?[] becauseArgs)
        where TException : Exception
        where TInner : Exception
    {
        var awaited = await assertions.ConfigureAwait(false);
        return awaited.WithInnerException<TInner>(because, becauseArgs);
    }

    /// <summary>Asserts the thrown exception has an inner exception of exactly <typeparamref name="TInner"/>.</summary>
    public static async Task<ExceptionAssertions<TInner>> WithInnerExactly<TException, TInner>(
        this Task<ExceptionAssertions<TException>> assertions,
        string? because = null, params object?[] becauseArgs)
        where TException : Exception
        where TInner : Exception
    {
        var awaited = await assertions.ConfigureAwait(false);
        return awaited.WithInnerExactly<TInner>(because, becauseArgs);
    }

    /// <summary>Asserts the thrown exception satisfies a predicate.</summary>
    public static async Task<ExceptionAssertions<TException>> Where<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        Func<TException, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
        where TException : Exception
    {
        var awaited = await assertions.ConfigureAwait(false);
        awaited.Where(predicate, because, becauseArgs, predicateExpression);
        return awaited;
    }
}
