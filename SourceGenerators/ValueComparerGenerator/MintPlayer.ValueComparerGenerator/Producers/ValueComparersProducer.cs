using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;

namespace MintPlayer.ValueComparerGenerator.Producers;

public class ValueComparersProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<ClassDeclaration> declarations;
    private readonly DiagnosticDescriptor classShouldBePartial = new(
        "ValueComparersProducer",
        "ValueComparersProducer",
        "Class {0} is not partial",
        "ValueComparerGenerator",
        DiagnosticSeverity.Error, 
        true
    );

    public ValueComparersProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, $"ValueComparers.g.cs")
    {
        this.declarations = declarations;
    }

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        foreach (var declaration in declarations.Where(d => !d.IsPartial))
        {
            yield return Diagnostic.Create(classShouldBePartial, declaration.Location, declaration.FullName);
        }
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);

        foreach (var nsGrouping in declarations.Where(d => d.IsPartial).GroupBy(d => d.Namespace))
        {
            writer.WriteLine($"namespace {nsGrouping.Key}");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var cls in nsGrouping)
            {
                writer.WriteLine($"[{cls.ComparerAttributeType}(typeof({cls.Name}Comparer))]");
                writer.WriteLine($"partial class {cls.Name}");
                writer.WriteLine("{");
                writer.WriteLine("}");

                writer.WriteLine();

                // TODO: Use same access modifiers
                writer.WriteLine($"public sealed class {cls.Name}Comparer : {cls.ComparerType}<{cls.FullName}>");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"protected override bool AreEqual({cls.FullName} x, {cls.FullName} y)");
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var prop in cls.Properties.Where(p => !p.HasComparerIgnore))
                {
                    writer.WriteLine($"if (!IsEquals(x.{prop.Name}, y.{prop.Name})) return false;");
                }

                writer.WriteLine("return true;");

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
