using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;
using System.Linq;
using System.Xml.Linq;

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

        foreach (var baseTypeGrouping in declarations.Where(d => d.BaseType.IsPartial).GroupBy(d => new { d.BaseType.Name, d.BaseType.FullName, d.BaseType.Namespace, d.BaseType.IsPartial })
            .Select(n => new { n.Key.Name, n.Key.FullName, n.Key.Namespace, n.Key.IsPartial, Types = n.SelectMany(n => n.DerivedTypes).ToArray() }))
        {
            writer.WriteLine($"namespace {baseTypeGrouping.Namespace}");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"[{comparerAttributeType}(typeof({baseTypeGrouping.Name}ValueComparer))]");
            writer.WriteLine($"partial class {baseTypeGrouping.Name}");
            writer.WriteLine("{");
            writer.WriteLine("}");

            writer.WriteLine();

            // TODO: Use same access modifiers
            writer.WriteLine($"public sealed class {baseTypeGrouping.Name}ValueComparer : {comparerType}<{baseTypeGrouping.FullName}>");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"protected override bool AreEqual({baseTypeGrouping.FullName} x, {baseTypeGrouping.FullName} y)");
            writer.WriteLine("{");
            writer.Indent++;

            //foreach (var prop in baseTypeGrouping.BaseType.Properties.Where(p => !p.HasComparerIgnore))
            //{
            //    writer.WriteLine($"if (!IsEquals(x.{prop.Name}, y.{prop.Name})) return false;");
            //}

            writer.WriteLine("return (x, y) switch");
            writer.WriteLine("{");
            writer.Indent++;
            foreach (var derivedType in baseTypeGrouping.Types)
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

            writer.Indent--;
            writer.WriteLine("}");
        }


        //foreach (var nsGrouping in declarations.GroupBy(d => d.Namespace))
        //{
        //    writer.WriteLine($"namespace {nsGrouping.Key}");
        //    writer.WriteLine("{");
        //    writer.Indent++;

        //    foreach (var cls in nsGrouping.Where(t => t.IsBaseTypePartial))
        //    {
        //        writer.WriteLine($"[{cls.ComparerAttributeType}(typeof({cls.BaseTypeName}Comparer))]");
        //        writer.WriteLine($"partial class {cls.BaseTypeName}");
        //        writer.WriteLine("{");
        //        writer.WriteLine("}");

        //        writer.WriteLine();

        //        // TODO: Use same access modifiers
        //        writer.WriteLine($"public sealed class {cls.BaseTypeName}Comparer : {cls.ComparerType}<{cls.BaseType}>");
        //        writer.WriteLine("{");
        //        writer.Indent++;

        //        writer.WriteLine($"protected override bool AreEqual({cls.BaseType} x, {cls.BaseType} y)");
        //        writer.WriteLine("{");
        //        writer.Indent++;

        //        foreach (var prop in cls.Properties.Where(p => !p.HasComparerIgnore))
        //        {
        //            writer.WriteLine($"if (!IsEquals(x.{prop.Name}, y.{prop.Name})) return false;");
        //        }

        //        writer.WriteLine("return (x, y) switch");
        //        writer.WriteLine("{");
        //        writer.Indent++;
        //        foreach (var derivedType in cls.DerivedTypes)
        //        {
        //            writer.WriteLine($"({derivedType} x, {derivedType} y) => IsEquals(x, y),");
        //        }
        //        writer.WriteLine("_ => false,");
        //        writer.Indent--;
        //        writer.WriteLine("};");

        //        writer.Indent--;
        //        writer.WriteLine("}");

        //        writer.Indent--;
        //        writer.WriteLine("}");
        //        writer.WriteLine();
        //    }

        //    writer.Indent--;
        //    writer.WriteLine("}");
        //}
    }
}
