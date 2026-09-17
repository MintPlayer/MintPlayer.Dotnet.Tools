using System.Xml.Linq;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Xml;

/// <summary>
/// Assertions on a LINQ-to-XML <see cref="XElement"/>: name, value, attributes and child elements.
/// </summary>
/// <remarks>
/// <para>
/// LINQ-to-XML only. The <c>XmlDocument</c> DOM is reachable through
/// <see cref="XmlNodeAssertionExtensions"/>, which converts once and then uses this surface — one
/// implementation rather than two that drift.
/// </para>
/// <para>
/// ⚠️ <b>Names are compared including their namespace.</b> <c>HaveName("item")</c> does NOT match
/// <c>&lt;ns:item&gt;</c>, and that is the safer default by a distance: XML where the namespace is
/// wrong looks identical in a diff, and an assertion that ignored it would pass on precisely the
/// documents worth catching. Pass an <see cref="XName"/> when a namespace is intended.
/// </para>
/// </remarks>
public class XElementAssertions
{
    public XElementAssertions(XElement? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "element" : subjectExpression!;
    }

    /// <summary>The element under test.</summary>
    public XElement? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the element's name is <paramref name="expected"/>, namespace included.</summary>
    public AndConstraint<XElementAssertions> HaveName(XName expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Subject is null) return FailNull($"to have name '{expected}'", because, becauseArgs);

        Assert().ForCondition(Subject.Name == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have name {0}{reason}, but found {1}.", expected.ToString(), Subject.Name.ToString());
        return new(this);
    }

    /// <summary>Asserts the element's text value is <paramref name="expected"/>.</summary>
    /// <remarks>
    /// <see cref="XElement.Value"/> concatenates the text of every descendant, so an element with
    /// children has the concatenation as its value. That is LINQ-to-XML's own definition and this
    /// does not second-guess it; use <see cref="HaveElement"/> to reach into children.
    /// </remarks>
    public AndConstraint<XElementAssertions> HaveValue(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Subject is null) return FailNull($"to have value {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        Assert().ForCondition(Subject.Value == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have value {0}{reason}, but found {1}.", expected, Subject.Value);
        return new(this);
    }

    /// <summary>Asserts the element carries an attribute named <paramref name="name"/>, and exposes it.</summary>
    public AndWhichConstraint<XElementAssertions, string> HaveAttribute(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Subject is null)
        {
            FailNull($"to have attribute '{name}'", because, becauseArgs);
            return new(this, string.Empty);
        }

        var attribute = Subject.Attribute(name);
        Assert().ForCondition(attribute is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have attribute {0}{reason}, but it does not.", name.ToString());
        return new(this, attribute?.Value ?? string.Empty);
    }

    /// <summary>Asserts the element carries <paramref name="name"/> with value <paramref name="expectedValue"/>.</summary>
    public AndConstraint<XElementAssertions> HaveAttribute(XName name, string expectedValue, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(expectedValue);
        if (Subject is null) return FailNull($"to have attribute '{name}'", because, becauseArgs);

        var attribute = Subject.Attribute(name);
        if (attribute is null)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to have attribute {0} with value {1}{reason}, but the attribute is missing.", name.ToString(), expectedValue);
            return new(this);
        }

        Assert().ForCondition(attribute.Value == expectedValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have attribute {0} with value {1}{reason}, but found {2}.", name.ToString(), expectedValue, attribute.Value);
        return new(this);
    }

    /// <summary>Asserts the element does not carry an attribute named <paramref name="name"/>.</summary>
    public AndConstraint<XElementAssertions> NotHaveAttribute(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Subject is null) return FailNull($"not to have attribute '{name}'", because, becauseArgs);

        Assert().ForCondition(Subject.Attribute(name) is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have attribute {0}{reason}, but it does.", name.ToString());
        return new(this);
    }

    /// <summary>Asserts the element has a direct child named <paramref name="name"/>, and exposes it.</summary>
    /// <remarks>Direct children only — a descendant further down does not satisfy this.</remarks>
    public AndWhichConstraint<XElementAssertions, XElement?> HaveElement(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Subject is null)
        {
            FailNull($"to have child element '{name}'", because, becauseArgs);
            return new(this, null);
        }

        var child = Subject.Element(name);
        Assert().ForCondition(child is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have child element {0}{reason}, but it does not.", name.ToString());
        return new(this, child);
    }

    /// <summary>Asserts the element has no direct child named <paramref name="name"/>.</summary>
    public AndConstraint<XElementAssertions> NotHaveElement(XName name, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Subject is null) return FailNull($"not to have child element '{name}'", because, becauseArgs);

        Assert().ForCondition(Subject.Element(name) is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have child element {0}{reason}, but it does.", name.ToString());
        return new(this);
    }

    /// <summary>Asserts the number of direct children named <paramref name="name"/> satisfies <paramref name="occurrence"/>.</summary>
    public AndConstraint<XElementAssertions> HaveElement(XName name, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Subject is null) return FailNull($"to have child element '{name}' {occurrence}", because, becauseArgs);

        var count = 0;
        foreach (var _ in Subject.Elements(name)) count++;

        if (!occurrence.IsSatisfiedBy(count))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to have child element {{0}} {occurrence}{{reason}}, but found {{1}}.", name.ToString(), count);
        }
        return new(this);
    }

    /// <summary>Asserts the element has no child elements.</summary>
    public AndConstraint<XElementAssertions> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to have no child elements", because, becauseArgs);

        var count = 0;
        foreach (var _ in Subject.Elements()) count++;

        Assert().ForCondition(count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have no child elements{reason}, but found {0}.", count);
        return new(this);
    }

    /// <summary>
    /// Asserts the element is structurally equal to <paramref name="expected"/> by
    /// <see cref="XNode.DeepEquals"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DeepEquals</c> is strict about things a reader may not expect: attribute ORDER does not
    /// matter, but whitespace text nodes DO, so two documents that render identically can differ
    /// here purely on indentation. That is LINQ-to-XML's semantics, and re-defining it would mean
    /// this assertion disagreeing with the framework method it is named after. Parse both sides with
    /// the same options, or compare the parts you care about with the assertions above.
    /// </remarks>
    public AndConstraint<XElementAssertions> BeEquivalentTo(XElement expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Subject is null) return FailNull("to be equivalent to the given element", because, becauseArgs);

        Assert().ForCondition(XNode.DeepEquals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be equivalent to {0}{reason}, but found {1}.", expected.ToString(), Subject.ToString());
        return new(this);
    }

    private AndConstraint<XElementAssertions> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but found <null>.");
        return new(this);
    }
}
