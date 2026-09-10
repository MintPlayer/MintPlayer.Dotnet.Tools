using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

/// <summary>
/// Layer 3b: the fix actually produces the intended code. An analyzer test proves the
/// diagnostic fires; only applying the fix proves the fix is right.
/// </summary>
public class UnusedUsingsCodeFixTests
{
    private static Task<CodeFixResult> Fix(string source)
        => CodeFixHarness.ApplyAsync("UnusedUsingsAnalyzer", "UnusedUsingsCodeFixProvider", source);

    [Fact]
    public async Task ItRemovesASingleUnusedUsing()
    {
        var result = await Fix("""
            using System.Text;

            namespace Demo;

            public class Thing { }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().NotContain("using System.Text;");
        result.FixedSource.Should().Contain("public class Thing");
    }

    /// <summary>
    /// Characterization. The action is titled "Remove all unused usings", but it derives what
    /// to remove from <c>context.Diagnostics</c> — and Roslyn validates that every diagnostic
    /// in a CodeFixContext shares the requested span, so a provider can only ever see the one
    /// at the cursor. A single invocation therefore removes ONE using, not all of them;
    /// document-wide removal comes from the BatchFixer FixAll provider. Recorded in
    /// docs/PRD-TestCoverage.md; the title over-promises for a single invocation.
    /// </summary>
    [Fact]
    public async Task ASingleInvocationRemovesOnlyTheUsingAtTheReportedSpan()
    {
        var result = await Fix("""
            using System;
            using System.Text;
            using System.Collections.Generic;

            namespace Demo;

            public class Thing { }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().NotContain("using System;");
        result.FixedSource.Should().Contain("using System.Text;");
        result.FixedSource.Should().Contain("using System.Collections.Generic;");
    }

    [Fact]
    public async Task ItKeepsTheUsingsThatAreActuallyUsed()
    {
        var result = await Fix("""
            using System.Text;
            using System.Collections.Generic;

            namespace Demo;

            public class Thing
            {
                public string Describe() => new StringBuilder().Append("x").ToString();
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("using System.Text;");
        result.FixedSource.Should().NotContain("using System.Collections.Generic;");
    }

    [Fact]
    public async Task TheFixedCodeHasNoDiagnosticsLeft()
    {
        var result = await Fix("""
            using System;
            using System.Text;

            namespace Demo;

            public class Thing
            {
                public string Describe() => new StringBuilder().Append("x").ToString();
            }
            """);

        var after = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", [result.FixedSource]);

        after.Should().BeEmpty();
    }

    [Fact]
    public async Task ItOffersADescriptiveTitle()
    {
        var result = await Fix("""
            using System.Text;

            namespace Demo;

            public class Thing { }
            """);

        result.ActionTitle.Should().Be("Remove all unused usings");
    }

    [Fact]
    public async Task WithNothingToFix_ItDoesNotOfferAnAction()
    {
        var result = await Fix("""
            namespace Demo;

            public class Thing { }
            """);

        result.Applied.Should().BeFalse();
        result.FixedSource.Should().Contain("public class Thing");
    }

    [Fact]
    public async Task ApplyingTheFixIsIdempotent()
    {
        var first = await Fix("""
            using System.Text;

            namespace Demo;

            public class Thing { }
            """);

        var second = await Fix(first.FixedSource);

        second.Applied.Should().BeFalse();
        second.FixedSource.Should().Be(first.FixedSource);
    }

}

public class InterfaceImplementationCodeFixTests
{
    [Fact]
    public async Task ItAddsTheMissingMemberToTheInterface()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public void Extra() { }
            }
            """);

        result.Applied.Should().BeTrue();

        // "void Extra();" — with a semicolon and no body — can only be the INTERFACE declaration.
        //
        // This assertion used to be Contain("Extra"), which the fixture satisfies before the fix
        // runs at all: `public void Extra() { }` is right there in the class. Combined with
        // Applied being true whenever the action yields an ApplyChangesOperation — which it does
        // even when the fix returns the solution untouched — the test passed while the whole body
        // of the fix was unreachable. It was unreachable because the harness added its document
        // without a filePath, so the fix's lookup of the interface's own document matched nothing.
        result.FixedSource.Should().Contain("void Extra();",
            "the fix's job is to declare the missing member on IThing, not merely to leave the class alone");
    }

    /// <summary>
    /// A method with parameters and a non-void return exercises the parameter-list and return-type
    /// construction in <c>CreateInterfaceMember</c>, which a parameterless <c>void</c> does not.
    /// </summary>
    [Fact]
    public async Task ItAddsAMethodWithItsParametersAndReturnType()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public int Compute(string name, int count) => count;
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("Compute(");
        result.FixedSource.Should().Contain("string name");
        result.FixedSource.Should().Contain("int count");
    }

