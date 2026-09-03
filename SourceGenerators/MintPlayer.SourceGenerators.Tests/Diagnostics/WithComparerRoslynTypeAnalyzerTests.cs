using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

/// <summary>
/// MINT001 — <c>.WithComparer(...)</c> applied to a model that still carries Roslyn symbols.
/// </summary>
/// <remarks>
/// This is the highest-consequence analyzer in the repo and had one smoke test. Holding an
/// <c>ISymbol</c> in a pipeline model keeps an entire <c>Compilation</c> alive between runs and
/// defeats incrementality outright — the generator appears to work, and every IDE keystroke pays
/// for it.
///
/// The fixtures declare their own <c>WithComparer</c> extension rather than referencing the real
/// one: the analyzer matches on method NAME plus the provider return type, so a local declaration
/// reaches exactly the same code path while keeping the fixture readable.
/// </remarks>
public class WithComparerRoslynTypeAnalyzerTests
{
    private const string Analyzer = "WithComparerRoslynTypeAnalyzer";
    private const string Assembly = "MintPlayer.ValueComparerGenerator";

    private static readonly Type[] RoslynReferences = [typeof(Microsoft.CodeAnalysis.SyntaxNode)];

    private static Task<IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> Run(string source)
        => GeneratorHarness.RunAnalyzerAsync(Analyzer, [source], RoslynReferences, Assembly);

    private const string Preamble = """
        using Microsoft.CodeAnalysis;

        namespace Demo;

        public static class Ext
        {
            public static IncrementalValuesProvider<T> WithComparer<T>(this IncrementalValuesProvider<T> provider) => provider;
            public static IncrementalValueProvider<T> WithNullableComparer<T>(this IncrementalValueProvider<T> provider) => provider;
        }
        """;

    [Fact]
    public async Task ItFlagsAModelHoldingASymbol()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public ISymbol? Symbol { get; set; } }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("MINT001");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("Symbol");
    }

    [Fact]
    public async Task ItFlagsWithNullableComparerToo()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public SyntaxNode? Node { get; set; } }

            public class Test
            {
                public void Run(IncrementalValueProvider<Model> provider) => provider.WithNullableComparer();
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    /// <summary>
    /// A Roslyn type reached through a generic argument is just as fatal as a direct property,
    /// and much easier to miss by eye.
    /// </summary>
    [Fact]
    public async Task ItLooksThroughGenericArguments()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            using System.Collections.Generic;

            public class Model { public List<ISymbol> Symbols { get; set; } = new(); }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    [Fact]
    public async Task ItLooksThroughNestedModels()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Inner { public ITypeSymbol? Type { get; set; } }
            public class Model { public Inner Inner { get; set; } = new(); }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    [Fact]
    public async Task ItLooksThroughArrays()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public ISymbol[] Symbols { get; set; } = []; }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    [Fact]
    public async Task ItFlagsAProviderOfARoslynTypeDirectly()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Test
            {
                public void Run(IncrementalValuesProvider<ISymbol> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    /// <summary>
    /// The recursion tracks visited types. A self-referencing model must terminate rather than
    /// hang the compiler — a failure that would look like the analyzer being slow, not wrong.
    /// </summary>
    [Fact]
    public async Task ItTerminatesOnARecursiveModel()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Node
            {
                public Node? Next { get; set; }
                public string Name { get; set; } = "";
            }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Node> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    #region Clean models

    [Fact]
    public async Task ItStaysQuietOnARoslynFreeModel()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model
            {
                public string Name { get; set; } = "";
                public int Count { get; set; }
            }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// A static property is not part of the compared value, so it must not trigger the rule —
    /// otherwise the fix is to delete a perfectly fine static member.
    /// </summary>
    [Fact]
    public async Task ItIgnoresStaticProperties()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model
            {
                public static ISymbol? Shared { get; set; }
                public string Name { get; set; } = "";
            }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// A method that happens to be called WithComparer but returns something else is not the
    /// pipeline operator this rule is about.
    /// </summary>
    [Fact]
    public async Task ItIgnoresAnUnrelatedMethodOfTheSameName()
    {
        var diagnostics = await Run("""
            using Microsoft.CodeAnalysis;

            namespace Demo;

            public class Model { public ISymbol? Symbol { get; set; } }

            public class Bag
            {
                public Bag WithComparer() => this;
            }

            public class Test
            {
                public void Run(Bag bag) => bag.WithComparer();
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItStaysQuietOnCodeThatDoesNotUseWithComparer()
    {
        var diagnostics = await Run("""
            namespace Demo;

            public class Thing { public string Name { get; set; } = ""; }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItStaysQuietOnAnEmptyCompilation()
    {
        var diagnostics = await Run("// nothing");

        diagnostics.Should().BeEmpty();
    }

    #endregion
}
