using System.Reflection;
using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Reflection;

namespace MintPlayer.Assertions;

/// <summary>
/// Should() overloads for the reflection family: assemblies, member infos, and type selectors.
/// </summary>
/// <remarks>
/// These types are only ever the subject of an assertion in a test that is about a codebase's shape,
/// so the reflection they do reaches nobody else. See the remarks on
/// <c>TypeAssertions</c>'s member half for why that is what makes the family admissible at all.
/// </remarks>
public static partial class AssertionExtensions
{
    /// <summary>Asserts on an <see cref="Assembly"/>: references, defined types, signing.</summary>
    public static AssemblyAssertions Should(this Assembly? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on every type a <see cref="TypeSelector"/> matched.</summary>
    public static TypeSelectorAssertions Should(this TypeSelector? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on a <see cref="MemberInfo"/> — a method, property, field or constructor.</summary>
    public static MemberInfoAssertions Should(this MemberInfo? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on a <see cref="MethodInfo"/>: everything a member has, plus the method-only checks.</summary>
    public static MethodInfoAssertions Should(this MethodInfo? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on a <see cref="PropertyInfo"/>: everything a member has, plus the property-only checks.</summary>
    public static PropertyInfoAssertions Should(this PropertyInfo? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
