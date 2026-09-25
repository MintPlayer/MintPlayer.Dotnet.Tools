using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

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
            if (method.Method.ClassIsPartial && method.Method.MethodIsPrivate)
            {
                writer.WriteLine(Header);
                writer.WriteLine();
                var ns = method.Method.PathSpec?.ContainingNamespace ?? method.Method.ContainingNamespace ?? RootNamespace;
                IDisposableWriterIndent? namespaceBlock = string.IsNullOrEmpty(ns) ? null : writer.OpenBlock($"namespace {ns}");

                using (writer.OpenPathSpec(method.Method.PathSpec))
                {
                    writer.Write("public ");
                    if (method.Method.ClassIsStatic) writer.Write("static ");
                    writer.Write("partial ");
                    writer.Write($"class {method.Method.ClassName}");
                    writer.WriteLine();

                    using (writer.OpenBlock(string.Empty))
                    {
                        for (int i = 1; i < method.Count + 1; i++)
                        {
                            // A summary rather than <inheritdoc cref> to the decorated method: that method is
                            // private, takes one collection instead of these parameters, and a cref to a name
                            // these overloads share is ambiguous (CS0419).
                            writer.WriteLine(i == 1
                                ? $"/// <summary>Calls <c>{method.Method.MethodName}</c> with the argument as a one-element collection.</summary>"
                                : $"/// <summary>Calls <c>{method.Method.MethodName}</c> with the {i} arguments as one collection.</summary>");
                            writer.Write("public ");
                            if (method.Method.MethodIsStatic) writer.Write("static ");
                            // Never "partial", even when the decorated method is: an overload has
                            // another signature, so it can never be that method's implementing
                            // half, and a partial one is an implementation with no definition (CS0759).
                            writer.Write($"void {method.Method.MethodName}<");
                            writer.Write(string.Join(", ", Enumerable.Range(1, i).Select(i => $"T{i}")));
                            writer.Write(">( ");

                            writer.Write(string.Join(", ", Enumerable.Range(1, i)
                                .Select(i => new { Type = $"T{i}", Name = $"t{i}" })
                                .Select(i => $"{i.Type} {i.Name}")));
                            writer.Write(")");
                            writer.WriteLine();

                            using (writer.OpenBlock(string.Empty))
                            {
                                if (method.Method.MethodIsStatic || method.Method.ClassIsStatic)
                                    writer.Write($"{method.Method.ClassName}.{method.Method.MethodName}([");
                                else
                                    writer.Write($"this.{method.Method.MethodName}([");
                                writer.Write(string.Join(", ", Enumerable.Range(1, i).Select(i => $"t{i}")));
                                writer.Write("]);\n");
                            }
                        }
                    }
                }

                namespaceBlock?.Dispose();
            }
        }
    }
}