    /// <summary>
    /// The <c>IPropertySymbol</c> arm, which builds a get/set accessor list rather than a
    /// parameter list.
    /// </summary>
    [Fact]
    public async Task ItAddsAPropertyWithGetAndSetAccessors()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public string Name { get; set; } = "";
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("Name");
        result.FixedSource.Should().Contain("get;");
        result.FixedSource.Should().Contain("set;");
    }

    /// <summary>
    /// <c>[NoInterfaceMember]</c> is the opt-out, and it is the filter that makes the fix usable at
    /// all — without it every public member of a partial CLI command would be dragged onto its
    /// interface.
    /// </summary>
    [Fact]
    public async Task ItSkipsMembersMarkedNoInterfaceMember()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }

                [NoInterfaceMember]
                public void Hidden() { }

                public void Visible() { }
            }
            """);

        result.FixedSource.Should().Contain("void Visible();");
        result.FixedSource.Should().NotContain("void Hidden();",
            "[NoInterfaceMember] is the opt-out and must keep the member off the interface");
    }

    [Fact]
    public async Task WithNothingToFix_ItDoesNotOfferAnAction()
    {
        var result = await CodeFixHarness.ApplyAsync(
            "InterfaceImplementationAnalyzer",
            "InterfaceCodeFixProvider",
            """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
            }
            """);

        result.Applied.Should().BeFalse();
    }

    private const string Analyzer = "InterfaceImplementationAnalyzer";
    private const string Provider = "InterfaceCodeFixProvider";

    /// <summary>
    /// The scenario the fix exists for: the interface lives in a project the class references.
    /// </summary>
    /// <remarks>
    /// This is the acceptance gate for the whole rule. <c>InterfaceCodeFixProvider</c> uses
    /// <c>createChangedSolution</c> and reaches across projects precisely so it can do this, and
    /// until the harness could express two projects there was no test that executed that path —
    /// the five tests that existed all put the interface and the class in one source string, where
    /// the cross-project code is never exercised and cannot fail.
    /// </remarks>
    [Fact]
    public async Task ItAddsTheMemberToAnInterfaceInAReferencedProject()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider,
        [
            FixtureProject.Of("Contracts", ("IThing.cs", """
                namespace Demo;

                public interface IThing
                {
                    void DoIt();
                }
                """)),
            FixtureProject.Of("Impl", ("Thing.cs", """
                namespace Demo;

                public class Thing : IThing
                {
                    public void DoIt() { }
                    public void Extra() { }
                }
                """)),
        ]);

        result.Applied.Should().BeTrue();

        result.Document("Contracts/IThing.cs").Should().Contain("void Extra();",
            "the member belongs on the interface, in the project the interface lives in");

        result.Document("Impl/Thing.cs").Should().NotContain("void Extra();",
            "the class is not the document the fix is meant to edit");

        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    /// <summary>
    /// With several interfaces to choose from, the fix offers the choice rather than guessing.
    /// </summary>
    /// <remarks>
    /// The provider used to take <c>classSymbol.Interfaces.FirstOrDefault()</c>, so the target was
    /// whichever interface appeared first in the base list — correct only by coincidence, and only
    /// while every fixture implemented exactly one interface.
    /// </remarks>
    [Fact]
    public async Task ItOffersOneActionPerImplementedInterface()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, [FixtureProject.Of("P", TwoInterfaces)]);

        result.Titles.Should().HaveCount(2);
        result.Titles.Should().Contain(t => t.Contains("IPerson"));
        result.Titles.Should().Contain(t => t.Contains("IAuditable"));
    }

    [Fact]
    public async Task ItAddsTheMemberToTheChosenInterface()
    {
        var first = await CodeFixHarness.ApplyAsync(Analyzer, Provider, [FixtureProject.Of("P", TwoInterfaces)], actionIndex: 0);
        var second = await CodeFixHarness.ApplyAsync(Analyzer, Provider, [FixtureProject.Of("P", TwoInterfaces)], actionIndex: 1);

        Declaration(first, "IPerson").Should().Contain("LastName");
        Declaration(first, "IAuditable").Should().NotContain("LastName");

        Declaration(second, "IAuditable").Should().Contain("LastName");
        Declaration(second, "IPerson").Should().NotContain("LastName");

        first.Errors.Should().BeEmpty(first.ErrorText);
        second.Errors.Should().BeEmpty(second.ErrorText);
    }

    /// <summary>
    /// A record implementing an interface must not throw before the light bulb is even offered.
    /// </summary>
    /// <remarks>
    /// The analyzer gates on <c>TypeKind.Class</c>, which includes records, but the fix located the
    /// declaration with <c>.First()</c> over <c>OfType&lt;ClassDeclarationSyntax&gt;()</c> — and a
    /// <c>RecordDeclarationSyntax</c> is a sibling of that type, not a subtype. Registration threw
    /// <c>InvalidOperationException: Sequence contains no elements</c> on perfectly ordinary C#.
    /// </remarks>
    [Fact]
    public async Task ItFixesARecordWithoutThrowing()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public record Thing : IThing
            {
                public void DoIt() { }
                public void Extra() { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("void Extra();");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    /// <summary>
    /// Member kinds the analyzer never reports must not reach the fix's member factory.
    /// </summary>
    /// <remarks>
    /// The analyzer restricts candidates to methods and properties; the fix's filter dropped that
    /// clause, so a field, an event or a nested type flowed into <c>CreateInterfaceMember</c> and
    /// hit <c>throw new NotImplementedException</c>. A code fix must never throw at the user, so
    /// the factory now declines unsupported kinds instead. Each fixture also carries one genuinely
    /// missing method, because without a reported diagnostic the fix is never invoked at all.
    /// </remarks>
    [Theory]
    [InlineData("public string Note;")]
    [InlineData("public event System.EventHandler Changed;")]
    [InlineData("public class Nested { }")]
    public async Task ItIgnoresMemberKindsItCannotDeclare(string extraMember)
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, $$"""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public void Extra() { }
                {{extraMember}}
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("void Extra();");
        result.FixedSource.Should().NotContain("Note");
        result.FixedSource.Should().NotContain("Changed");
        result.FixedSource.Should().NotContain("Nested");
        result.Errors.Should().BeEmpty(result.ErrorText);
    }

    /// <summary>
    /// The generated interface property mirrors the class property's accessors.
    /// </summary>
    /// <remarks>
    /// The fix emitted <c>{ get; set; }</c> unconditionally. For a get-only class property that
    /// declares an interface member the class does not implement — CS0535, a hard compile error
    /// introduced into code that compiled a moment earlier. This is the only defect in the rule
    /// that turns working code into broken code, which is why the assertion is on
    /// <see cref="CodeFixResult.Errors"/> and not merely on the text.
    /// </remarks>
    [Theory]
    [InlineData("public string Extra => \"x\";", "get;", "set;")]
    [InlineData("public string Extra { get; init; }", "init;", "set;")]
    [InlineData("public string Extra { get; set; }", "set;", null)]
    public async Task ItPreservesThePropertyAccessorShape(string declaration, string expected, string? notExpected)
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, $$"""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                {{declaration}}
            }
            """);

        result.Applied.Should().BeTrue();
        result.Errors.Should().BeEmpty(result.ErrorText);

        var member = Declaration(result, "IThing");
        member.Should().Contain(expected);
        if (notExpected is not null) member.Should().NotContain(notExpected);
    }

    /// <summary>
    /// A member inherited from a base interface is already satisfied and must not be re-declared.
    /// </summary>
    /// <remarks>
    /// The analyzer checks the whole hierarchy; the fix checked only the interface's own members,
    /// so it re-declared an inherited member on the derived interface — CS0108 member hiding, in
    /// code the analyzer had deliberately accepted.
    /// </remarks>
    [Fact]
    public async Task ItDoesNotRedeclareAMemberFromABaseInterface()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, """
            namespace Demo;

            public interface IBase
            {
                string Name { get; }
            }

            public interface IThing : IBase
            {
            }

            public class Thing : IThing
            {
                public string Name => "x";
                public void Extra() { }
            }
            """);

        result.Applied.Should().BeTrue();
        result.FixedSource.Should().Contain("void Extra();");

        Declaration(result, "IThing").Should().NotContain("Name",
            "Name is already declared on IBase, which is why the analyzer never reported it");
    }

    /// <summary>Applying the fix, then running the analyzer again, leaves nothing to report.</summary>
    [Fact]
    public async Task TheFixedCodeHasNoDiagnosticsLeft()
    {
        var result = await CodeFixHarness.ApplyAsync(Analyzer, Provider, """
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public void Extra() { }
            }
            """);

        result.Applied.Should().BeTrue();

        var again = await GeneratorHarness.RunAnalyzerAsync(Analyzer, [result.FixedSource]);
        again.Should().BeEmpty();
    }

    private const string TwoInterfaces = """
        namespace Demo;

        public interface IPerson
        {
            string Name { get; }
        }

        public interface IAuditable
        {
            int Version { get; }
        }

        public class Person : IPerson, IAuditable
        {
            public string Name => "x";
            public int Version => 1;
            public string LastName => "y";
        }
        """;

    /// <summary>The body of one interface declaration in the fixed source.</summary>
    /// <remarks>
    /// Asserting on the whole document cannot tell an interface declaration from the class member
    /// it was generated from — <c>Contain("LastName")</c> is satisfied by the class before the fix
    /// runs at all, which is how an earlier test in this file passed while the fix was unreachable.
    /// </remarks>
    private static string Declaration(CodeFixResult result, string interfaceName)
    {
        var source = result.FixedSource;
        var start = source.IndexOf($"interface {interfaceName}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the fixed source should still declare {interfaceName}");

        var open = source.IndexOf('{', start);
        var close = source.IndexOf('}', open);
        return source[open..(close + 1)];
    }
}
