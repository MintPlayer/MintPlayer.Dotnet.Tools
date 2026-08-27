using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions;

/// <summary>
/// Should() entry points for primitive and value-type subjects. Concrete overloads exist for every
/// built-in numeric type (and its nullable) so overload resolution prefers them over the object
/// catch-all, which would box the subject.
/// </summary>
public static partial class AssertionExtensions
{
    /// <summary>Returns assertions on a <see cref="bool"/> subject.</summary>
    public static BooleanAssertions Should(this bool subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="bool"/> subject.</summary>
    public static BooleanAssertions Should(this bool? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on an <see cref="sbyte"/> subject.</summary>
    public static NumericAssertions<sbyte> Should(this sbyte subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="sbyte"/> subject.</summary>
    public static NumericAssertions<sbyte> Should(this sbyte? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="byte"/> subject.</summary>
    public static NumericAssertions<byte> Should(this byte subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="byte"/> subject.</summary>
    public static NumericAssertions<byte> Should(this byte? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="short"/> subject.</summary>
    public static NumericAssertions<short> Should(this short subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="short"/> subject.</summary>
    public static NumericAssertions<short> Should(this short? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="ushort"/> subject.</summary>
    public static NumericAssertions<ushort> Should(this ushort subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="ushort"/> subject.</summary>
    public static NumericAssertions<ushort> Should(this ushort? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on an <see cref="int"/> subject.</summary>
    public static NumericAssertions<int> Should(this int subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="int"/> subject.</summary>
    public static NumericAssertions<int> Should(this int? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="uint"/> subject.</summary>
    public static NumericAssertions<uint> Should(this uint subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="uint"/> subject.</summary>
    public static NumericAssertions<uint> Should(this uint? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="long"/> subject.</summary>
    public static NumericAssertions<long> Should(this long subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="long"/> subject.</summary>
    public static NumericAssertions<long> Should(this long? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="ulong"/> subject.</summary>
    public static NumericAssertions<ulong> Should(this ulong subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="ulong"/> subject.</summary>
    public static NumericAssertions<ulong> Should(this ulong? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="float"/> subject.</summary>
    public static NumericAssertions<float> Should(this float subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="float"/> subject.</summary>
    public static NumericAssertions<float> Should(this float? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="double"/> subject.</summary>
    public static NumericAssertions<double> Should(this double subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="double"/> subject.</summary>
    public static NumericAssertions<double> Should(this double? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="decimal"/> subject.</summary>
    public static NumericAssertions<decimal> Should(this decimal subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="decimal"/> subject.</summary>
    public static NumericAssertions<decimal> Should(this decimal? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="Guid"/> subject.</summary>
    public static GuidAssertions Should(this Guid subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable <see cref="Guid"/> subject.</summary>
    public static GuidAssertions Should(this Guid? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on an enum subject.</summary>
    public static EnumAssertions<TEnum> Should<TEnum>(this TEnum subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        where TEnum : struct, Enum
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a nullable enum subject.</summary>
    public static EnumAssertions<TEnum> Should<TEnum>(this TEnum? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        where TEnum : struct, Enum
        => new(subject, subjectExpression);

    /// <summary>Returns assertions on a <see cref="Type"/> subject.</summary>
    public static TypeAssertions Should(this Type? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Returns comparison assertions on any <see cref="IComparable{T}"/> subject.</summary>
    public static ComparableAssertions<T> Should<T>(this IComparable<T>? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        where T : IComparable<T>
        => new(subject, subjectExpression);
}
