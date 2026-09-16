using System.Xml.Linq;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Xml;

/// <summary>
/// Assertions on an <see cref="XElement"/>: its name, its value, its attributes and its children.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-free and purely additive — <c>System.Xml.Linq</c> is part of the shared framework, so
/// this costs no dependency and touches nothing else in the library. That was the deciding argument
/// for answering PRD §13's third open question with "in scope" rather than deferring it: the only
/// cost was surface area to maintain, and the alternative was leaving XML tests to write
/// <c>element.Attribute("id")?.Value.Should().Be("7")</c>, which reports "expected \"7\" but found
/// &lt;null&gt;" for a missing attribute and a wrong value alike.
/// </para>
/// <para>
/// Names are compared as <see cref="XName"/>, so a namespace is respected when one is given and
/// ignored when the caller passes a bare string — which is what a string implicitly converts to.
/// </para>
/// </remarks>
public class XElementAssertions : ReferenceTypeAssertions<XElement, XElementAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public XElementAssertions(XElement? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the element is named <paramref name="expected"/>.</summary>
    public AndConstraint<XElementAssertions> HaveName(XName expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.Name == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be named {0}{reason}, but found {1}.", expected.ToString(), Subject?.Name.ToString());
        return new(this);
    }

    /// <summary>Asserts the element's text content is <paramref name="expected"/>.</summary>
    public AndConstraint<XElementAssertions> HaveValue(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && string.Equals(Subject.Value, expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have value {0}{reason}, but found {1}.", expected, Subject?.Value);
        return new(this);
    }

    /// <summary>Asserts the element carries an attribute named <paramref name="name"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<XElementAssertions, XAttribute> HaveAttribute(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        var attribute = Subject?.Attribute(name);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an attribute {0}{reason}, but it does not.", name.ToString());
        return new(this, attribute!);
    }

    /// <summary>
    /// Asserts the element carries attribute <paramref name="name"/> with value
    /// <paramref name="expectedValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinguishes "no such attribute" from "wrong value", which is exactly what the hand-written
    /// <c>element.Attribute(name)?.Value.Should().Be(...)</c> cannot do — there, a missing attribute
    /// and a null value produce the same message.
    /// </para>
    /// <para>
    /// Not an overload of <see cref="HaveAttribute(XName, string, object[])"/>, deliberately.
    /// <c>HaveAttribute(name, value)</c> and <c>HaveAttribute(name, because)</c> have identical
    /// parameter types, so the compiler picks one and the caller gets the other — silently. The same
    /// trap took a rename to <c>ContainAll</c> to fix on the collection assertions; a distinct name
    /// is the only spelling that cannot be misread.
    /// </para>
    /// </remarks>
    public AndConstraint<XElementAssertions> HaveAttributeWithValue(XName name, string expectedValue, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(expectedValue);

        var attribute = Subject?.Attribute(name);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an attribute {0} with value {1}{reason}, but the attribute is not there.", name.ToString(), expectedValue);
        if (attribute is null) return new(this);

        Assert().ForCondition(string.Equals(attribute.Value, expectedValue, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an attribute {0} with value {1}{reason}, but found {2}.", name.ToString(), expectedValue, attribute.Value);
        return new(this);
    }

    /// <summary>Asserts the element carries no attribute named <paramref name="name"/>.</summary>
    public AndConstraint<XElementAssertions> NotHaveAttribute(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        Assert().ForCondition(Subject is null || Subject.Attribute(name) is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have an attribute {0}{reason}.", name.ToString());
        return new(this);
    }

    /// <summary>Asserts the element has a direct child named <paramref name="name"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<XElementAssertions, XElement> HaveElement(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        var child = Subject?.Element(name);
        Assert().ForCondition(child is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a child element {0}{reason}, but it does not.", name.ToString());
        return new(this, child!);
    }

    /// <summary>Asserts the element has exactly <paramref name="expected"/> direct children named <paramref name="name"/>.</summary>
    public AndConstraint<XElementAssertions> HaveElementCount(XName name, int expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);

        var count = 0;
        if (Subject is not null)
        {
            foreach (var _ in Subject.Elements(name)) count++;
        }

        Assert().ForCondition(Subject is not null && count == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} child element(s) named {1}{reason}, but found {2}.", expected, name.ToString(), count);
        return new(this);
    }

    /// <summary>Asserts the element has no child elements.</summary>
    public AndConstraint<XElementAssertions> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null && !Subject.HasElements).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have no child elements{reason}, but it has some.");
        return new(this);
    }

    /// <summary>
    /// Asserts the element is structurally equivalent to <paramref name="expected"/>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="XNode.DeepEquals"/>, which is ordered and whitespace-sensitive — the same
    /// comparison <c>XNode</c> itself defines, rather than a second opinion about what two XML
    /// documents being "the same" means. When that is not the question, assert on the parts.
    /// </remarks>
    public AndConstraint<XElementAssertions> BeEquivalentTo(XElement expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && XNode.DeepEquals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be equivalent to {0}{reason}, but found {1}.", expected.ToString(), Subject?.ToString());
        return new(this);
    }
}

/// <summary>Assertions on an <see cref="XDocument"/>: its root, and what hangs off it.</summary>
public class XDocumentAssertions : ReferenceTypeAssertions<XDocument, XDocumentAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public XDocumentAssertions(XDocument? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the document has a root element named <paramref name="expected"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<XDocumentAssertions, XElement> HaveRoot(XName expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var root = Subject?.Root;
        var matches = root is not null && root.Name == expected;

        Assert().ForCondition(matches).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have root element {0}{reason}, but found {1}.", expected.ToString(), root?.Name.ToString());
        return new(this, matches ? root! : null!);
    }

    /// <summary>Asserts the document's root has a direct child named <paramref name="name"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<XDocumentAssertions, XElement> HaveElement(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        var child = Subject?.Root?.Element(name);

        Assert().ForCondition(child is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected the root of {subject} to have a child element {0}{reason}, but it does not.", name.ToString());
        return new(this, child!);
    }

    /// <summary>Asserts the document is structurally equivalent to <paramref name="expected"/>.</summary>
    /// <remarks>See <see cref="XElementAssertions.BeEquivalentTo"/> for what "equivalent" means here.</remarks>
    public AndConstraint<XDocumentAssertions> BeEquivalentTo(XDocument expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && XNode.DeepEquals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be equivalent to {0}{reason}, but found {1}.", expected.ToString(), Subject?.ToString());
        return new(this);
    }
}

/// <summary>Assertions on an <see cref="XAttribute"/>.</summary>
public class XAttributeAssertions : ReferenceTypeAssertions<XAttribute, XAttributeAssertions>
{
    /// <summary>Wraps <paramref name="subject"/>, remembering the caller's expression text for messages.</summary>
    public XAttributeAssertions(XAttribute? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts the attribute's value is <paramref name="expected"/>.</summary>
    public AndConstraint<XAttributeAssertions> HaveValue(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && string.Equals(Subject.Value, expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have value {0}{reason}, but found {1}.", expected, Subject?.Value);
        return new(this);
    }

    /// <summary>Asserts the attribute is named <paramref name="expected"/>.</summary>
    public AndConstraint<XAttributeAssertions> HaveName(XName expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.Name == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be named {0}{reason}, but found {1}.", expected.ToString(), Subject?.Name.ToString());
        return new(this);
    }
}
