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

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        // One block per type: two decorated methods of one type, even in two files, share it.
        var types = Methods
            .Where(m => m.PathSpec is not null && m.MethodName is not null)
            .GroupBy(m => m.PathSpec!);

        var firstType = true;
        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstType) writer.WriteLine();
            firstType = false;

            using (writer.OpenNamespace(type.Key.ContainingNamespace))
            using (writer.OpenPathSpec(type.Key, includeTarget: true))
            {
                var firstMethod = true;
                foreach (var method in type)
                {
                    for (var arity = 2; arity <= method.Count; arity++)
                    {
                        if (!firstMethod) writer.WriteLine();
                        firstMethod = false;
                        WriteOverload(writer, method, arity);
                    }
                }
            }
        }
    }

    private static void WriteOverload(IndentedTextWriter writer, Models.GenericMethodDeclaration method, int arity)
    {
        var indices = Enumerable.Range(1, arity).ToArray();
        var typeParameters = indices.Select(i => $"{method.TypeParameterPrefix}{i}").ToArray();
        var arguments = string.Join(", ", indices.Select(i => $"t{i}"));

        // A summary rather than <inheritdoc cref> to the decorated method: that method is private, takes one collection
        // instead of these parameters, and a cref to a name these overloads share is ambiguous (CS0419).
        writer.WriteLine($"/// <summary>Calls <c>{method.MethodName!.TrimStart('@')}</c> with the {arity} arguments as one collection.</summary>");

        // Never "partial", even when the decorated method is: an overload has another signature, so it can never be
        // that method's implementing half, and a partial one is an implementation with no definition (CS0759).
        var parameters = string.Join(", ", indices.Select(i => $"{typeParameters[i - 1]} t{i}"));
        writer.WriteLine($"public {(method.IsStatic ? "static " : string.Empty)}{method.ReturnType ?? "void"} {method.MethodName}<{string.Join(", ", typeParameters)}>({parameters})");

        // The decorated method takes non-nullable object elements: without `notnull`, [t1] is CS8601 (G15).
        if (method.ElementsNotNull)
        {
            writer.Indent++;
            foreach (var typeParameter in typeParameters)
                writer.WriteLine($"where {typeParameter} : notnull");
            writer.Indent--;
        }

        using (writer.OpenBlock(string.Empty))
        {
            // Unqualified for a static method: `Type.M` would name the open generic type wrongly for `Foo<T>` (G5). Only
            // the decorated method takes one argument, since every overload takes two or more.
            var call = method.IsStatic ? $"{method.MethodName}([{arguments}])" : $"this.{method.MethodName}([{arguments}])";
            writer.WriteLine(method.ReturnType is null ? $"{call};" : $"return {call};");
        }
    }
}
