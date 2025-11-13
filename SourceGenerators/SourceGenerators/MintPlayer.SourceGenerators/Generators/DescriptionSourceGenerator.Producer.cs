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
            using (writer.OpenBlock($"namespace {namespaceGrouping.Key}"))
            {


                writer.WriteLine("internal static partial class SymbolDescriptions");
                using (writer.OpenBlock(string.Empty))
                {
                    foreach (var symbol in namespaceGrouping)
                    {
                        var sanitizedTypeName = symbol.TypeName!.Replace(".", "_").Replace("`", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "_").Replace("&", "_");
                        writer.WriteLine($"public static string {sanitizedTypeName} => @\"{symbol.MarkupText!.Replace("\"", "\"\"")}\";");
                    }
                }
            }
            writer.WriteLine();
        }
    }
}
