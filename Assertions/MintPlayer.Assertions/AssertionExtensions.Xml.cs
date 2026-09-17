using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MintPlayer.Assertions.Xml;

namespace MintPlayer.Assertions;

/// <summary>Should() overloads for LINQ-to-XML.</summary>
public static partial class AssertionExtensions
{
    public static XDocumentAssertions Should(this XDocument? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);

    // XElement derives from XContainer, which is an IEnumerable of nothing in particular — so
    // without this overload an XElement subject would bind to Should<T>(this IEnumerable<T>) and
    // offer collection assertions on a document node. A non-generic overload on the exact type wins,
    // which is what makes this the fix rather than a convenience.
    public static XElementAssertions Should(this XElement? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject, subjectExpression);
}
