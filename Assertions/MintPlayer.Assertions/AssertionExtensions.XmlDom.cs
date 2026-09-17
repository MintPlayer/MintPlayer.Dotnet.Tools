using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using MintPlayer.Assertions.Xml;

namespace MintPlayer.Assertions;

/// <summary>
/// Reaches the <c>XmlDocument</c> DOM through the LINQ-to-XML assertions.
/// </summary>
/// <remarks>
/// ⚠️ <b>One implementation, not two.</b> The DOM and LINQ-to-XML expose the same information behind
/// different types, and writing the assertions twice guarantees they drift — the same failure mode
/// the generator/reflection parity tests exist for elsewhere in this library. Converting once and
/// reusing costs a copy of the document, on an assertion the caller opted into, and cannot disagree
/// with itself.
/// <para>
/// The conversion goes through the node's <c>OuterXml</c> and <c>XDocument.Parse</c>, which is
/// lossless for everything these assertions look at.
/// </para>
/// </remarks>
public static partial class AssertionExtensions
{
    /// <summary>Asserts on an <see cref="XmlDocument"/> through the LINQ-to-XML surface.</summary>
    public static XDocumentAssertions Should(this XmlDocument? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : XDocument.Parse(subject.OuterXml), subjectExpression);

    /// <summary>Asserts on an <see cref="XmlElement"/> through the LINQ-to-XML surface.</summary>
    public static XElementAssertions Should(this XmlElement? subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        => new(subject is null ? null : XElement.Parse(subject.OuterXml), subjectExpression);
}
