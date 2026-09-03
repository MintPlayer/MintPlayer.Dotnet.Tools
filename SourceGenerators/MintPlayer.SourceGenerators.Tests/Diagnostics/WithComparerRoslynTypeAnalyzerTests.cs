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

    #region Composite types — FindFirstRoslynLeaf

    /// <summary>
    /// A Roslyn symbol buried inside a composite type still pins the compilation, so the analyzer
    /// walks arrays, nullables and generic arguments to find one.
    /// </summary>
    /// <remarks>
    /// This walker — <c>FindFirstRoslynLeaf</c> and the type-graph traversal feeding it — was the
    /// single largest uncovered region in the analyzer. The existing fixtures all held a symbol
    /// directly as a property, which reaches the top-level check and returns before any of the
    /// recursion runs.
    /// </remarks>
    [Theory]
    [InlineData("SyntaxNode[] Nodes", "an array element")]
    [InlineData("SyntaxNode[][] Jagged", "a jagged array element")]
    [InlineData("System.Collections.Generic.List<ISymbol> Symbols", "a generic type argument")]
    [InlineData("System.Collections.Generic.Dictionary<string, SyntaxNode> ByName", "the second type argument")]
    [InlineData("System.Collections.Generic.List<SyntaxNode[]> Nested", "an array inside a generic")]
    [InlineData("(string Name, ISymbol Symbol) Pair", "a tuple element")]
    public async Task ItFlagsARoslynTypeReachedThroughAComposite(string member, string why)
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public {{member}} { get; set; } = default!; }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "MINT001", $"the model reaches a Roslyn type via {why}");
    }

    /// <summary>
    /// A nullable value type wrapping a Roslyn struct. <c>Nullable&lt;T&gt;</c> is unwrapped
    /// explicitly by the walker rather than being treated as an ordinary generic.
    /// </summary>
    [Fact]
    public async Task ItFlagsANullableRoslynStruct()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public SyntaxToken? Token { get; set; } }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "MINT001");
    }

    /// <summary>
    /// A model reached only through another model's property — the traversal has to follow member
    /// types, not just the top-level one, and must not loop on a self-referencing graph.
    /// </summary>
    [Fact]
    public async Task ItFollowsNestedModelsWithoutLoopingOnCycles()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Inner
            {
                public ISymbol? Symbol { get; set; }
                public Outer? Back { get; set; }
            }

            public class Outer
            {
                public Inner? Inner { get; set; }
                public Outer? Self { get; set; }
            }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Outer> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "MINT001",
            "the symbol is two hops away, and the cycle between Outer and Inner must not hang the walk");
    }

    /// <summary>
    /// The negative case for the walker: a composite of ordinary types must not be flagged. Without
    /// this the theory above would pass for an analyzer that flagged every composite.
    /// </summary>
    [Theory]
    [InlineData("string[] Names")]
    [InlineData("System.Collections.Generic.List<int> Counts")]
    [InlineData("System.Collections.Generic.Dictionary<string, int> Totals")]
    [InlineData("(string Name, int Count) Pair")]
    public async Task ItLeavesCompositesOfOrdinaryTypesAlone(string member)
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public {{member}} { get; set; } = default!; }

            public class Test
            {
                public void Run(IncrementalValuesProvider<Model> provider) => provider.WithComparer();
            }
            """);

        diagnostics.Should().NotContain(d => d.Id == "MINT001");
    }

    #endregion
}
