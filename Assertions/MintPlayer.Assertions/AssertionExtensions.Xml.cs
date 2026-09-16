using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MintPlayer.Assertions.Xml;

namespace MintPlayer.Assertions;

/// <summary>
/// Should() overloads for <c>System.Xml.Linq</c>.
/// </summary>
/// <remarks>
/// In scope because it is reflection-free, purely additive and needs no dependency —
/// <c>System.Xml.Linq</c> is part of the shared framework. See <see cref="XElementAssertions"/> for
/// what the alternative looked like.
/// </remarks>
public static partial class AssertionExtensions
{
    /// <summary>Asserts on an <see cref="XElement"/>.</summary>
    public static XElementAssertions Should(this XElement? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on an <see cref="XDocument"/>.</summary>
    public static XDocumentAssertions Should(this XDocument? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    /// <summary>Asserts on an <see cref="XAttribute"/>.</summary>
    public static XAttributeAssertions Should(this XAttribute? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
