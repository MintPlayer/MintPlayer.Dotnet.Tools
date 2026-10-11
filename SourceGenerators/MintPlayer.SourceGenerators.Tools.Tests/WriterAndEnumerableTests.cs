using System.CodeDom.Compiler;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Tools.Tests;

public class EnumerableExtensionsTests
{
    [Fact]
    public void NotNull_DropsNulls()
        => new[] { "a", null, "b", null }.NotNull().Should().Equal(["a", "b"]);

    [Fact]
    public void NotNull_OnAnAllNullSequence_IsEmpty()
        => new string?[] { null, null }.NotNull().Should().BeEmpty();

    [Fact]
    public void NotNull_OnEmpty_IsEmpty()
        => Array.Empty<string?>().NotNull().Should().BeEmpty();

    [Fact]
    public void NotNull_KeepsOrder()
        => new[] { "c", null, "a", "b" }.NotNull().Should().Equal(["c", "a", "b"]);

    [Fact]
    public void DistinctBy_KeepsTheFirstOfEachKey()
    {
        var items = new[] { (Key: 1, Value: "first"), (Key: 2, Value: "x"), (Key: 1, Value: "second") };

        items.DistinctBy(i => i.Key).Select(i => i.Value).Should().Equal(["first", "x"]);
    }

    [Fact]
    public void DistinctBy_OnEmpty_IsEmpty()
        => Array.Empty<string>().DistinctBy(s => s).Should().BeEmpty();

    [Fact]
    public void DistinctBy_WithAConstantKey_KeepsOneItem()
        => new[] { "a", "b", "c" }.DistinctBy(_ => 0).Should().Equal(["a"]);

    [Fact]
    public void DistinctBy_HandlesNullKeys()
    {
        var items = new[] { "a", "bb", "c" };

        items.DistinctBy(s => (string?)null).Should().Equal(["a"]);
    }

    [Fact]
    public void DistinctBy_IsLazy()
    {
        var started = false;

        IEnumerable<int> Tracked()
        {
            started = true;
            yield return 1;
        }

        var query = Tracked().DistinctBy(i => i);
        started.Should().BeFalse();

        query.ToList();
        started.Should().BeTrue();
    }
}

public class IndentedTextWriterExtensionsTests
{
    private static (IndentedTextWriter Writer, StringWriter Backing) Create()
    {
        var backing = new StringWriter { NewLine = "\n" };
        return (new IndentedTextWriter(backing, "    "), backing);
    }

    [Fact]
    public void OpenBlock_WritesTheHeaderBracesAndIndents()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock("public class Foo"))
        {
            writer.WriteLine("var x = 1;");
        }

        backing.ToString().Should().Be("public class Foo\n{\n    var x = 1;\n}\n");
    }

    [Fact]
    public void OpenBlock_Nests()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock("class Outer"))
        using (writer.OpenBlock("void Inner()"))
        {
            writer.WriteLine("body;");
        }

        backing.ToString().Should().Be(
            "class Outer\n{\n    void Inner()\n    {\n        body;\n    }\n}\n");
    }

    [Fact]
    public void OpenBlock_WithoutBraces_StillIndents()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock("#region Stuff", writeBraces: false))
        {
            writer.WriteLine("line;");
        }

        backing.ToString().Should().Be("#region Stuff\n    line;\n");
    }

    [Fact]
    public void OpenBlock_WithAnEmptyHeader_OmitsTheHeaderLine()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock(string.Empty))
        {
            writer.WriteLine("line;");
        }

        backing.ToString().Should().Be("{\n    line;\n}\n");
    }

    [Fact]
    public void OpenBlock_RestoresTheIndentAfterDisposal()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock("class Foo")) { }
        writer.WriteLine("after");

        backing.ToString().Should().EndWith("after\n");
        writer.Indent.Should().Be(0);
    }

    [Fact]
    public void IndentSingleLine_IndentsOnlyThatLine()
    {
        var (writer, backing) = Create();

        writer.WriteLine("first");
        writer.IndentSingleLine(": base()");
        writer.WriteLine("last");

        backing.ToString().Should().Be("first\n    : base()\nlast\n");
        writer.Indent.Should().Be(0);
    }

    [Fact]
    public void IndentSingleLine_StacksOnTopOfAnOpenBlock()
    {
        var (writer, backing) = Create();

        using (writer.OpenBlock("class Foo"))
        {
            writer.IndentSingleLine("nested");
        }

        backing.ToString().Should().Contain("        nested\n");
    }
}

public class TypeExtensionsTests
{
    private class Base { }
    private class Derived : Base { }
    private class Deeper : Derived { }
    private class OpenGeneric<T> { }
    private class ClosedFromGeneric : OpenGeneric<int> { }

    [Fact]
    public void IsDerivedFrom_ForADirectBase_IsTrue()
        => typeof(Derived).IsDerivedFrom(typeof(Base)).Should().BeTrue();

    [Fact]
    public void IsDerivedFrom_ForAnIndirectBase_IsTrue()
        => typeof(Deeper).IsDerivedFrom(typeof(Base)).Should().BeTrue();

    [Fact]
    public void IsDerivedFrom_ForTheSameType_IsTrue()
        => typeof(Derived).IsDerivedFrom(typeof(Derived)).Should().BeTrue();

    [Fact]
    public void IsDerivedFrom_ForAnUnrelatedType_IsFalse()
        => typeof(Derived).IsDerivedFrom(typeof(string)).Should().BeFalse();

    [Fact]
    public void IsDerivedFrom_ComparesTheOpenGenericDefinition()
    {
        // The walk calls GetGenericTypeDefinition on each closed generic, so an open
        // generic base matches.
        typeof(ClosedFromGeneric).IsDerivedFrom(typeof(OpenGeneric<>)).Should().BeTrue();
    }

    [Fact]
    public void IsDerivedFrom_StopsAtObject()
        => typeof(Derived).IsDerivedFrom(typeof(object)).Should().BeFalse();
}
