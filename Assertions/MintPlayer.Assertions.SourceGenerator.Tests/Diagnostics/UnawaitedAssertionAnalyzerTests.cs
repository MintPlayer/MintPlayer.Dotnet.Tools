namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// MPA0001 — an assertion that returns something awaitable, used as a bare statement.
/// </summary>
/// <remarks>
/// This is the only rule in the set with <see cref="Microsoft.CodeAnalysis.DiagnosticSeverity.Error"/>
/// severity, and rightly so: an un-awaited <c>ThrowAsync</c> never runs its assertion, so the test
/// passes whatever the code under test does. A green suite that verifies nothing is worse than a
/// red one.
/// </remarks>
public class UnawaitedAssertionAnalyzerTests
{
    private const string Analyzer = "UnawaitedAssertionAnalyzer";

    /// <summary>
    /// ThrowAsync returns ThrownExceptionTask&lt;T&gt;, not a Task — so this case only works
    /// because SymbolHelpers.IsTaskLike also recognises awaitables structurally, by looking for a
    /// parameterless GetAwaiter. A narrower check would silently go quiet on exactly the assertion
    /// most likely to be forgotten.
    /// </summary>
    [Fact]
    public async Task ItFlagsAnUnawaitedCustomAwaitable()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System;
            using System.Threading.Tasks;
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    Func<Task> act = () => Task.CompletedTask;
                    act.Should().ThrowAsync<InvalidOperationException>();
                }
            }
            """);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("MPA0001");
        diagnostics[0].Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ItFlagsAnUnawaitedTask()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System;
            using System.Threading.Tasks;
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    Func<Task> act = () => Task.CompletedTask;
                    act.Should().NotThrowAsync();
                }
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MPA0001");
    }

    [Fact]
    public async Task ItIgnoresAnAwaitedAssertion()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System;
            using System.Threading.Tasks;
            using MintPlayer.Assertions;

            public class Test
            {
                public async Task Run()
                {
                    Func<Task> act = () => Task.CompletedTask;
                    await act.Should().ThrowAsync<InvalidOperationException>();
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresASynchronousAssertion()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run() => 1.Should().Be(1);
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// The rule is scoped to this library's own namespace. An un-awaited Task from anywhere else
    /// is someone else's problem — CS4014 and the BCL analyzers already cover it, and duplicating
    /// them would make the rule noisy enough to be turned off.
    /// </summary>
    [Fact]
    public async Task ItIgnoresAnUnawaitedTaskFromOutsideTheLibrary()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using System.Threading.Tasks;

            public class Test
            {
                public void Run() => Task.Delay(1);
            }
            """);

        diagnostics.Should().BeEmpty();
    }
}
