using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

/// <summary>
/// MINT001 — a <c>[GenerateEquality]</c> model that still carries Roslyn symbols.
/// </summary>
/// <remarks>
/// This is the highest-consequence analyzer in the repo. Holding an <c>ISymbol</c> in a pipeline model keeps an
/// entire <c>Compilation</c> alive between runs and defeats incrementality outright — the generator appears to work,
/// and every IDE keystroke pays for it.
///
/// The rule used to trigger on <c>.WithComparer(...)</c> calls. Those are gone: models compare by value under the
/// default comparer, so the rule now inspects the properties of every <c>[GenerateEquality]</c> type (and every
/// type deriving from one). Every case of the old suite is ported to the new trigger; the cases that were about the
/// call itself have a named replacement.
///
/// The fixtures declare their own <c>GenerateEqualityAttribute</c> rather than referencing the real one: the
/// analyzer matches on the attribute's name and namespace, so a local declaration reaches exactly the same code path.
/// </remarks>
public class RoslynTypeInModelAnalyzerTests
{
    private const string Analyzer = "RoslynTypeInModelAnalyzer";
    private const string Assembly = "MintPlayer.ValueComparerGenerator";

    private static readonly Type[] RoslynReferences = [typeof(Microsoft.CodeAnalysis.SyntaxNode)];

    private const string Attributes = """
        namespace MintPlayer.ValueComparerGenerator.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class GenerateEqualityAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class EqualityIgnoreAttribute : System.Attribute { }
        }
        """;

    private static Task<IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> Run(string source)
        => GeneratorHarness.RunAnalyzerAsync(Analyzer, [Attributes, source], RoslynReferences, Assembly);

    private const string Preamble = """
        using Microsoft.CodeAnalysis;
        using MintPlayer.ValueComparerGenerator.Attributes;

        namespace Demo;
        """;

