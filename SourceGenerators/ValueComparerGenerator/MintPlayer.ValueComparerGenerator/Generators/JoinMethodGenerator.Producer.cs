using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Generators;

internal class JoinMethodProducer : Producer
{
    private readonly JoinMethodInfo info;
    public JoinMethodProducer(JoinMethodInfo info, string rootNamespace) : base(rootNamespace, "JoinMethods.g.cs")
    {
        this.info = info;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        writer.WriteLine("namespace Microsoft.CodeAnalysis");
        writer.WriteLine("{");
        writer.Indent++;

        if (info.HasCodeAnalysisReference)
        {
            writer.WriteLine("public static class IncrementalValueProviderAdditionalEx");
            writer.WriteLine("{");
            writer.Indent++;

            info.NumberOfJoinMethods ??= 5;
            for (var i = 6; i <= info.NumberOfJoinMethods; i++)
            {
                var typeParameters = string.Join(", ", Enumerable.Range(1, i).Select(n => $"T{n}"));                // T1, T2, T3, T4, ...
                var typeParametersBraced = $"({typeParameters})";                                                   // (T1, T2, T3, T4, ...)
                var previousTypeParameters = string.Join(", ", Enumerable.Range(1, i - 1).Select(n => $"T{n}"));    // T1, T2, T3, ...
                var previousTuple = $"({previousTypeParameters})";                                                  // (T1, T2, T3, ...)
                var methodParameters = string.Join(", ", Enumerable.Range(1, i).Select(n => n == 1 ? $"this global::Microsoft.CodeAnalysis.IncrementalValueProvider<T{previousTuple}> previous" : $"global::Microsoft.CodeAnalysis.IncrementalValueProvider<T{n}> p{n}"));
                var selectParameters = string.Join(", ", Enumerable.Range(1, i).Select(n => n == i ? "t.Right" : $"t.Left.Item{n}"));
                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValueProvider<({typeParameters})> Join<{typeParameters}>(");
                writer.Indent++;
                writer.WriteLine($"{methodParameters})");
                if (i == 2)
                {
                    writer.WriteLine($"=> global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.Combine(previous, p{i});");
                }
                else
                {
                    writer.WriteLine($"=> global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.Combine(previous, p{i})");
                    writer.Indent++;
                    writer.WriteLine($".Select(static (t, _) => ({selectParameters}));");
                    writer.Indent--;
                    writer.Indent--;
                }
                writer.WriteLine();
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.Indent--;
        writer.WriteLine("}");
    }
}
