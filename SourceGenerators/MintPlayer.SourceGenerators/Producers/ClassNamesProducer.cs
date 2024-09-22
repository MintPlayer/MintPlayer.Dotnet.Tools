using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.Reflection.PortableExecutable;
using System.Text;

namespace MintPlayer.SourceGenerators.Producers;

public class ClassNamesProducer : Producer
{
    private readonly IEnumerable<ClassDeclaration> declarations;
    public ClassNamesProducer(IEnumerable<ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace)
    {
        this.declarations = declarations;
    }

    protected override ProducedSource ProduceSource(CancellationToken cancellationToken)
    {
        var source = new StringBuilder();
        source.AppendLine(Header);
        source.AppendLine();
        source.AppendLine($"namespace {RootNamespace};");

        source.AppendLine("public static class ClassNames");
        source.AppendLine("{");
        var list = string.Join(", ", declarations.Select(d => $"\"{d.Name}\""));
        source.AppendLine($"    public static string[] List => [{list}];");
        source.AppendLine("}");

        var sourceText = source.ToString();
        var fileName = $"ClassNames.g.cs";

        return new ProducedSource { FileName = fileName, Source = sourceText };
    }
}