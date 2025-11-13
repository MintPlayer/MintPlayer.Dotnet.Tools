using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class DescriptionSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> valueComparerCacheProvider)
    {
        var xmlCommentProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) => node is ClassDeclarationSyntax
                                      or RecordDeclarationSyntax
                                      or StructDeclarationSyntax
                                      or EnumDeclarationSyntax
                                      or ConstructorDeclarationSyntax
                                      or PropertyDeclarationSyntax
                                      or EventDeclarationSyntax,
            static (ctx, ct) =>
            {
                if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is INamedTypeSymbol symbol)
                {
                    var xml = symbol?.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: ct);
                    if (string.IsNullOrWhiteSpace(xml)) return default;

                    return new Models.SymbolWithMarkups
                    {
                        TypeName = symbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        PathSpec = symbol.GetPathSpec(ct),
                        MarkupText = xml,
                    };
                }

                return default;
            })
            .WithNullableComparer()
            .Where(static item => item is { MarkupText: not null, TypeName: not null, PathSpec: not null })
            .Collect();

        var xmlCommentSourceProvider = xmlCommentProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new DescriptionsProducer(p.Item1, p.Item2.RootNamespace!));

        context.ProduceCode(xmlCommentSourceProvider);
    }
}
