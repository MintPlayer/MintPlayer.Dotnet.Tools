using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Producers;

public sealed class TreeValueComparerProducer : Producer
{
    private readonly IEnumerable<ClassDeclaration> classDeclarations;
    private readonly IEnumerable<TypeTreeDeclaration> treeDeclarations;
    private readonly IEnumerable<ClassDeclaration> childrenWithoutChildren;
    private readonly Settings settings;
    private readonly string comparerType;
    private readonly string comparerAttributeType;
    public TreeValueComparerProducer(IEnumerable<ClassDeclaration> classDeclarations, IEnumerable<TypeTreeDeclaration> treeDeclarations, IEnumerable<ClassDeclaration> childrenWithoutChildren, Settings settings, string comparerType, string comparerAttributeType) : base(settings.RootNamespace!, $"TreeValueComparers.g.cs")
    {
        this.classDeclarations = classDeclarations;
        this.treeDeclarations = treeDeclarations;
        this.childrenWithoutChildren = childrenWithoutChildren;
        this.settings = settings;
        this.comparerType = comparerType;
        this.comparerAttributeType = comparerAttributeType;
    }


    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);

        var treeGrouped = treeDeclarations.Where(d => d.BaseType.IsPartial)
            .Select(bt => new
            {
                bt.BaseType.Name,
                bt.BaseType.FullName,
                bt.BaseType.Namespace,
                bt.DerivedTypes,
                bt.BaseType.Properties,
            })
            .Concat(classDeclarations.Where(cd => !treeDeclarations.Any(td => td.BaseType.FullName == cd.FullName))
                .Where(d => d.IsPartial)
                .Select(d => new
                {
                    d.Name,
                    d.FullName,
                    d.Namespace,
                    DerivedTypes = Array.Empty<DerivedType>(),
                    d.Properties,
                }
            ))
            .Concat(childrenWithoutChildren
                .Where(d => d.IsPartial)
                .Select(d => new
                {
                    d.Name,
                    d.FullName,
                    d.Namespace,
                    DerivedTypes = Array.Empty<DerivedType>(),
                    d.Properties,
                }
            ))
            .GroupBy(d => d.Namespace);

        writer.WriteLine($"using Microsoft.CodeAnalysis;");
        writer.WriteLine();

        var ivp = settings.IncrementalValueProviderSymbol;
        foreach (var namespaceGrouping in treeGrouped)
        {
            writer.WriteLine($"namespace {namespaceGrouping.Key}");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"public static class ValueComparerExtensions");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var baseType in namespaceGrouping)
            {
                writer.WriteLine($"public static {ivp}<{baseType.Name}> WithComparer(this {ivp}<{baseType.Name}> source)");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"return source.WithComparer({baseType.Name}ValueComparer.Instance);");

                writer.Indent--;
                writer.WriteLine("}");
            }

            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine();

            foreach (var baseType in namespaceGrouping)
            {
                writer.WriteLine($"[{comparerAttributeType}(typeof({baseType.Name}ValueComparer))]");
                writer.WriteLine($"partial class {baseType.Name}");
                writer.WriteLine("{");
                writer.WriteLine("}");
                writer.WriteLine();


                // TODO: Use same access modifiers
                writer.WriteLine($"public sealed class {baseType.Name}ValueComparer : {comparerType}<{baseType.FullName}>");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"protected override bool AreEqual({baseType.FullName} x, {baseType.FullName} y)");
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var prop in baseType.Properties.Where(p => !p.HasComparerIgnore))
                {
                    writer.WriteLine($"if (!IsEquals(x.{prop.Name}, y.{prop.Name})) return false;");
                }

                // TODO: call base class comparer instead of false
                if (baseType.DerivedTypes.Length > 0)
                {
                    writer.WriteLine("return (x, y) switch");
                    writer.WriteLine("{");
                    writer.Indent++;
                    foreach (var derivedType in baseType.DerivedTypes)
                    {
                        writer.WriteLine($"({derivedType.Type} a, {derivedType.Type} b) => IsEquals(a, b),");
                    }
                    writer.WriteLine("_ => false,");
                    writer.Indent--;
                    writer.WriteLine("};");
                }
                else
                {
                    writer.WriteLine("return true;");
                }

                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");
                writer.WriteLine();
            }
            writer.Indent--;
            writer.WriteLine("}");
        }
    }
}
