using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Generators;

/// <summary>
/// Writes the <c>Join</c> overloads for arity 6 to <c>n</c>, from <c>[assembly: GenerateJoinMethods(n)]</c>.
/// MintPlayer.SourceGenerators.Tools ships arity 2 to 5, so for <c>n &lt;= 5</c>, or a compilation that doesn't
/// reference Roslyn, nothing is written and no file is emitted.
/// </summary>
/// <remarks>
/// The class is <c>internal</c>: public copies in two assemblies made the referencing one's calls ambiguous (CS0121),
/// raised CS1591 in a documented consumer, and put a public type in the consumer's <c>Microsoft.CodeAnalysis</c>
/// namespace. The namespace stays, so the overloads resolve beside the Tools ones without a <c>using</c>.
/// </remarks>
internal class JoinMethodProducer : Producer
{
    /// <summary>The highest arity MintPlayer.SourceGenerators.Tools ships itself.</summary>
    private const uint ShippedArity = 5;

    private readonly uint numberOfJoinMethods;
    private readonly bool hasCodeAnalysisReference;
    public JoinMethodProducer(uint numberOfJoinMethods, bool hasCodeAnalysisReference, string rootNamespace) : base(rootNamespace, "JoinMethods.g.cs")
    {
        this.numberOfJoinMethods = numberOfJoinMethods;
        this.hasCodeAnalysisReference = hasCodeAnalysisReference;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (!hasCodeAnalysisReference || numberOfJoinMethods <= ShippedArity) return;

        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        using (writer.OpenBlock("namespace Microsoft.CodeAnalysis"))
        using (writer.OpenBlock("internal static partial class IncrementalValueProviderAdditionalEx"))
        {
            for (var i = (int)ShippedArity + 1; i <= numberOfJoinMethods; i++)
            {
                var typeParameters = string.Join(", ", Enumerable.Range(1, i).Select(n => $"T{n}"));                // T1, T2, T3, T4, ...
                var previousTypeParameters = string.Join(", ", Enumerable.Range(1, i - 1).Select(n => $"T{n}"));    // T1, T2, T3, ...
                var selectParameters = string.Join(", ", Enumerable.Range(1, i).Select(n => n == i ? "t.Right" : $"t.Left.Item{n}"));

                // Chains like the Tools overloads: previous.Join(next).
                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValueProvider<({typeParameters})> Join<{typeParameters}>(");
                writer.Indent++;
                writer.WriteLine($"this global::Microsoft.CodeAnalysis.IncrementalValueProvider<({previousTypeParameters})> previous,");
                writer.WriteLine($"global::Microsoft.CodeAnalysis.IncrementalValueProvider<T{i}> next)");
                writer.WriteLine("=> global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.Combine(previous, next)");
                writer.IndentSingleLine($".Select(static (t, _) => ({selectParameters}));");
                writer.Indent--;
                writer.WriteLine();
            }
        }
    }
}
