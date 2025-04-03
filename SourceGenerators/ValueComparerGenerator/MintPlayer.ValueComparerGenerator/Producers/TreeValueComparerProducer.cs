using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Producers;

public sealed class TreeValueComparerProducer : Producer
{
    private readonly IEnumerable<TypeTreeDeclaration> declarations;
    private readonly string comparerType;
    private readonly string comparerAttributeType;
    public TreeValueComparerProducer(IEnumerable<TypeTreeDeclaration> declarations, string rootNamespace, string comparerType, string comparerAttributeType) : base(rootNamespace, $"TreeValueComparers.g.cs")
    {
        this.declarations = declarations;
        this.comparerType = comparerType;
        this.comparerAttributeType = comparerAttributeType;
    }


    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);

        var grouped = declarations.Where(d => d.BaseType.IsPartial)
            .GroupBy(d => d.BaseType.Namespace)
            .Select(g => new
            {
                Namespace = g.Key,
                Types = g
                    .ToArray()
                    .GroupBy(d => new { d.BaseType.Name, d.BaseType.FullName, d.BaseType.Namespace })
                    .Select(g => new
                    {
                        g.Key.Name,
                        g.Key.FullName,
                        g.Key.Namespace,
                        Types = g.SelectMany(d => d.DerivedTypes).ToArray(),
                        g.First().BaseType.Properties,
                    })
            });

        foreach (var namespaceGrouping in grouped)
        {
            writer.WriteLine($"namespace {namespaceGrouping.Namespace}");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var baseType in namespaceGrouping.Types)
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

                writer.WriteLine("return (x, y) switch");
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var derivedType in baseType.Types)
                {
                    writer.WriteLine($"({derivedType.Type} x, {derivedType.Type} y) => IsEquals(x, y),");
                }
                writer.WriteLine("_ => false,");
                writer.Indent--;
                writer.WriteLine("};");

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
