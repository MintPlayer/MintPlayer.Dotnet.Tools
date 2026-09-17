using System.Xml;
using System.Xml.Linq;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Xml;

/// <summary>Assertions on a LINQ-to-XML <see cref="XDocument"/>.</summary>
public class XDocumentAssertions
{
    public XDocumentAssertions(XDocument? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "document" : subjectExpression!;
    }

    /// <summary>The document under test.</summary>
    public XDocument? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the document has a root element, and exposes it for further assertions.</summary>
    public AndWhichConstraint<XDocumentAssertions, XElement?> HaveRoot(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null)
        {
            FailNull("to have a root element", because, becauseArgs);
            return new(this, null);
        }

        Assert().ForCondition(Subject.Root is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a root element{reason}, but it has none.");
        return new(this, Subject.Root);
    }

    /// <summary>Asserts the document's root element is named <paramref name="expected"/>, namespace included.</summary>
    public AndWhichConstraint<XDocumentAssertions, XElement?> HaveRoot(XName expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Subject is null)
        {
            FailNull($"to have root element '{expected}'", because, becauseArgs);
            return new(this, null);
        }

        var root = Subject.Root;
        if (root is null)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to have root element {0}{reason}, but it has no root at all.", expected.ToString());
            return new(this, null);
        }

        Assert().ForCondition(root.Name == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have root element {0}{reason}, but found {1}.", expected.ToString(), root.Name.ToString());
        return new(this, root);
    }

    /// <summary>
    /// Asserts the document is structurally equal to <paramref name="expected"/> by
    /// <see cref="XNode.DeepEquals"/>. See the remark on <c>XElementAssertions.BeEquivalentTo</c>.
    /// </summary>
    public AndConstraint<XDocumentAssertions> BeEquivalentTo(XDocument expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Subject is null) return FailNull("to be equivalent to the given document", because, becauseArgs);

        Assert().ForCondition(XNode.DeepEquals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be equivalent to {0}{reason}, but found {1}.", expected.ToString(), Subject.ToString());
        return new(this);
    }

    private AndConstraint<XDocumentAssertions> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but found <null>.");
        return new(this);
    }
}
