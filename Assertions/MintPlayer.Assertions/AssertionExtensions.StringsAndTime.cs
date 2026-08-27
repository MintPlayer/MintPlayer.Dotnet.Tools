using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions;

/// <summary>Should() entry points for strings and date/time subjects.</summary>
public static partial class AssertionExtensions
{
    /// <summary>Returns assertions for a string subject.</summary>
    public static StringAssertions Should(this string? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="DateTime"/> subject.</summary>
    public static DateTimeAssertions Should(this DateTime subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a nullable <see cref="DateTime"/> subject.</summary>
    public static DateTimeAssertions Should(this DateTime? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="DateTimeOffset"/> subject.</summary>
    public static DateTimeOffsetAssertions Should(this DateTimeOffset subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a nullable <see cref="DateTimeOffset"/> subject.</summary>
    public static DateTimeOffsetAssertions Should(this DateTimeOffset? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="DateOnly"/> subject.</summary>
    public static DateOnlyAssertions Should(this DateOnly subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a nullable <see cref="DateOnly"/> subject.</summary>
    public static DateOnlyAssertions Should(this DateOnly? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="TimeOnly"/> subject.</summary>
    public static TimeOnlyAssertions Should(this TimeOnly subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a nullable <see cref="TimeOnly"/> subject.</summary>
    public static TimeOnlyAssertions Should(this TimeOnly? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a <see cref="TimeSpan"/> subject.</summary>
    public static TimeSpanAssertions Should(this TimeSpan subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions for a nullable <see cref="TimeSpan"/> subject.</summary>
    public static TimeSpanAssertions Should(this TimeSpan? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
