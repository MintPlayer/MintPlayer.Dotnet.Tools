using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Producers;

public sealed class TreeValueComparerProducer : Producer
{
    private readonly IEnumerable<ClassDeclaration> classDeclarations;
    private readonly IEnumerable<TypeTreeDeclaration> treeDeclarations;
    private readonly IEnumerable<ClassDeclaration> childrenWithoutChildren;
    private readonly string comparerType;
    private readonly string comparerAttributeType;
    private readonly bool hasCodeAnalysisReference;
    public TreeValueComparerProducer(IEnumerable<ClassDeclaration> classDeclarations, IEnumerable<TypeTreeDeclaration> treeDeclarations, IEnumerable<ClassDeclaration> childrenWithoutChildren, string rootNamespace, string comparerType, string comparerAttributeType, bool hasCodeAnalysisReference) : base(rootNamespace, $"TreeValueComparers.g.cs")
    {
        this.classDeclarations = classDeclarations;
        this.treeDeclarations = treeDeclarations;
        this.childrenWithoutChildren = childrenWithoutChildren;
        this.comparerType = comparerType;
        this.comparerAttributeType = comparerAttributeType;
        this.hasCodeAnalysisReference = hasCodeAnalysisReference;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        var treeGrouped = treeDeclarations.Where(d => d.BaseType.IsPartial)
            .Select(bt => new
            {
                bt.BaseType.Name,
                bt.BaseType.FullName,
                bt.BaseType.PathSpec,
                bt.BaseType.IsAbstract,
                bt.BaseType.IsInternal,
                bt.DerivedTypes,
                bt.BaseType.Properties,
                bt.BaseType.AllProperties,
            })
            .Concat(classDeclarations.Where(cd => !treeDeclarations.Any(td => td.BaseType.FullName == cd.FullName))
                .Where(d => d.IsPartial)
                .Select(d => new
                {
                    d.Name,
                    d.FullName,
                    d.PathSpec,
                    IsAbstract = d.IsAbstract,
                    d.IsInternal,
                    DerivedTypes = Array.Empty<DerivedType>(),
                    d.Properties,
                    d.AllProperties,
                }
            ))
            .Concat(childrenWithoutChildren
                .Where(d => d.IsPartial)
                .Select(d => new
                {
                    d.Name,
                    d.FullName,
                    d.PathSpec,
                    d.IsAbstract,
                    d.IsInternal,
                    DerivedTypes = Array.Empty<DerivedType>(),
                    d.Properties,
                    d.AllProperties,
                }
            ))
            .GroupBy(d => d.PathSpec?.ContainingNamespace ?? RootNamespace)
            .Select(ns => new
            {
                Namespace = ns.Key,
                Types = ns.ToArray(),
            });


        foreach (var namespaceGrouping in treeGrouped)
        {
            using (writer.OpenBlock($"namespace {namespaceGrouping.Namespace}"))
            {
                foreach (var baseType in namespaceGrouping.Types)
                {
                    var mod = baseType.IsInternal ? "internal" : "public";

                    // Open parent partial class blocks (kept open until end of this baseType generation)
                    var parentBlocks = new Stack<IDisposableWriterIndent>();
                    if (baseType.PathSpec is { } pathSpec && pathSpec.Parents.Any())
                    {
                        foreach (var parentType in pathSpec.Parents.AsEnumerable().Reverse())
                        {
                            parentBlocks.Push(writer.OpenBlock($"partial class {parentType.Name}"));
                        }
                    }

                    // Empty partial for the actual type itself (attribute applied here)
                    writer.WriteLine($"[{comparerAttributeType}(typeof({baseType.Name}ValueComparer))]");
                    using (writer.OpenBlock($"partial class {baseType.Name}"))
                    {
                        // intentionally left empty
                    }
                    writer.WriteLine();

                    // ValueComparer implementation
                    using (writer.OpenBlock($"{mod} sealed class {baseType.Name}ValueComparer : {comparerType}<{baseType.FullName}>"))
                    {
                        writer.WriteLine($"public static readonly {baseType.FullName}ValueComparer Instance = new {baseType.FullName}ValueComparer();");

                        using (writer.OpenBlock($"protected override bool AreEqual({baseType.FullName} x, {baseType.FullName} y)"))
                        {
                            if (!baseType.IsAbstract)
                            {
                                foreach (var prop in baseType.AllProperties.Where(p => !p.HasComparerIgnore))
                                    writer.WriteLine($"if (!IsEquals(x.{prop.Name}, y.{prop.Name})) return false;");
                            }

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
                        }

                        using (writer.OpenBlock($"protected override void AddHash(ref global::MintPlayer.SourceGenerators.Tools.Polyfills.HashCodeCompat h, {baseType.FullName} obj)"))
                        {
                            foreach (var prop in baseType.AllProperties)
                            {
                                writer.WriteLine($"{comparerType}<{baseType.FullName}>.AddHash(ref h, obj.{prop.Name});");
                            }
                        }
                    }
                    writer.WriteLine();

                    // Close parent blocks
                    while (parentBlocks.Count > 0)
                    {
                        parentBlocks.Pop().Dispose();
                    }
                }
            }
        }

        if (hasCodeAnalysisReference)
        {
            using (writer.OpenBlock($"namespace {RootNamespace}"))
            {
                using (writer.OpenBlock($"public static class ValueComparerExtensions"))
                {
                    foreach (var type in treeGrouped.SelectMany(g => g.Types))
                    {
                        var mod = type.IsInternal ? "internal" : "public";

                        using (writer.OpenBlock($"{mod} static global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}> WithComparer(this global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}> provider)"))
                            writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, global::MintPlayer.SourceGenerators.Tools.ValueComparers.ComparerRegistry.For<{type.FullName}>());");

                        using (writer.OpenBlock($"{mod} static global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}> WithComparer(this global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}> provider)"))
                            writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, global::MintPlayer.SourceGenerators.Tools.ValueComparers.ComparerRegistry.For<{type.FullName}>());");

                        using (writer.OpenBlock($"{mod} static global::Microsoft.CodeAnalysis.IncrementalValueProvider<global::System.Collections.Immutable.ImmutableArray<{type.FullName}>> WithComparer(this global::Microsoft.CodeAnalysis.IncrementalValueProvider<global::System.Collections.Immutable.ImmutableArray<{type.FullName}>> provider)"))
                            writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, global::MintPlayer.SourceGenerators.Tools.ValueComparers.ComparerRegistry.For<global::System.Collections.Immutable.ImmutableArray<{type.FullName}>>());");

                        using (writer.OpenBlock($"{mod} static global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}?> WithNullableComparer(this global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}?> provider)"))
                            writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, global::MintPlayer.SourceGenerators.Tools.ValueComparers.ComparerRegistry.For<{type.FullName}?>());");

                        using (writer.OpenBlock($"{mod} static global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}?> WithNullableComparer(this global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}?> provider)"))
                            writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, global::MintPlayer.SourceGenerators.Tools.ValueComparers.ComparerRegistry.For<{type.FullName}?>());");
                    }
                }
            }
        }
    }
}
