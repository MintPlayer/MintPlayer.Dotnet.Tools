using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers;

public class GenericMethodProducer : Producer
{
    public GenericMethodProducer(IEnumerable<Models.GenericMethodDeclaration> methods, string rootNamespace) : base(rootNamespace, "GenericMethods.g.cs")
    {
        Methods = methods;
    }

    public IEnumerable<Models.GenericMethodDeclaration> Methods { get; }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var method in Methods)
        {
            if (method?.Method is null) continue;
            if (method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword) && method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword))
            {
                writer.WriteLine(Header);
                writer.WriteLine();
                writer.WriteLine($"namespace {RootNamespace}");
                writer.WriteLine("{");
                writer.Indent++;

                writer.Write("public ");
                if (method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) writer.Write("static ");
                writer.Write("partial ");
                writer.Write($"class {method.Method.ClassName}");
                writer.WriteLine();

                writer.WriteLine("{");
                writer.Indent++;

                for (int i = 1; i < method.Count + 1; i++)
                {
                    writer.Write("public ");
                    if (method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) writer.Write("static ");
                    if (method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) writer.Write("partial ");
                    writer.Write($"void {method.Method.MethodName}<");
                    writer.Write(string.Join(", ", Enumerable.Range(1, i).Select(i => $"T{i}")));
                    writer.Write(">(");

                    writer.Write(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => new { Type = $"T{i}", Name = $"t{i}" })
                        .Select(i => $"{i.Type} {i.Name}")));
                    writer.Write(")");
                    writer.WriteLine();

                    writer.WriteLine("{");
                    writer.Indent++;
                    if (method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) || method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
                        writer.Write($"{method.Method.ClassName}.{method.Method.MethodName}([");
                    else
                        writer.Write($"this.{method.Method.MethodName}([");
                    writer.Write(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => $"t{i}")));
                    writer.Write("]);");
                    writer.WriteLine();
                    writer.Indent--;
                    writer.WriteLine("}");
                }

                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");
            }
        }
    }
}
