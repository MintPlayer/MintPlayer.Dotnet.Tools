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

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var namespaceGrouping in symbols.GroupBy(s => s.PathSpec?.ContainingNamespace))
        {
            var qualifiers = namespaceGrouping
                .Where(c => c.PathSpec is { IsSupported: true } && !string.IsNullOrEmpty(c.MarkupText))
                .ToArray();
            if (qualifiers.Length == 0) continue;

            using (writer.OpenNamespace(namespaceGrouping.Key))
            {
                foreach (var type in qualifiers)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // The type's own header comes from its PathSpec: the user's keyword, its type parameters (W2) and
                    // interface parents (K5), never a hand-written `partial class Name`.
                    var attribute = $"""[global::System.ComponentModel.Description("{type.MarkupText!.EscapeForStringLiteral()}")]""";
                    using (writer.OpenPathSpec(type.PathSpec, includeTarget: true, targetLeadingLines: [attribute]))
                    {
                    }
                }
            }
        }
    }
}
