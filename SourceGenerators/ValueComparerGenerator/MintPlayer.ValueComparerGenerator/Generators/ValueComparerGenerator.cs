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
                        const string valueComparerType = "global::MintPlayer.SourceGenerators.Tools.ValueComparers.ValueComparer";
                        // ValueComparerAttribute is within the Tools project, AutoValueComparerAttribute is within the ValueComparerGenerator project
                        const string valueComparerAttributeType = "global::MintPlayer.SourceGenerators.Tools.ValueComparerAttribute";

                        var attr = symbol.GetAttributes()
                            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, autoValueComparerAttribute));

                        if (attr is not null)
                        {
                            return new Models.ClassDeclaration
                            {
                                Name = symbol.Name,
                                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                Namespace = symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
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



        var comparerSourceProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.ValueComparersProducer(declarations: p.Left.OfType<Models.ClassDeclaration>(), rootNamespace: p.Right.RootNamespace!));

        context.ProduceCode(comparerSourceProvider);
    }
}
