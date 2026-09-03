namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// MPA0100 — a file still importing FluentAssertions, offered a migration.
/// </summary>
/// <remarks>
/// Info severity and purely syntactic: it matches on the using directive without needing
/// FluentAssertions to be resolvable, which is what lets it fire in a project that has not yet
/// added the package back.
/// </remarks>
public class FluentAssertionsMigrationAnalyzerTests
{
    private const string Analyzer = "FluentAssertionsMigrationAnalyzer";

    [Fact]
    public async Task ItFlagsTheRootNamespace()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using FluentAssertions;

            public class Test { }
            """);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("MPA0100");
        diagnostics[0].Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
    }

    [Fact]
    public async Task ItFlagsASubNamespace()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using FluentAssertions.Execution;

            public class Test { }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MPA0100");
    }

    /// <summary>
    /// A namespace that merely starts with the same characters is a different library. The check
    /// requires the dot, so this must stay quiet.
    /// </summary>
    [Fact]
    public async Task ItIgnoresANamespaceThatMerelySharesAPrefix()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using FluentAssertionsExtras;

            public class Test { }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// An alias or a static import is not a plain namespace import, and rewriting one is not the
    /// mechanical substitution the code fix performs — so the analyzer declines to offer it.
    /// </summary>
    [Theory]
    [InlineData("using FA = FluentAssertions;")]
    [InlineData("using static FluentAssertions.AssertionExtensions;")]
    public async Task ItIgnoresAliasedAndStaticImports(string directive)
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            {{directive}}

            public class Test { }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAFileWithNoFluentAssertionsImport()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System;
            using MintPlayer.Assertions;

            public class Test { }
            """);

        diagnostics.Should().BeEmpty();
    }
}
