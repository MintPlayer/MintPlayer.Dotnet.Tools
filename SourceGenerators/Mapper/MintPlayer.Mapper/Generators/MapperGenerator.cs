using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;

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
                            ...
                        };
                    }
                    return null;
                }
            )
            .WithComparer();

        var typesToMapSourceProvider = typesToMapProvider
            .Select(static Producer (p, ct) => new MapperProducer(p));

        context.ProduceCode(typesToMapSourceProvider);
    }

    public override void RegisterComparers()
    {
    }
}
