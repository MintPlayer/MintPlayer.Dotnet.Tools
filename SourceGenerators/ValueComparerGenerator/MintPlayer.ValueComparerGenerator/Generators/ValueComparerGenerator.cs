using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class ValueComparerGenerator : IncrementalGenerator
{
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
                    if (symbol.BaseType is { Name: not "Object" } baseType)
                    {
                        return new
                        {
                            symbol.Name,
                            Location = symbol.Locations.FirstOrDefault(),
                            Type = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                            HasAttribute = symbol.GetAttributes()
                                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.AutoValueComparerAttribute"))),
                            BaseType = (Models.BaseType?)new Models.BaseType
                            {
                                Name = baseType.Name,
                                FullName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                IsPartial = baseType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ClassDeclarationSyntax baseClassDeclaration && baseClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                Namespace = baseType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                                Properties = baseType.GetMembers().OfType<IPropertySymbol>().Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.ComparerIgnoreAttribute"))),
                                }).ToArray(),
                                HasAttribute = baseType.GetAttributes()
                                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.AutoValueComparerAttribute"))),
                            },
                            Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            Properties = symbol.GetMembers().OfType<IPropertySymbol>().Select(property => new Models.PropertyDeclaration
                            {
                                Name = property.Name,
                                Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.ComparerIgnoreAttribute"))),
                            }).ToArray(),
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
                            HasAttribute = symbol.GetAttributes()
                                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.AutoValueComparerAttribute"))),
                            BaseType = (Models.BaseType?)null,
                            Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            Properties = symbol.GetMembers().OfType<IPropertySymbol>().Select(property => new Models.PropertyDeclaration
                            {
                                Name = property.Name,
                                Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.ComparerIgnoreAttribute"))),
                            }).ToArray(),
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
                    Namespace = t.Namespace,
                    IsPartial = t.IsPartial,
                    Location = t.Location,
                    Properties = t.Properties,
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
                        Namespace = t.Namespace,
                    }).ToArray(),
                })
        );

        //allTypesProvider.Collect().Select(p => p.Except(typeProvider.co).Except(typeTreeProvider));
        var childrenWithoutDerived = allTypesProvider.Collect().Combine(typeProvider).Combine(typeTreeProvider)
            .Select(static (p, ct) => p.Left.Left
                .Where(all => all is { HasAttribute: true })
                .Where(all => !p.Left.Right.Any(x => x.FullName == all?.Type) && !p.Right.Any(x => x.BaseType.FullName == all?.Type))
                .Select(t => new Models.ClassDeclaration
                {
                    Name = t.Name,
                    FullName = t.Type,
                    Namespace = t.Namespace,
                    IsPartial = t.IsPartial,
                    Location = t.Location,
                    Properties = t.Properties,
                    HasAutoValueComparerAttribute = t.HasAttribute,
                }));

        var typeTreeSourceProvider = typeProvider
            .Combine(typeTreeProvider)
            .Combine(childrenWithoutDerived)
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.TreeValueComparerProducer(p.Left.Left.Left.Where(t => t.HasAutoValueComparerAttribute), p.Left.Left.Right, p.Left.Right, p.Right.RootNamespace!, valueComparerType, valueComparerAttributeType));

        context.ProduceCode(typeTreeSourceProvider);
    }

}