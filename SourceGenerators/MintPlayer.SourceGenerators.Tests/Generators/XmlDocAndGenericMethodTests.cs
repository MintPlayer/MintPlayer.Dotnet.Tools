using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// <c>DescriptionSourceGenerator</c>'s XML-documentation path — the half the existing tests never
/// reach, which only covered <c>[Description]</c> on enum members.
/// </summary>
public class XmlDocumentationTests
{
    private const string Generator = "DescriptionSourceGenerator";

    private static GeneratorRun Run(string source) => GeneratorHarness.Run(Generator, [source]);

    [Fact]
    public void ItPicksUpAClassSummary()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>A widget that does widget things.</summary>
            public partial class Widget { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("widget things");
    }

    [Theory]
    [InlineData("public partial class Widget { }")]
    [InlineData("public partial record Widget { }")]
    [InlineData("public partial struct Widget { }")]
    [InlineData("public partial interface IWidget { }")]
    public void ItPicksUpEveryDeclarationKind(string declaration)
    {
        var run = Run($$"""
            namespace Demo;

            /// <summary>Documented.</summary>
            {{declaration}}
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Documented");
    }

    /// <summary>
    /// XML markup has to be stripped to plain text before it is embedded in a string literal —
    /// leaving the tags in would put raw angle brackets and quotes into generated C#.
    /// </summary>
    [Fact]
    public void ItStripsTheMarkup()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>A <see cref="Widget"/> that does <c>things</c>.</summary>
            public partial class Widget { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("<see cref");
        run.AllSources.Should().NotContain("<c>");
    }

    /// <summary>
    /// A summary that starts the declaration's leading trivia — at column 0, directly below the
    /// line before. <c>SyntaxTriviaList.ToString()</c> leaves out the first trivia's own leading
    /// trivia, and for a doc comment that is the <c>///</c> exterior, so the summary was read
    /// without its <c>///</c> prefixes and dropped. Indentation or a blank line before the comment
    /// hid it, which is why it looked like a block-scoped namespace problem.
    /// </summary>
    [Theory]
    [InlineData("namespace Demo\n{\n/// <summary>A widget that does widget things.</summary>\npublic partial class Widget { }\n}")]
    [InlineData("namespace Demo;\n/// <summary>A widget that does widget things.</summary>\npublic partial class Widget { }")]
    [InlineData("/// <summary>A widget that does widget things.</summary>\npublic partial class Widget { }")]
    [InlineData("namespace Demo\n{\n    /// <summary>A widget that does widget things.</summary>\n    public partial class Widget { }\n}")]
    public void ItPicksUpASummaryThatIsTheFirstTrivia(string source)
    {
        var run = Run(source);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("widget things");
    }

    [Fact]
    public void ItIgnoresAnUndocumentedType()
    {
        var run = Run("""
            namespace Demo;

            public partial class Widget { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// A non-doc comment is leading trivia too, and must not be mistaken for documentation.
    /// </summary>
    [Fact]
    public void ItIgnoresAnOrdinaryComment()
    {
        var run = Run("""
            namespace Demo;

            // just a note to self
            public partial class Widget { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("note to self");
    }

    [Fact]
    public void ItHandlesAMultiLineSummary()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>
            /// First line of the summary.
            /// Second line of the summary.
            /// </summary>
            public partial class Widget { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("First line");
    }

    [Fact]
    public void ItEmitsCompilableCodeForADocumentedEnum()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>The kinds of widget.</summary>
            public enum WidgetKind { Small, Large }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}

/// <summary>
/// <c>GenericMethodSourceGenerator</c>, which fans a method out into arity-specific overloads.
/// </summary>
/// <remarks>
/// The fixtures use the shipped <c>MintPlayer.SourceGenerators.Attributes.GenericMethodAttribute</c>, never a copy of
/// their own: a copy once tested a positional <c>(int count)</c> constructor that the real attribute lacked (G0).
/// <see cref="GenericMethodRobustnessTests"/> holds the five-kind and #194 matrices.
/// </remarks>
public class GenericMethodGenerationTests
{
    private const string Generator = "GenericMethodSourceGenerator";

    private static GeneratorRun Run(string body) => GeneratorHarness.Run(Generator, [$$"""
        namespace Demo
        {
            using MintPlayer.SourceGenerators.Attributes;

        {{body}}
        }
        """]);

    [Fact]
    public void ItFansAMethodOutIntoOverloads()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(3)]
                    private void Add(object[] values) { }
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2>(");
        run.AllSources.Should().Contain("Add<T1, T2, T3>(");
    }

    [Fact]
    public void ItHonoursTheRequestedArity()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(4)]
                    private void Add(object[] values) { }
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);

        // A four-way fan-out has to reach a fourth type parameter, and stop there.
        run.AllSources.Should().Contain("T4");
        run.AllSources.Should().NotContain("T5");
    }

    /// <summary>
    /// The shape the producer is built for: each overload forwards its arguments as a collection
    /// expression (<c>this.Add([t1, t2])</c>) to the decorated private method, so that method takes
    /// a collection and the generator never implements it.
    /// </summary>
    [Theory]
    [InlineData("private void Add(object[] values) { }")]
    [InlineData("private static void Add(object[] values) { }")]
    public void ItEmitsCompilableOverloads(string method)
    {
        var run = Run($$"""
                public partial class Builder
                {
                    [GenericMethod(3)]
                    {{method}}
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2, T3>");
    }

    /// <summary>
    /// A decorated partial method, declared and implemented by the user. The overloads have other
    /// signatures, so they can never be its implementing half; copying <c>partial</c> onto them
    /// made each one an implementation with no definition (CS0759).
    /// </summary>
    [Fact]
    public void ItEmitsCompilableOverloadsForAPartialMethod()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(3)]
                    private partial void Add(object[] values);

                    private partial void Add(object[] values) { }
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2, T3>");
    }

    [Fact]
    public void ItIgnoresAMethodWithoutTheAttribute()
    {
        var run = Run("""
                public partial class Builder
                {
                    private void Add(object[] values) { }
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().BeEmpty();
    }

    /// <summary>
    /// The named form. This test was called <c>ItSkipsANonNumericCount</c>, but <c>Count = 3</c> is numeric and was
    /// never skipped: it generates overloads up to arity 3.
    /// </summary>
    [Fact]
    public void ItReadsANamedCount()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(Count = 3)]
                    private void Add(object[] values) { }
                }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Add<T1, T2, T3>(");
        run.AllSources.Should().NotContain("T4");
    }

    [Fact]
    public void OnAPlainCompilation_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run(Generator, ["namespace Demo; public class Plain { }"]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}
