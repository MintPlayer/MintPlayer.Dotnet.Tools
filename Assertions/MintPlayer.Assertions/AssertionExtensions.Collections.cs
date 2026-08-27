using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Collections;

namespace MintPlayer.Assertions;

public static partial class AssertionExtensions
{
    /// <summary>Returns assertions on a sequence of items.</summary>
    public static GenericCollectionAssertions<T> Should<T>(this IEnumerable<T>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>
    /// Returns assertions on a dictionary (or any other sequence of key/value pairs). For
    /// dictionary subjects overload resolution prefers this over the plain sequence overload,
    /// because the constructed parameter type is more specific.
    /// </summary>
    public static GenericDictionaryAssertions<TKey, TValue> Should<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>
    /// Returns assertions on an array — the full collection surface, not the narrower span one.
    /// This overload is required: since C# 14 an array converts implicitly to
    /// <see cref="ReadOnlySpan{T}"/>, and that conversion outranks the one to
    /// <see cref="IEnumerable{T}"/>, so without an exact <c>T[]</c> match every array subject
    /// would silently bind to the span overload.
    /// </summary>
    public static GenericCollectionAssertions<T> Should<T>(this T[]? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a span.</summary>
    public static SpanAssertions<T> Should<T>(this Span<T> subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a read-only span.</summary>
    public static ReadOnlySpanAssertions<T> Should<T>(this ReadOnlySpan<T> subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
