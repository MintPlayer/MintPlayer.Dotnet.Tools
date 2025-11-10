using Microsoft.CodeAnalysis;
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

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        return [];
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();
        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            using (writer.OpenBlock("public static class ClassNames"))
            {
                foreach (var declaration in declarations)
                {
                    writer.WriteLine($"public const string {declaration.Name} = \"{declaration.Name}\";");
                }
            }
        }
    }
}

public class ClassNameListProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<Models.ClassDeclaration> declarations;
    public ClassNameListProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, "ClassNameList.g.cs")
    {
        this.declarations = declarations;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        return [];
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();
        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            using (writer.OpenBlock("public static class ClassNameList"))
            {
                var list = string.Join(", ", declarations.Select(d => $"\"{d.Name}\""));
                writer.WriteLine($"public static string[] List => new[] {{ {list} }};");
            }
        }
    }
}
