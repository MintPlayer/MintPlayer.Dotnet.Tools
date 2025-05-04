using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

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

public class ClassNameListProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<Models.ClassDeclaration> declarations;
    public ClassNameListProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, "ClassNameList.g.cs")
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

        writer.WriteLine("public static class ClassNameList");
        writer.WriteLine("{");
        writer.Indent++;
        var list = string.Join(", ", declarations.Select(d => $"\"{d.Name}\""));
        writer.WriteLine($$"""public static string[] List => new[] { {{list}} };""");

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
