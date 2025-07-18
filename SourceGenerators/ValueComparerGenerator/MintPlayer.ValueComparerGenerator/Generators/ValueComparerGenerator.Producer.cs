﻿using MintPlayer.SourceGenerators.Tools;
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
                    DerivedTypes = Array.Empty<DerivedType>(),
                    d.Properties,
                    d.AllProperties,
                }
            ))
            .GroupBy(d => d.PathSpec.ContainingNamespace)
            .Select(ns => new
            {
                Namespace = ns.Key,
                Types = ns.ToArray(),
            });


        foreach (var namespaceGrouping in treeGrouped)
        {
            writer.WriteLine($"namespace {namespaceGrouping.Namespace}");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var baseType in namespaceGrouping.Types)
            {
                // Nested partial classes for each parent type
                foreach (var parentType in baseType.PathSpec.Parents)
                {
                    writer.WriteLine($"partial class {parentType.Name}");
                    writer.WriteLine("{");
                    writer.Indent++;
                }

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

                // If base-type is abstract, we don't need to compare properties
                // The derived-type comparer will check these properties
                if (!baseType.IsAbstract)
                {
                    foreach (var prop in baseType.AllProperties.Where(p => !p.HasComparerIgnore))
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

                // Nested partial classes for each parent type
                foreach (var parentType in baseType.PathSpec.Parents)
                {
                    writer.Indent--;
                    writer.WriteLine("}");
                }
            }
            writer.Indent--;
            writer.WriteLine("}");
        }

        if (hasCodeAnalysisReference)
        {
            writer.WriteLine($"namespace {RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine($"public static class ValueComparerExtensions");
            writer.WriteLine("{");
            writer.Indent++;
            foreach (var type in treeGrouped.SelectMany(g => g.Types))
            {
                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}> WithComparer(this global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}> provider)");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, {type.FullName}ValueComparer.Instance);");
                writer.Indent--;
                writer.WriteLine("}");

                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}> WithComparer(this global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}> provider)");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, {type.FullName}ValueComparer.Instance);");
                writer.Indent--;
                writer.WriteLine("}");

                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}?> WithNullableComparer(this global::Microsoft.CodeAnalysis.IncrementalValuesProvider<{type.FullName}?> provider)");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, {type.FullName}ValueComparer.Instance);");
                writer.Indent--;
                writer.WriteLine("}");

                writer.WriteLine($"public static global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}?> WithNullableComparer(this global::Microsoft.CodeAnalysis.IncrementalValueProvider<{type.FullName}?> provider)");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return global::Microsoft.CodeAnalysis.IncrementalValueProviderExtensions.WithComparer(provider, {type.FullName}ValueComparer.Instance);");
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
