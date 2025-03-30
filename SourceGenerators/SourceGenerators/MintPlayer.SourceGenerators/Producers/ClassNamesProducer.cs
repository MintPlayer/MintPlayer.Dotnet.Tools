using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Producers;

public class ClassNamesProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<Models.ClassDeclaration> declarations;
    public ClassNamesProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, "ClassNames.g.cs")
    {
        this.declarations = declarations;
    }

    public IEnumerable<Microsoft.CodeAnalysis.Diagnostic> GetDiagnostics()
    {
        return [];
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();
        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class ClassNames");
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var declaration in declarations)
        {
            writer.WriteLine($"public const string {declaration.Name} = \"{declaration.Name}\";");
        }

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