    [Fact]
    public async Task ItFlagsAModelHoldingASymbol()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model { public ISymbol? Symbol { get; set; } }
            """);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("MINT001");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("Symbol");
        diagnostic.GetMessage().Should().Contain("Model");
    }

    /// <summary>Reported on the offending property, which is where the fix goes.</summary>
    [Fact]
    public async Task ItReportsOnTheProperty()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model
            {
                public string Name { get; set; } = "";
                public SyntaxNode? Node { get; set; }
            }
            """);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)
            .Should().Be("Node");
    }

    /// <summary>
    /// Replaces ItFlagsWithNullableComparerToo: the second trigger of the old rule was the second pipeline operator.
    /// The new rule covers every model shape instead, so a record and a struct model are flagged too.
    /// </summary>
    [Theory]
    [InlineData("public partial record Model(SyntaxNode? Node);")]
    [InlineData("public partial record class Model { public SyntaxNode? Node { get; init; } }")]
    [InlineData("public partial struct Model { public SyntaxNode? Node { get; set; } }")]
    [InlineData("public partial record struct Model(SyntaxNode? Node);")]
    public async Task ItFlagsRecordAndStructModelsToo(string declaration)
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            {{declaration}}
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

            [GenerateEquality]
            public partial class Model { public List<ISymbol> Symbols { get; set; } = new(); }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    [Fact]
    public async Task ItLooksThroughNestedModels()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Inner { public ITypeSymbol? Type { get; set; } }

            [GenerateEquality]
            public partial class Model { public Inner Inner { get; set; } = new(); }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    [Fact]
    public async Task ItLooksThroughArrays()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model { public ISymbol[] Symbols { get; set; } = []; }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    /// <summary>
    /// Replaces ItFlagsAProviderOfARoslynTypeDirectly. A provider of a Roslyn type no longer passes through anything
    /// this repo owns; the model-level equivalent is a type deriving from a model, whose properties are compared by
    /// the generated equality even though the derived type carries no attribute of its own.
    /// </summary>
    [Fact]
    public async Task ItFlagsADerivedTypeWithoutTheAttribute()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public abstract partial class Base { public string Name { get; set; } = ""; }

            public partial class Derived : Base { public ISymbol? Symbol { get; set; } }
            """);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("MINT001");
        diagnostic.GetMessage().Should().Contain("Derived");
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

            [GenerateEquality]
            public partial class Node
            {
                public Node? Next { get; set; }
                public string Name { get; set; } = "";
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// A model holding another model: the inner one is reported on its own property, once, rather than again through
    /// every model that holds it.
    /// </summary>
    [Fact]
    public async Task ItReportsANestedModelOnlyOnce()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            using System.Collections.Generic;

            [GenerateEquality]
            public partial class Inner { public ISymbol? Symbol { get; set; } }

            [GenerateEquality]
            public partial class Outer
            {
                public Inner? Inner { get; set; }
                public List<Inner> Inners { get; set; } = new();
            }
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("Inner");
    }

    #region Clean models

    [Fact]
    public async Task ItStaysQuietOnARoslynFreeModel()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model
            {
                public string Name { get; set; } = "";
                public int Count { get; set; }
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

            [GenerateEquality]
            public partial class Model
            {
                public static ISymbol? Shared { get; set; }
                public string Name { get; set; } = "";
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// Replaces ItIgnoresAnUnrelatedMethodOfTheSameName. The rule is about models: a class without the attribute
    /// that holds a symbol is somebody else's business, even when it looks like a model.
    /// </summary>
    [Fact]
    public async Task ItIgnoresAClassWithoutTheAttribute()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Model { public ISymbol? Symbol { get; set; } }
            """);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// [EqualityIgnore] leaves a property out of equality, but the model still holds the symbol and so still pins
    /// the compilation. The rule is about what the model carries, not what it compares.
    /// </summary>
    [Fact]
    public async Task ItStillFlagsAnIgnoredProperty()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model { [EqualityIgnore] public ISymbol? Symbol { get; set; } }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MINT001");
    }

    /// <summary>Replaces ItStaysQuietOnCodeThatDoesNotUseWithComparer: code without any model.</summary>
    [Fact]
    public async Task ItStaysQuietOnCodeWithoutModels()
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

    #region Composite types

    /// <summary>
    /// A Roslyn symbol buried inside a composite type still pins the compilation, so the analyzer
    /// walks arrays, nullables, tuples and generic arguments to find one.
    /// </summary>
    [Theory]
    [InlineData("SyntaxNode[] Nodes", "an array element")]
    [InlineData("SyntaxNode[][] Jagged", "a jagged array element")]
    [InlineData("System.Collections.Generic.List<ISymbol> Symbols", "a generic type argument")]
    [InlineData("System.Collections.Generic.Dictionary<string, SyntaxNode> ByName", "the second type argument")]
    [InlineData("System.Collections.Generic.List<SyntaxNode[]> Nested", "an array inside a generic")]
    [InlineData("(string Name, ISymbol Symbol) Pair", "a tuple element")]
    [InlineData("System.Collections.Immutable.ImmutableArray<Location> Locations", "an ImmutableArray element")]
    public async Task ItFlagsARoslynTypeReachedThroughAComposite(string member, string why)
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model { public {{member}} { get; set; } = default!; }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "MINT001", $"the model reaches a Roslyn type via {why}");
    }

    /// <summary>
    /// A nullable value type wrapping a Roslyn struct. <c>Nullable&lt;T&gt;</c> is reached through its type argument.
    /// </summary>
    [Fact]
    public async Task ItFlagsANullableRoslynStruct()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            [GenerateEquality]
            public partial class Model { public SyntaxToken? Token { get; set; } }
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "MINT001");
    }

    /// <summary>
    /// A plain class reached only through another class's property — the traversal has to follow member types, not
    /// just the top-level one, and must not loop on a self-referencing graph.
    /// </summary>
    [Fact]
    public async Task ItFollowsNestedClassesWithoutLoopingOnCycles()
    {
        var diagnostics = await Run($$"""
            {{Preamble}}

            public class Inner
            {
                public ISymbol? Symbol { get; set; }
                public Outer? Back { get; set; }
            }

            [GenerateEquality]
            public partial class Outer
            {
                public Inner? Inner { get; set; }
                public Outer? Self { get; set; }
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

            [GenerateEquality]
            public partial class Model { public {{member}} { get; set; } = default!; }
            """);

        diagnostics.Should().NotContain(d => d.Id == "MINT001");
    }

    #endregion
}
