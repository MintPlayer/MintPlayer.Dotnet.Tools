using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// DescriptionSourceGenerator on every type kind and placement, and the shapes that made it emit a second type or a
/// duplicate attribute (docs/PRD-TypeKindRobustness.md: W1, W2, W3, K5, P21, P22).
/// </summary>
public class DescriptionRobustnessTests
{
    private const string Generator = "DescriptionSourceGenerator";
    private const string DescriptionAttribute = "System.ComponentModel.DescriptionAttribute";

    private static GeneratorRun Run(params string[] sources) => GeneratorHarness.Run(Generator, sources);

    /// <summary>The description the compiler sees on the type, or null.</summary>
    private static string? DescriptionOf(GeneratorRun run, string metadataName)
    {
        var type = run.Type(metadataName);
        type.Should().NotBeNull($"{metadataName} must exist");
        return type!.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == DescriptionAttribute)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .SingleOrDefault();
    }

    public static TheoryData<string, string> Kinds => new()
    {
        // keyword as the user writes it, keyword the generator re-declares it with
        { "class", "class" },
        { "record class", "record" },
        { "record", "record" },
        { "struct", "struct" },
        { "record struct", "record struct" },
    };

    public static TheoryData<string, string, string> KindsAndParents()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var kind in new[] { "class", "record class", "record", "struct", "record struct" })
        {
            foreach (var parent in new[] { "class", "record class", "record", "struct", "record struct", "interface" })
                data.Add(kind, parent, kind == "record class" ? "record" : kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ItDescribesATopLevelTypeOfEveryKind(string keyword, string emitted)
    {
        var run = Run($$"""
            namespace Demo;

            /// <summary>Doc text.</summary>
            public partial {{keyword}} W { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"partial {emitted} W");
        DescriptionOf(run, "Demo.W").Should().Be("Doc text.");
    }

    /// <summary>K5: a type nested in an interface used to land at namespace level as a new type.</summary>
    [Theory]
    [MemberData(nameof(KindsAndParents))]
    public void ItDescribesATypeNestedInEveryKind(string keyword, string parentKeyword, string emitted)
    {
        var run = Run($$"""
            namespace Demo;

            public partial {{parentKeyword}} Outer
            {
                /// <summary>Doc text.</summary>
                public partial {{keyword}} W { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"partial {emitted} W");
        DescriptionOf(run, "Demo.Outer+W").Should().Be("Doc text.");
        run.Type("Demo.W").Should().BeNull("the description must not create a second, top-level type");
    }

    [Fact]
    public void ATypeInsideANonPartialInterfaceIsLeftAlone()
    {
        var run = Run("""
            namespace Demo;

            public interface I
            {
                /// <summary>Nested.</summary>
                public partial class W { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Type("Demo.W").Should().BeNull();
        DescriptionOf(run, "Demo.I+W").Should().BeNull();
    }

    [Fact]
    public void ItDescribesATypeInTheGlobalNamespace()
    {
        var run = Run("""
            /// <summary>Global.</summary>
            public partial record struct W { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        DescriptionOf(run, "W").Should().Be("Global.");
    }

    /// <summary>W1: a summary on two parts gave two [Description] attributes, CS0579.</summary>
    [Fact]
    public void APartialTypeWithASummaryOnTwoPartsGetsOneDescription()
    {
        var run = Run(
            """
            namespace Demo;

            /// <summary>Part one.</summary>
            public partial class W { }
            """,
            """
            namespace Demo;

            /// <summary>Part two.</summary>
            public partial class W { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        DescriptionOf(run, "Demo.W").Should().Be("Part one.");
    }

    /// <summary>W2: the generic type got a non-generic `partial class W`, a separate type.</summary>
    [Fact]
    public void AGenericTypeIsDescribedItself()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>Generic.</summary>
            public partial class W<T> where T : class { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        DescriptionOf(run, "Demo.W`1").Should().Be("Generic.");
        run.Type("Demo.W").Should().BeNull("the description must not create a non-generic twin");
    }

    /// <summary>W3: a `file` type cannot be re-declared from another file; the partial created a separate type.</summary>
    [Fact]
    public void AFileLocalTypeIsLeftAlone()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>File local.</summary>
            file partial class W { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().NotContain("File local");
    }

    /// <summary>P21: an explicit [Description] plus a summary gave CS0579.</summary>
    [Fact]
    public void AnExplicitDescriptionWins()
    {
        var run = Run("""
            using System.ComponentModel;

            namespace Demo;

            /// <summary>From the summary.</summary>
            [Description("Explicit")]
            public partial class W { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        DescriptionOf(run, "Demo.W").Should().Be("Explicit");
        run.AllSources.Should().NotContain("From the summary");
    }

    /// <summary>P22: members are not candidates; only a documented partial type is.</summary>
    [Fact]
    public void DocumentedMembersProduceNothing()
    {
        var run = Run("""
            namespace Demo;

            public partial class W
            {
                /// <summary>A constructor.</summary>
                public W() { }

                /// <summary>A property.</summary>
                public int P { get; set; }

                /// <summary>An event.</summary>
                public event System.EventHandler? E { add { } remove { } }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void UnrelatedPartialsDoNotDisturbIt()
    {
        var run = Run(GeneratedCodeChecks.UnrelatedPartials, """
            namespace Demo;

            /// <summary>Doc text.</summary>
            public partial record W { }
            """);

        run.GeneratedErrors().Should().BeEmpty();
        run.AllSources.Should().NotContain("Rx");
        run.AllSources.Should().Contain("Doc text.");
    }

    [Fact]
    public void TheGeneratedCodeIsWarningFreeUnderNullable()
    {
        var run = Run("""
            namespace Demo;

            /// <summary>Doc "quoted" &amp; text.</summary>
            public partial record struct W<T> where T : notnull
            {
                /// <summary>Nested.</summary>
                public partial interface INested { }
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
        DescriptionOf(run, "Demo.W`1").Should().Be("Doc \"quoted\" & text.");
    }
}
