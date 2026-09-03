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
/// <c>GenericMethodSourceGenerator</c>, which fans a method out into N arity-specific overloads.
/// </summary>
/// <remarks>
/// The fixtures declare <c>GenericMethodAttribute</c> themselves, in the namespace the generator
/// looks it up by metadata name (<c>MintPlayer.SourceGenerators.Generators</c>). The attribute
/// ships inside the generator assembly rather than the attributes package, so a test compilation
/// has no other way to reference it — and the generator matches on the metadata name, so a
/// locally-declared one reaches exactly the same path.
/// </remarks>
public class GenericMethodGenerationTests
{
    private const string Generator = "GenericMethodSourceGenerator";

    private const string AttributeDeclaration = """
        namespace MintPlayer.SourceGenerators.Generators
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class GenericMethodAttribute : System.Attribute
            {
                public GenericMethodAttribute(int count) { }
                public uint Count { get; set; } = 1;
                public System.Type? Transformer { get; set; }
            }
        }
        """;

    private static GeneratorRun Run(string body) => GeneratorHarness.Run(Generator, [$$"""
        {{AttributeDeclaration}}

        namespace Demo
        {
            using MintPlayer.SourceGenerators.Generators;

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
                    private partial void Add<T>(T value);
                }
            """);

        run.GeneratedSources.Should().NotBeEmpty();
        run.AllSources.Should().Contain("Add");
    }

    [Fact]
    public void ItHonoursTheRequestedArity()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(4)]
                    private partial void Add<T>(T value);
                }
            """);

        run.GeneratedSources.Should().NotBeEmpty();

        // A four-way fan-out has to reach a fourth type parameter.
        run.AllSources.Should().Contain("T4");
    }

    [Fact]
    public void ItIgnoresAMethodWithoutTheAttribute()
    {
        var run = Run("""
                public partial class Builder
                {
                    private partial void Add<T>(T value);
                }
            """);

        run.AllSources.Should().NotContain("T4");
    }

    /// <summary>
    /// The count is read off the attribute's first argument as an integer literal. A non-numeric
    /// argument has to be skipped rather than crashing the generator, which would take the whole
    /// consumer build down.
    /// </summary>
    [Fact]
    public void ItSkipsANonNumericCount()
    {
        var run = Run("""
                public partial class Builder
                {
                    [GenericMethod(Count = 3)]
                    private partial void Add<T>(T value);
                }
            """);

        run.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void OnAPlainCompilation_ItDoesNotFail()
    {
        var run = GeneratorHarness.Run(Generator, ["namespace Demo; public class Plain { }"]);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }
}
