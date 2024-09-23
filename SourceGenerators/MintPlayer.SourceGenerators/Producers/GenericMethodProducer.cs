using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace MintPlayer.SourceGenerators.Producers
{
    public class GenericMethodProducer : Producer
    {
        public GenericMethodProducer(Models.GenericMethodDeclaration method, string rootNamespace) : base(rootNamespace)
        {
            Method = method;
        }

        public GenericMethodDeclaration Method { get; }

        protected override ProducedSource? ProduceSource(CancellationToken cancellationToken)
        {
            if (Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword) && Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword))
            {
                var source = new StringBuilder();
                source.AppendLine(Header);
                source.AppendLine();
                source.AppendLine($"namespace {RootNamespace};");

                source.Append("public ");
                if (Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) source.Append("static ");
                source.Append("partial ");
                source.Append($"class {Method.Method.ClassName}");
                source.AppendLine();

                source.AppendLine("{");

                for (int i = 1; i < Method.Count + 1; i++)
                {
                    source.Append("    public ");
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) source.Append("static ");
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) source.Append("partial ");
                    source.Append($"void {Method.Method.MethodName}<");
                    source.Append(string.Join(", ", Enumerable.Range(1, i).Select(i => $"T{i}")));
                    source.Append(">(");

                    source.Append(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => new { Type = $"T{i}", Name = $"t{i}" })
                        .Select(i => $"{i.Type} {i.Name}")));
                    source.Append(")");
                    source.AppendLine();

                    source.AppendLine("    {");
                    if (Method.Method.MethodModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) || Method.Method.ClassModifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
                        source.Append($"        {Method.Method.ClassName}.{Method.Method.MethodName}([");
                    else
                        source.Append($"        this.{Method.Method.MethodName}([");
                    source.Append(string.Join(", ", Enumerable.Range(1, i)
                        .Select(i => $"t{i}")));
                    source.Append("]);");
                    source.AppendLine();
                    source.AppendLine("    }");
                }

                source.AppendLine("}");

                var sourceText = source.ToString();
                var fileName = $"GenericMethods.g.cs";

                return new ProducedSource { FileName = fileName, Source = sourceText };
            }
            else
            {
                return null;
            }
        }
    }
}
