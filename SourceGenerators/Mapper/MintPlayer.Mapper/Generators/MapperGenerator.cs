using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System;

namespace MintPlayer.Mapper.Generators;

[Generator(LanguageNames.CSharp)]
public class MapperGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var typesToMapProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "MintPlayer.Mapper.Attributes.GenerateMapperAttribute",
                static (node, ct) => node is not null,
                static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.TargetNode, ct) is INamedTypeSymbol typeSymbol &&
                        ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute") is { } attr &&
                        attr.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol mapType)
                    {
                        return new Models.TypeToMap
                        {
                            DestinationNamespace = typeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                            DeclaredType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            MappingType =  mapType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),

                            DeclaredProperties = typeSymbol.GetAllProperties()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic)
                                .Select(p => new Models.PropertyDeclaration
                                {
                                    PropertyName = p.Name,
                                    PropertyType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Alias = p.GetAttributes()
                                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperAliasAttribute")
                                        ?.ConstructorArguments.FirstOrDefault().Value as string,
                                    //IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                                    //IsReadOnly = p.IsReadOnly,
                                    //IsStatic = p.IsStatic,
                                    //IsVirtual = p.IsVirtual,
                                    //IsAbstract = p.IsAbstract,
                                    //IsOverride = p.IsOverride,
                                })
                                .ToArray(),
                            MappingProperties = mapType.GetAllProperties()
                                .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic)
                                .Select(p => new Models.PropertyDeclaration
                                {
                                    PropertyName = p.Name,
                                    PropertyType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Alias = p.GetAttributes()
                                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperAliasAttribute")
                                        ?.ConstructorArguments.FirstOrDefault().Value as string,
                                    //IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                                    //IsReadOnly = p.IsReadOnly,
                                    //IsStatic = p.IsStatic,
                                    //IsVirtual = p.IsVirtual,
                                    //IsAbstract = p.IsAbstract,
                                    //IsOverride = p.IsOverride,
                                })
                                .ToArray(),
                        };
                    }
                    return null;
                }
            )
            .WithNullableComparer()
            .Collect();

        var typesToMapSourceProvider = typesToMapProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new MapperProducer(p.Item1, p.Item2.RootNamespace!));

        context.ProduceCode(typesToMapSourceProvider);
    }

    public override void RegisterComparers()
    {
    }
}
