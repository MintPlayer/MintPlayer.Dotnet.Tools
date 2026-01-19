using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class DescriptionsProducer : Producer
{
    private readonly IEnumerable<SymbolWithMarkups> symbols;
    public DescriptionsProducer(IEnumerable<Models.SymbolWithMarkups> symbols, string rootNamespace) : base(rootNamespace, "SymbolDescriptions.g.cs")
    {
        this.symbols = symbols;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();

        foreach (var namespaceGrouping in symbols.GroupBy(s => s.PathSpec?.ContainingNamespace))
        {
            var ns = namespaceGrouping.Key;
            if (namespaceGrouping.NotNull().ToArray() is not { Length: > 0 } classes) continue;

            var qualifiers = classes.Where(c => c.PathSpec is { AllPartial: true } && !string.IsNullOrEmpty(c.MarkupText)).ToArray();
            if (qualifiers.Length == 0) continue;

            IDisposableWriterIndent? namespaceBlock = string.IsNullOrEmpty(ns) ? null : writer.OpenBlock($"namespace {ns}");

            foreach (var cls in qualifiers)
            {
                if (string.IsNullOrEmpty(cls.MarkupText)) continue;
                using (writer.OpenPathSpec(cls.PathSpec))
                {
                    writer.WriteLine($"""[global::System.ComponentModel.Description("{cls.MarkupText.EscapeForStringLiteral()}")]""");
                    writer.WriteLine($$"""partial {{cls.TypeKind}} {{cls.Name}} { }""");
                }
            }

            namespaceBlock?.Dispose();
        }
    }
}
