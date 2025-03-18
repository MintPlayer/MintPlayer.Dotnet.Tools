using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Producers;

public class MethodDescriptionProducer : Producer
{
    private readonly IEnumerable<XmlMarkup> declarations;
    public MethodDescriptionProducer(IEnumerable<XmlMarkup> declarations, string rootNamespace) : base(rootNamespace, "Markup2Descriptions.g.cs")
    {
        this.declarations = declarations;
    }
        
    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();

        foreach (var nsGrouping in declarations.GroupBy(d => d.Namespace))
        {
            if (!string.IsNullOrEmpty(nsGrouping.Key))
            {
                writer.WriteLine($"namespace {nsGrouping.Key}");
                writer.WriteLine("{");
                writer.Indent++;
            }

            foreach (var clsGrouping in nsGrouping.GroupBy(cl => cl.ClassGenericParameters.Any() ? $"{cl.ClassName}<{string.Join(", ", cl.ClassGenericParameters)}>" : cl.ClassName))
            {
                writer.WriteLine($"partial class {clsGrouping.Key}");
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var methodGrouping in clsGrouping.GroupBy(meth => $"{meth.MethodName}({string.Join(", ", meth.MethodParameters.Select(p => $"{p.Type} {p.Name}"))})"))
                {
                    var first = methodGrouping.First();
                    writer.WriteLine($"""[global::System.ComponentModel.Description("{first.Text}")]""");
                    if (first.MethodAccessModifiers.Any(SyntaxKind.ProtectedKeyword))
                        writer.Write("protected ");
                    if (first.MethodAccessModifiers.Any(SyntaxKind.PrivateKeyword))
                        writer.Write("private ");
                    if (first.MethodAccessModifiers.Any(SyntaxKind.PublicKeyword))
                        writer.Write("public ");
                    if (first.MethodAccessModifiers.Any(SyntaxKind.InternalKeyword))
                        writer.Write("internal ");
                    writer.WriteLine($"partial {first.ReturnType} {methodGrouping.Key};");
                }

                writer.Indent--;
                writer.WriteLine("}");
            }

            if (!string.IsNullOrEmpty(nsGrouping.Key))
            {
                writer.Indent--;
                writer.WriteLine("}");
            }
        }
    }
}
