using System.Xml.Linq;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The XML family (PRD §7, and the answer to §13's third open question: in scope).
/// </summary>
public class Phase2XmlTests
{
    private static XDocument Sample() => XDocument.Parse(
        """
        <order id="7" status="open">
          <line sku="A1">2</line>
          <line sku="B2">5</line>
          <customer><name>Alice</name></customer>
        </order>
        """);

    [Fact]
    public void DocumentRootAndElements()
    {
        var document = Sample();

        document.Should().HaveRoot("order").Which.Should().HaveAttributeWithValue("id", "7");
        document.Should().HaveElement("customer").Which.Should().HaveElement("name").Which.Should().HaveValue("Alice");

        Assert.IsType<AssertionFailedException>(Record.Exception(() => document.Should().HaveRoot("invoice")));
    }

    /// <summary>
    /// The reason <c>HaveAttributeWithValue</c> exists at all, and why it is not an overload of
    /// <c>HaveAttribute</c> — <c>HaveAttribute(name, value)</c> and <c>HaveAttribute(name, because)</c>
    /// have identical parameter types, and the wrong one won. Hand-written
    /// <c>element.Attribute("id")?.Value.Should().Be("9")</c> reports "expected \"9\" but found
    /// &lt;null&gt;" for a missing attribute and for a wrong value alike.
    /// </summary>
    [Fact]
    public void AMissingAttributeAndAWrongValueReadDifferently()
    {
        var root = Sample().Root!;

        var missing = Record.Exception(() => root.Should().HaveAttributeWithValue("nope", "x"));
        var wrong = Record.Exception(() => root.Should().HaveAttributeWithValue("id", "9"));

        Assert.Contains("the attribute is not there", missing!.Message);
        Assert.Contains("but found \"7\"", wrong!.Message);
    }

    [Fact]
    public void ElementNameValueAndCounts()
    {
        var root = Sample().Root!;

        root.Should().HaveName("order").And.HaveElementCount("line", 2);
        root.Should().NotHaveAttribute("deleted");
        root.Element("customer")!.Element("name")!.Should().HaveValue("Alice").And.BeEmpty();

        Assert.IsType<AssertionFailedException>(Record.Exception(() => root.Should().HaveElementCount("line", 3)));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => root.Should().BeEmpty()));
    }

    [Fact]
    public void AttributeAssertions()
    {
        var attribute = Sample().Root!.Attribute("status")!;

        attribute.Should().HaveName("status").And.HaveValue("open");
        Assert.IsType<AssertionFailedException>(Record.Exception(() => attribute.Should().HaveValue("closed")));
    }

    [Fact]
    public void BeEquivalentTo_UsesDeepEquals()
    {
        Sample().Should().BeEquivalentTo(Sample());
        Sample().Root!.Should().BeEquivalentTo(Sample().Root!);

        var changed = Sample();
        changed.Root!.SetAttributeValue("id", "8");

        Assert.IsType<AssertionFailedException>(Record.Exception(() => Sample().Should().BeEquivalentTo(changed)));
    }

    /// <summary>A bare string converts to an <see cref="XName"/> without a namespace, so this is explicit.</summary>
    [Fact]
    public void NamespacesAreRespected()
    {
        XNamespace ns = "urn:example";
        var element = new XElement(ns + "thing", new XAttribute("id", "1"));

        element.Should().HaveName(ns + "thing");
        Assert.IsType<AssertionFailedException>(Record.Exception(() => element.Should().HaveName("thing")));
    }

    [Fact]
    public void ANullSubjectFails()
    {
        XElement? element = null;

        Assert.IsType<AssertionFailedException>(Record.Exception(() => element.Should().HaveName("anything")));
        element.Should().BeNull();
    }
}
