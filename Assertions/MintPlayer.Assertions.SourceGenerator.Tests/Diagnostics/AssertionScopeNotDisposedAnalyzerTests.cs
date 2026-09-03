namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// MPA0003 — an <c>AssertionScope</c> that nothing disposes.
/// </summary>
/// <remarks>
/// The scope collects failures and reports them on dispose. Never disposing it means every failure
/// it gathered is discarded, so the test passes — the same silent-green failure mode as MPA0001,
/// reached a different way.
/// </remarks>
public class AssertionScopeNotDisposedAnalyzerTests
{
    private const string Analyzer = "AssertionScopeNotDisposedAnalyzer";

    private static string Wrap(string body) => $$"""
        using MintPlayer.Assertions;

        public class Test
        {
            public void Run()
            {
        {{body}}
            }
        }
        """;

    [Fact]
    public async Task ItFlagsAScopeCreatedAndImmediatelyDiscarded()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, Wrap("        new AssertionScope();"));

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MPA0003");
    }

    [Fact]
    public async Task ItFlagsAScopeInALocalThatIsNeverUsedAgain()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, Wrap("""
                    var scope = new AssertionScope();
                    1.Should().Be(1);
            """));

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MPA0003");
    }

    [Fact]
    public async Task ItIgnoresAUsingDeclaration()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, Wrap("""
                    using var scope = new AssertionScope();
                    1.Should().Be(1);
            """));

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAUsingStatement()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, Wrap("""
                    using (var scope = new AssertionScope())
                    {
                        1.Should().Be(1);
                    }
            """));

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// The analyzer stays quiet when the local is referenced after its declaration, on the
    /// assumption that something downstream disposes it. Deliberately imprecise in that direction:
    /// a warning on every scope that is merely passed to a helper would be noise, and noise gets
    /// the rule suppressed wholesale.
    /// </summary>
    [Fact]
    public async Task ItIgnoresAScopeThatIsUsedLater()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, Wrap("""
                    var scope = new AssertionScope();
                    1.Should().Be(1);
                    scope.Dispose();
            """));

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// Nothing to do with AssertionScope — guards against the analyzer matching on type name
    /// rather than symbol identity.
    /// </summary>
    [Fact]
    public async Task ItIgnoresAnUnrelatedDisposable()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System.IO;

            public class Test
            {
                public void Run()
                {
                    var stream = new MemoryStream();
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }
}
