using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;

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
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.TargetNode, ct) is INamedTypeSymbol typeSymbol)
                    {
                        return new Models.TypeToMap
                        {
                            DeclaredType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            MappingType = ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute")
                                is { } attribute &&
                                attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol mapType
                                ? mapType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                : string.Empty
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
