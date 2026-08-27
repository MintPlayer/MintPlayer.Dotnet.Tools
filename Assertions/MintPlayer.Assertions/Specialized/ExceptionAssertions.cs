using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Formatting;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Specialized;

/// <summary>
/// Assertions on a caught exception: its message (wildcard matching), inner exceptions,
/// parameter name (for <see cref="ArgumentException"/>-derived exceptions) and arbitrary
/// predicates. Usually obtained from <c>action.Should().Throw&lt;T&gt;()</c>.
/// </summary>
public class ExceptionAssertions<TException> : ReferenceTypeAssertions<TException, ExceptionAssertions<TException>>
    where TException : Exception
{
    public ExceptionAssertions(TException? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>The caught exception, for direct inspection (same as <see cref="ReferenceTypeAssertions{TSubject, TSelf}.Subject"/>).</summary>
    public TException Which => Subject!;

    /// <summary>
    /// Asserts the exception message matches the given wildcard pattern, <b>case-sensitively</b>.
    /// <c>*</c> matches any sequence (including newlines), <c>?</c> matches exactly one character.
    /// To ignore case, pass a <see cref="StringComparison"/>:
    /// <c>WithMessage("*not found*", StringComparison.OrdinalIgnoreCase)</c>.
    /// </summary>
    public AndConstraint<ExceptionAssertions<TException>> WithMessage(string wildcardPattern, string? because = null, params object?[] becauseArgs)
        => WithMessage(wildcardPattern, StringComparison.Ordinal, because, becauseArgs);

    /// <summary>
    /// Asserts the exception message matches the given wildcard pattern using an explicit
    /// <paramref name="comparison"/> — so the call site says whether case matters instead of
    /// leaving the reader to guess.
    /// </summary>
    /// <remarks>
    /// A separate overload rather than an optional parameter on the one above: an optional
    /// parameter would have to sit before <paramref name="because"/> and would break every
    /// existing positional <c>WithMessage(pattern, "reason")</c> call.
    /// </remarks>
    public AndConstraint<ExceptionAssertions<TException>> WithMessage(string wildcardPattern, StringComparison comparison, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(wildcardPattern);
        var casing = comparison is StringComparison.Ordinal or StringComparison.InvariantCulture or StringComparison.CurrentCulture
            ? "matching"
            : "matching (ignoring case)";
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith($"Expected {{subject}} to have a message {casing} {{0}}{{reason}}, but the exception was <null>.", wildcardPattern)
            .ForCondition(Subject is null || WildcardPattern.IsMatch(Subject.Message, wildcardPattern, comparison)).BecauseOf(because, becauseArgs)
            .FailWith($"Expected {{subject}} to have a message {casing} {{0}}{{reason}}, but found {{1}}.", wildcardPattern, Subject?.Message);
        return new(this);
    }

    /// <summary>Asserts the exception has an inner exception assignable to <typeparamref name="TInner"/> and drills into it.</summary>
    public ExceptionAssertions<TInner> WithInnerException<TInner>(string? because = null, params object?[] becauseArgs)
        where TInner : Exception
    {
        var inner = Subject?.InnerException;
        Assert().ForCondition(inner is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an inner exception of type {0}{reason}, but it has none.", typeof(TInner))
            .ForCondition(inner is null || inner is TInner).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an inner exception of type {0}{reason}, but found {1}: {2}.", typeof(TInner), inner?.GetType(), inner?.Message);
        return new(inner as TInner, SubjectExpression + ".InnerException");
    }

    /// <summary>Asserts the exception has an inner exception of exactly type <typeparamref name="TInner"/> (not a derived type) and drills into it.</summary>
    public ExceptionAssertions<TInner> WithInnerExactly<TInner>(string? because = null, params object?[] becauseArgs)
        where TInner : Exception
    {
        var inner = Subject?.InnerException;
        Assert().ForCondition(inner is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an inner exception of exactly type {0}{reason}, but it has none.", typeof(TInner))
            .ForCondition(inner is null || inner.GetType() == typeof(TInner)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an inner exception of exactly type {0}{reason}, but found {1}: {2}.", typeof(TInner), inner?.GetType(), inner?.Message);
        return new(inner?.GetType() == typeof(TInner) ? (TInner?)inner : null, SubjectExpression + ".InnerException");
    }

    /// <summary>
    /// Asserts the exception is an <see cref="ArgumentException"/> (or derived) whose
    /// <see cref="ArgumentException.ParamName"/> equals <paramref name="expectedParameterName"/>.
    /// </summary>
    public AndConstraint<ExceptionAssertions<TException>> WithParameterName(string expectedParameterName, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedParameterName);
        var argumentException = Subject as ArgumentException;
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have parameter name {0}{reason}, but the exception was <null>.", expectedParameterName)
            .ForCondition(Subject is null || argumentException is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have parameter name {0}{reason}, but {1} is not an ArgumentException.", expectedParameterName, Subject?.GetType())
            .ForCondition(argumentException is null || argumentException.ParamName == expectedParameterName).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have parameter name {0}{reason}, but found {1}.", expectedParameterName, argumentException?.ParamName);
        return new(this);
    }

    /// <summary>
    /// Asserts the exception matches an arbitrary predicate; the predicate's source text appears in the failure message.
    /// </summary>
    /// <remarks>
    /// <paramref name="becauseArgs"/> is a plain array (not <c>params</c>) so that
    /// <paramref name="predicateExpression"/> can be captured automatically as the last parameter.
    /// </remarks>
    public AndConstraint<ExceptionAssertions<TException>> Where(Func<TException, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match condition ({0}){reason}, but the exception was <null>.", predicateExpression)
            .ForCondition(Subject is null || predicate(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match condition ({0}){reason}, but {1} did not.", predicateExpression, Subject);
        return new(this);
    }
}
