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
}
