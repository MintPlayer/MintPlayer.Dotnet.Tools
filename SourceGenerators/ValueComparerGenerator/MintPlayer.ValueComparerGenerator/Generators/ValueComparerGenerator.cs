using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public class ValueComparerGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        const string valueComparerType = "global::MintPlayer.SourceGenerators.Tools.ValueComparers.ValueComparer";
        // ValueComparerAttribute is within the Tools project, AutoValueComparerAttribute is within the ValueComparerGenerator project
        const string valueComparerAttributeType = "global::MintPlayer.SourceGenerators.Tools.ValueComparerAttribute";

        var classDeclarationsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) =>
                {
                    return node is ClassDeclarationSyntax { } classDeclaration;
                },
                static (context, ct) =>
                {
                    if (context.Node is ClassDeclarationSyntax classDeclaration &&
                        context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
                    {
                        var autoValueComparerAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.AutoValueComparerAttribute");

                        var attr = symbol.GetAttributes()
                            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, autoValueComparerAttribute));

                        if (attr is not null)
                        {
                            return new Models.ClassDeclaration
                            {
                                Name = symbol.Name,
                                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                                IsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                ComparerType = valueComparerType,
                                ComparerAttributeType = valueComparerAttributeType,
                                Properties = symbol.GetMembers().OfType<IPropertySymbol>().Select(property => new Models.PropertyDeclaration
                                {
                                    Name = property.Name,
                                    Type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    HasComparerIgnore = property.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, context.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.ValueComparerGenerator.Attributes.ComparerIgnoreAttribute"))),
                                }).ToArray(),
                                Location = symbol.Locations.FirstOrDefault(),
                            };
                        }
                    }

                    return default;
                }
            )
            .WithComparer(ClassDeclarationValueComparer.Instance)
            .Collect();

        var allTypesProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) =>
            {
                return node is ClassDeclarationSyntax { } classDeclaration;
            },
            static (context, ct) =>
            {
                if (context.Node is ClassDeclarationSyntax classDeclaration &&
                    context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol &&
                    symbol.BaseType is { } baseType)
                {
                    return new
                    {
                        symbol.Name,
                        Type = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BaseType = new Models.BaseType
                        {
                            Name = baseType.Name,
                            FullName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsPartial = baseType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ClassDeclarationSyntax baseClassDeclaration && baseClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                            Namespace = baseType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
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
                return default;
            }
        );

        var typeTreeProvider = allTypesProvider
            .Collect()
            .Select((allTypes, ct) => allTypes
                .NotNull()
                .GroupBy(t => t.BaseType)
                .Select(g => new Models.TypeTreeDeclaration
                {
                    BaseType = g.Key,
                    DerivedTypes = g.Select(t => new Models.DerivedType
                    {
                        Type = t.Type,
                        Name = t.Name,
                        Namespace = t.Namespace,
                    }).ToArray(),
                })
                .Where(t => t.BaseType.FullName != "Object")
        );

        var comparerSourceProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.ValueComparersProducer(declarations: p.Left.OfType<Models.ClassDeclaration>(), rootNamespace: p.Right.RootNamespace!));

        var typeTreeSourceProvider = typeTreeProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.TreeValueComparerProducer(p.Left, p.Right.RootNamespace!, valueComparerType, valueComparerAttributeType));

        context.ProduceCode(comparerSourceProvider, typeTreeSourceProvider);
    }
}
