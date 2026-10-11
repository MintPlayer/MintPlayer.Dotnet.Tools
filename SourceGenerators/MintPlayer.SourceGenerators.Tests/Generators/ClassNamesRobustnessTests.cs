using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>ClassNamesSourceGenerator on the names that broke its output (docs/PRD-TypeKindRobustness.md: W7, C8).</summary>
public class ClassNamesRobustnessTests
{
    private const string Generator = "ClassNamesSourceGenerator";

    private static GeneratorRun Run(string source) => GeneratorHarness.Run(Generator, [source]);

    /// <summary>W7: `const string event` does not parse.</summary>
    [Fact]
    public void AKeywordClassNameIsEscaped()
    {
        var run = Run("""
            namespace Demo;

            public class @event { }
            public class @class { }
            public class Plain { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("public const string @event = \"event\";");

        var classNames = run.Emit().GetGeneratedType("TestRoot.ClassNames");
        classNames.GetField("event")!.GetValue(null).Should().Be("event");
        classNames.GetField("Plain")!.GetValue(null).Should().Be("Plain");
    }

    /// <summary>W7: a member named after its enclosing type is CS0542.</summary>
    [Fact]
    public void AClassNamedClassNamesElsewhereGetsNoConstant()
    {
        var run = Run("""
            namespace Demo;

            public class ClassNames { }
            public class Alpha { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("public const string Alpha");
        run.AllSources.Should().Contain("\"ClassNames\"", "the list still names it");
    }

    /// <summary>W7: the user's own ClassNames / ClassNameList in the root namespace is CS0101 with the generated one.</summary>
    [Theory]
    [InlineData("ClassNames", "ClassNames.g.cs", "ClassNameList.g.cs")]
    [InlineData("ClassNameList", "ClassNameList.g.cs", "ClassNames.g.cs")]
    public void AUserTypeWithTheGeneratedNameInTheRootNamespaceWins(string name, string skipped, string kept)
    {
        var run = Run($$"""
            namespace TestRoot;

            public class {{name}} { }
            public class Alpha { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor(skipped).Should().BeNull();
        run.SourceFor(kept).Should().NotBeNull();
    }

    /// <summary>C8: the field provider crashed on a global-namespace class; it was never wired up and is gone.</summary>
    [Fact]
    public void AFieldInAGlobalNamespaceClassDoesNotCrashIt()
    {
        var run = Run("""
            using System;

            public class G { [Obsolete] private readonly int x; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("public const string G = \"G\";");
    }

    [Fact]
    public void TheGeneratedCodeIsWarningFreeUnderNullable()
    {
        var run = Run("""
            namespace Demo;

            public class @event { }
            public class Alpha { }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();
    }
}
