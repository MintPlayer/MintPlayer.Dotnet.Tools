namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// The four code-fix providers that ship inside MintPlayer.Assertions.
/// </summary>
/// <remarks>
/// These are the highest-consequence code in the package: a generator that emits nonsense produces
/// a compile error the consumer can see, but a code fix rewrites source the consumer already wrote.
/// Every test here therefore asserts on the resulting text, not merely that a fix was offered.
/// </remarks>
public class CodeFixTests
{
    #region MPA0001 — await the assertion

    [Fact]
    public async Task ItAwaitsAnUnawaitedAssertion()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "UnawaitedAssertionAnalyzer", "UnawaitedAssertionCodeFixProvider", """
            using System;
            using System.Threading.Tasks;
            using MintPlayer.Assertions;

            public class Test
            {
                public async Task Run()
                {
                    Func<Task> act = () => Task.CompletedTask;
                    act.Should().ThrowAsync<InvalidOperationException>();
                }
            }
            """);

        result.Applied.Should().BeTrue();
        result.ActionTitle.Should().Be("Await the assertion");
        result.FixedSource.Should().Contain("await act.Should().ThrowAsync<InvalidOperationException>();");
    }

    /// <summary>
    /// Indentation is not cosmetic here: the fix moves the statement's leading trivia onto the
    /// inserted <c>await</c> token. Getting that wrong shifts the line to column zero, which is
    /// the sort of thing a "does it contain await" assertion would happily miss.
    /// </summary>
    [Fact]
    public async Task ItPreservesIndentationWhenAwaiting()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "UnawaitedAssertionAnalyzer", "UnawaitedAssertionCodeFixProvider", """
            using System;
            using System.Threading.Tasks;
            using MintPlayer.Assertions;

            public class Test
            {
                public async Task Run()
                {
                    Func<Task> act = () => Task.CompletedTask;
                    act.Should().NotThrowAsync();
                }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("        await act.Should().NotThrowAsync();");
    }

    #endregion

    #region MPA0003 — dispose the AssertionScope

    [Fact]
    public async Task ItConvertsALocalScopeToAUsingDeclaration()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "AssertionScopeNotDisposedAnalyzer", "AssertionScopeNotDisposedCodeFixProvider", """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    var scope = new AssertionScope();
                }
            }
            """);

        result.Applied.Should().BeTrue();
        result.ActionTitle.Should().Be("Convert to using declaration");
        result.FixedSource.Should().Contain("using var scope = new AssertionScope();");
    }

    /// <summary>
    /// The discarded-expression form has no local to attach <c>using</c> to, so the fix has to
    /// invent one — a different code path from the local-declaration case above.
    /// </summary>
    [Fact]
    public async Task ItAssignsADiscardedScopeToAUsingDeclaration()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "AssertionScopeNotDisposedAnalyzer", "AssertionScopeNotDisposedCodeFixProvider", """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    new AssertionScope();
                }
            }
            """);

        result.Applied.Should().BeTrue();
        result.ActionTitle.Should().Be("Assign to a using declaration");
        result.FixedSource.Should().Contain("using");
        result.FixedSource.Should().Contain("new AssertionScope()");
    }

    #endregion

    #region MPA0100 — migrate from FluentAssertions

    [Fact]
    public async Task ItRewritesTheFluentAssertionsUsing()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "FluentAssertionsMigrationAnalyzer", "FluentAssertionsMigrationCodeFixProvider", """
            using FluentAssertions;

            public class Test
            {
                public void Run() { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.ActionTitle.Should().Be("Migrate file to MintPlayer.Assertions");
        result.FixedSource.Should().Contain("using MintPlayer.Assertions;");
        result.FixedSource.Should().NotContain("using FluentAssertions;");
    }

    /// <summary>
    /// FluentAssertions.Execution collapses into the single root import rather than gaining a
    /// counterpart: it carried <c>AssertionScope</c>, which in this library lives in
    /// <c>MintPlayer.Assertions</c> despite sitting in an <c>Execution/</c> folder. Asserting the
    /// collapse explicitly, because "two usings became one" looks like a dropped import until you
    /// know that.
    /// </summary>
    [Fact]
    public async Task ItCollapsesTheExecutionSubNamespaceIntoTheRootImport()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "FluentAssertionsMigrationAnalyzer", "FluentAssertionsMigrationCodeFixProvider", """
            using FluentAssertions;
            using FluentAssertions.Execution;

            public class Test
            {
                public void Run() { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("using MintPlayer.Assertions;");
        result.FixedSource.Should().NotContain("FluentAssertions");

        var occurrences = result.FixedSource.Split(["using MintPlayer.Assertions"], StringSplitOptions.None).Length - 1;
        occurrences.Should().Be(1);
    }

    /// <summary>
    /// The renames are the part of the migration that can silently change meaning, so each entry
    /// in the rename table is asserted rather than sampled.
    /// </summary>
    [Theory]
    [InlineData("HaveCountGreaterOrEqualTo", "HaveCountGreaterThanOrEqualTo")]
    [InlineData("BeGreaterOrEqualTo", "BeGreaterThanOrEqualTo")]
    [InlineData("BeLessOrEqualTo", "BeLessThanOrEqualTo")]
    [InlineData("WithInnerExceptionExactly", "WithInnerExactly")]
    public async Task ItRenamesTheKnownRenamedMembers(string before, string after)
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "FluentAssertionsMigrationAnalyzer", "FluentAssertionsMigrationCodeFixProvider", $$"""
            using FluentAssertions;

            public class Test
            {
                public void Run(dynamic subject) => subject.Should().{{before}}(1);
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain(after);
        result.FixedSource.Should().NotContain($".{before}(");
    }

    /// <summary>
    /// Deduplication: a file already importing MintPlayer.Assertions must not end up importing it
    /// twice, which would be a compile error introduced by the fix.
    /// </summary>
    [Fact]
    public async Task ItDoesNotDuplicateAnExistingImport()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "FluentAssertionsMigrationAnalyzer", "FluentAssertionsMigrationCodeFixProvider", """
            using FluentAssertions;
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run() { }
            }
            """);

        result.Applied.Should().BeTrue();

        var occurrences = result.FixedSource.Split(["using MintPlayer.Assertions;"], StringSplitOptions.None).Length - 1;
        occurrences.Should().Be(1);
    }

    /// <summary>
    /// Members with no MintPlayer equivalent are deliberately left alone rather than guessed at —
    /// a wrong rename compiles and asserts something different, which is worse than not migrating.
    /// </summary>
    [Fact]
    public async Task ItLeavesMembersWithNoEquivalentUntouched()
    {
        var result = await Harness.Instance.ApplyCodeFixAsync(
            "FluentAssertionsMigrationAnalyzer", "FluentAssertionsMigrationCodeFixProvider", """
            using FluentAssertions;

            public class Test
            {
                public void Run(dynamic subject) => subject.Should().NotThrowAfter(1, 1);
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("NotThrowAfter");
    }

    #endregion

    #region Completeness

    /// <summary>
    /// MPA0002 is the one rule that ships without a fix — there is no mechanical way to know what
    /// assertion the author meant. Asserted explicitly so that adding a fix later is a deliberate
    /// change to this test rather than an unnoticed gap.
    /// </summary>
    [Fact]
    public void VacuousShouldHasNoCodeFix()
        => Harness.Instance.CodeFixProvidersFor("MPA0002").Should().BeEmpty();

    [Theory]
    [InlineData("MPA0001")]
    [InlineData("MPA0003")]
    [InlineData("MPA0100")]
    public void EveryOtherRuleShipsAFix(string diagnosticId)
        => Harness.Instance.CodeFixProvidersFor(diagnosticId).Should().NotBeEmpty();

    #endregion
}
