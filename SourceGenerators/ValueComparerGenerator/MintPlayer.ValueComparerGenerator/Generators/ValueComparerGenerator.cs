using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class ValueComparerGenerator : IncrementalGenerator
{
    public override void RegisterComparers() { }

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        const string valueComparerType = "global::MintPlayer.SourceGenerators.Tools.ValueComparers.ValueComparer";
        // ValueComparerAttribute is within the Tools project, AutoValueComparerAttribute is within the ValueComparerGenerator project
        const string valueComparerAttributeType = "global::MintPlayer.SourceGenerators.Tools.ValueComparerAttribute";

        // This provider retrieves all types with and without a base-type and have the AutoValueComparerAttribute
        var allTypesProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) =>
            {
                return node is ClassDeclarationSyntax { } classDeclaration;
            },
            static (context, ct) =>
            {
                // AutoValueComparerAttribute should be applied to the base type, because the base type must be partial
                if (context.Node is ClassDeclarationSyntax classDeclaration &&
                    context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
                {
                    var comparerIgnoreAttr = context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.ComparerIgnoreAttribute");
                    var autoValueComparerAttr = context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.AutoValueComparerAttribute");
                    if (symbol.BaseType is { Name: not "Object" } baseType)
                    {
                        return new
                        {
                            symbol.Name,
                            Location = symbol.Locations.FirstOrDefault(),
                            Type = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                            IsAbstract = symbol.IsAbstract,
                            HasAttribute = symbol.GetAttributes()
                                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, autoValueComparerAttr)),
                            BaseType = (Models.BaseType?)new Models.BaseType
                            {
                                Name = baseType.Name,
                                FullName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                IsAbstract = baseType.IsAbstract,
                                IsPartial = baseType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ClassDeclarationSyntax baseClassDeclaration && baseClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                // TODO: replace "Namespace" with "BaseTypePathspec"
                                PathSpec = baseType.GetPathSpec(ct),
                                Properties = baseType.GetMembers().OfType<IPropertySymbol>().Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                }).ToArray(),
                                AllProperties = baseType.GetAllProperties()
                                    .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic)
                                    .Select(property => new Models.PropertyDeclaration
                                    {
                                        Name = property.Name,
                                        Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                        HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                    })
                                    .ToArray() ?? [],
                                HasAttribute = baseType.GetAttributes()
                                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, autoValueComparerAttr)),
                            },
                            AllProperties = symbol.GetAllProperties()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic)
                                .Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                })
                                .ToArray() ?? [],
                            //Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            PathSpec = symbol.GetPathSpec(ct),
                            Properties = symbol.GetMembers().OfType<IPropertySymbol>()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared)
                                .Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                })
                                .ToArray(),
                            HasCodeAnalysisReference = context.SemanticModel.Compilation.ReferencedAssemblyNames
                                .Any(a => a.Name == "Microsoft.CodeAnalysis"),
                        };
                    }
                    else
                    {
                        return new
                        {
                            symbol.Name,
                            Location = symbol.Locations.FirstOrDefault(),
                            Type = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                            IsAbstract = symbol.IsAbstract,
                            HasAttribute = symbol.GetAttributes()
                                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, autoValueComparerAttr)),
                            BaseType = (Models.BaseType?)null,
                            AllProperties = symbol.GetAllProperties()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic)
                                .Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                })
                                .ToArray() ?? [],
                            //Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            PathSpec = symbol.GetPathSpec(ct),
                            Properties = symbol.GetMembers().OfType<IPropertySymbol>()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared)
                                .Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, comparerIgnoreAttr)),
                                }).ToArray(),
                            HasCodeAnalysisReference = context.SemanticModel.Compilation.ReferencedAssemblyNames
                                .Any(a => a.Name == "Microsoft.CodeAnalysis"),
                        };
                    }
                }
                return default;
            }
        );

        // This provider retrieves all types without a base-type
        var typeProvider = allTypesProvider
            .Collect()
            .Select((allTypes, ct) => allTypes
                .NotNull()
                .Where(t => t.IsPartial && (t.BaseType is null || !t.BaseType.IsPartial || !t.BaseType.HasAttribute))
                .Select(t => new Models.ClassDeclaration
                {
                    Name = t.Name,
                    FullName = t.Type,
                    PathSpec = t.PathSpec,
                    IsPartial = t.IsPartial,
                    IsAbstract = t.IsAbstract,
                    Location = t.Location,
                    Properties = t.Properties,
                    AllProperties = t.AllProperties,
                    HasAutoValueComparerAttribute = t.HasAttribute,
                })
        );

        // This provider retrieves all types that have derived types
        var typeTreeProvider = allTypesProvider
            .Collect()
            .Select((allTypes, ct) => allTypes
                .NotNull()
                //.Where(t => t.BaseType.IsPartial && (t.BaseType.FullName != "Object" || t.BaseType.HasAttribute))
                .Where(t => t.BaseType is { IsPartial: true, HasAttribute: true })
                .GroupBy(t => new { t.BaseType!.FullName })
                .Select(g => new Models.TypeTreeDeclaration
                {
                    BaseType = g.First().BaseType!,
                    DerivedTypes = g.Select(t => new Models.DerivedType
                    {
                        Type = t.Type,
                        Name = t.Name,
                        PathSpec = t.PathSpec,
                        AllProperties = t.AllProperties,
                    }).ToArray(),
                })
        );

        //allTypesProvider.Collect().Select(p => p.Except(typeProvider.co).Except(typeTreeProvider));
        var childrenWithoutDerived = allTypesProvider.Collect()
            .Join(typeProvider)
            .Join(typeTreeProvider)
            .Select(static (p, ct) => p.Item1
                .Where(all => all is { HasAttribute: true })
                .Where(all => !p.Item2.Any(x => x.FullName == all?.Type) && !p.Item3.Any(x => x.BaseType.FullName == all?.Type))
                .Select(t => new Models.ClassDeclaration
                {
                    Name = t.Name,
                    FullName = t.Type,
                    PathSpec = t.PathSpec,
                    IsAbstract = t.IsAbstract,
                    IsPartial = t.IsPartial,
                    Location = t.Location,
                    Properties = t.Properties,
                    AllProperties = t.AllProperties,
                    HasAutoValueComparerAttribute = t.HasAttribute,
                }));

        var hasCodeAnalysisReference = allTypesProvider
            .Collect()
            .Select(static (allTypes, ct) => allTypes
                .NotNull()
                .Any(t => t.HasCodeAnalysisReference));

        var typeTreeSourceProvider = typeProvider
            .Join(typeTreeProvider)
            .Join(childrenWithoutDerived)
            .Join(settingsProvider)
            .Join(hasCodeAnalysisReference)
            .Select(static Producer (p, ct) => new Producers.TreeValueComparerProducer(
                p.Item1.Where(t => t.HasAutoValueComparerAttribute),
                p.Item2,
                p.Item3,
                p.Item4.RootNamespace!,
                valueComparerType,
                valueComparerAttributeType,
                p.Item5
            ));

        context.ProduceCode(typeTreeSourceProvider);
    }
}
