using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MintPlayer.Assertions.Tests;

/// <summary>Streams, LINQ-to-XML, the XmlDocument bridge, ValueTask and TaskCompletionSource.</summary>
public class StreamXmlAndTaskTests
{
    // -------------------------------------------------------------------------------------------
    // Streams
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void StreamCapabilities()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        stream.Should().BeReadable();
        stream.Should().BeWritable();
        stream.Should().BeSeekable();

        Assert.IsType<AssertionFailedException>(Record.Exception(() => stream.Should().NotBeReadable()));
    }

    [Fact]
    public void ReadOnlyStreamIsNotWritable()
    {
        using var stream = new MemoryStream([1, 2, 3], writable: false);

        stream.Should().BeReadable();
        stream.Should().NotBeWritable();
    }

    [Fact]
    public void LengthPositionAndEnds()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        stream.Should().HaveLength(3);
        stream.Should().HavePosition(0);
        stream.Should().BeAtStart();
        stream.Should().NotBeEmpty();

        stream.Position = 3;
        stream.Should().BeAtEnd();
    }

    [Fact]
    public void AnEmptyStream()
    {
        using var stream = new MemoryStream();

        stream.Should().BeEmpty();

        Assert.IsType<AssertionFailedException>(Record.Exception(() => stream.Should().NotBeEmpty()));
    }

    /// <summary>
    /// Reading Length on a disposed stream throws. That is a failed assertion about the stream, not
    /// an error in the test, so it is reported as one — and the message names the exception.
    /// </summary>
    [Fact]
    public void AThrowingPropertyBecomesAFailureRatherThanAnError()
    {
        var stream = new MemoryStream();
        stream.Dispose();

        var ex = Record.Exception(() => stream.Should().HaveLength(0));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("ObjectDisposedException", ex.Message);
    }

    [Fact]
    public void ANullStreamFailsRatherThanThrows()
    {
        Stream? stream = null;

        var ex = Record.Exception(() => stream.Should().BeReadable());

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("<null>", ex.Message);
    }

    // -------------------------------------------------------------------------------------------
    // LINQ-to-XML
    // -------------------------------------------------------------------------------------------

    private const string Sample = """
        <order id="7">
          <line sku="a" />
          <line sku="b" />
          <note>hello</note>
        </order>
        """;

    [Fact]
    public void DocumentRootAndName()
    {
        var doc = XDocument.Parse(Sample);

        doc.Should().HaveRoot("order");
        doc.Should().HaveRoot().Which!.Should().HaveAttribute("id", "7");
    }

    [Fact]
    public void ElementNameValueAndAttributes()
    {
        var root = XDocument.Parse(Sample).Root!;

        root.Should().HaveName("order");
        root.Should().HaveAttribute("id").Which.Should().Be("7");
        root.Should().NotHaveAttribute("missing");
        root.Should().HaveElement("note").Which!.Should().HaveValue("hello");
        root.Should().NotHaveElement("absent");
    }

    [Fact]
    public void ChildOccurrences()
    {
        var root = XDocument.Parse(Sample).Root!;

        root.Should().HaveElement("line", Exactly.Twice());
        root.Should().HaveElement("note", Exactly.Once());
    }

    /// <summary>
    /// A name comparison includes the namespace. XML whose namespace is wrong looks identical in a
    /// diff, so an assertion that ignored it would pass on exactly the documents worth catching.
    /// </summary>
    [Fact]
    public void NamesAreComparedWithTheirNamespace()
    {
        var ns = XNamespace.Get("urn:test");
        var element = new XElement(ns + "item");

        element.Should().HaveName(ns + "item");

        Assert.IsType<AssertionFailedException>(Record.Exception(() => element.Should().HaveName("item")));
    }

    [Fact]
    public void AnElementSubjectDoesNotBindToTheCollectionOverload()
    {
        var element = new XElement("x");

        // If XElement bound to Should<T>(this IEnumerable<T>) this would not compile.
        element.Should().BeEmpty();
    }

    [Fact]
    public void DeepEqualsBackedEquivalence()
    {
        var a = XElement.Parse("<a x='1'><b/></a>");
        var b = XElement.Parse("<a x='1'><b/></a>");
        var c = XElement.Parse("<a x='2'><b/></a>");

        a.Should().BeEquivalentTo(b);
        Assert.IsType<AssertionFailedException>(Record.Exception(() => a.Should().BeEquivalentTo(c)));
    }

    /// <summary>The DOM reaches the same implementation rather than a second one that can drift.</summary>
    [Fact]
    public void TheXmlDocumentBridgeUsesTheSameAssertions()
    {
        var dom = new XmlDocument();
        dom.LoadXml(Sample);

        dom.Should().HaveRoot("order");
        dom.DocumentElement.Should().HaveElement("line", Exactly.Twice());
    }

    // -------------------------------------------------------------------------------------------
    // ValueTask and TaskCompletionSource
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AValueTaskReturningDelegateIsAsserted()
    {
        static ValueTask Throwing() => ValueTask.FromException(new InvalidOperationException("boom"));

        await ((Func<ValueTask>)Throwing).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AGenericValueTaskReturningDelegateIsAsserted()
    {
        static ValueTask<int> Answer() => ValueTask.FromResult(42);

        await ((Func<ValueTask<int>>)Answer).Should().NotThrowAsync();
    }

    /// <summary>
    /// The subject is the delegate, not the ValueTask: a ValueTask may be awaited only once, so an
    /// assertion surface taking the value would hand the caller undefined behaviour.
    /// </summary>
    [Fact]
    public async Task EachAssertionProducesItsOwnValueTask()
    {
        var calls = 0;
        ValueTask Count() { calls++; return ValueTask.CompletedTask; }

        await ((Func<ValueTask>)Count).Should().NotThrowAsync();
        await ((Func<ValueTask>)Count).Should().NotThrowAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ATaskCompletionSourceIsAssertedThroughItsTask()
    {
        var source = new TaskCompletionSource<int>();
        source.SetException(new InvalidOperationException("nope"));

        await source.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ExecutionTimeOfMeasuresAnAction()
    {
        AssertionExtensions.ExecutionTimeOf(() => Thread.Sleep(1)).BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ExecutionTimeOfRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AssertionExtensions.ExecutionTimeOf(null!));
    }

    private static MemoryStream TextStream(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void AForwardOnlyStreamReportsItsCapabilities()
    {
        using var inner = TextStream("abc");
        using var stream = new BufferedStream(inner);

        stream.Should().BeReadable();
    }
}
