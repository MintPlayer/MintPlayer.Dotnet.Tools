using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace MintPlayer.SourceGenerators.Producers
{
    public class GenericMethodProducer : Producer
    {
        public GenericMethodProducer(Models.GenericMethodDeclaration method, string rootNamespace) : base(rootNamespace, "GenericMethods.g.cs")
        {
            Method = method;
        }

        public GenericMethodDeclaration Method { get; }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            if (Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword) && Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword))
            {
                writer.WriteLine(Header);
                writer.WriteLine();
                writer.WriteLine($"namespace {RootNamespace};");

                writer.Write("public ");
                if (Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) writer.Write("static ");
                writer.Write("partial ");
                writer.Write($"class {Method.Method.ClassName}");
                writer.WriteLine();

                writer.WriteLine("{");
                writer.Indent++;

                for (int i = 1; i < Method.Count + 1; i++)
                {
                    writer.Write("public ");
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) writer.Write("static ");
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) writer.Write("partial ");
                    writer.Write($"void {Method.Method.MethodName}<");
                    writer.Write(string.Join(", ", Enumerable.Range(1, i).Select(i => $"T{i}")));
                    writer.Write(">(");

                    writer.Write(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => new { Type = $"T{i}", Name = $"t{i}" })
                        .Select(i => $"{i.Type} {i.Name}")));
                    writer.Write(")");
                    writer.WriteLine();

                    writer.WriteLine("{");
                    writer.Indent++;
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) || Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
                        writer.Write($"{Method.Method.ClassName}.{Method.Method.MethodName}([");
                    else
                        writer.Write($"this.{Method.Method.MethodName}([");
                    writer.Write(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => $"t{i}")));
                    writer.Write("]);");
                    writer.WriteLine();
                    writer.Indent--;
                    writer.WriteLine("}");
                }

                writer.Indent--;
                writer.WriteLine("}");
            }
        }
    }
}
