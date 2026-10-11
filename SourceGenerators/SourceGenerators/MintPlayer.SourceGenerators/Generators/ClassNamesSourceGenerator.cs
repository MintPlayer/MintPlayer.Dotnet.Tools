using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ClassNamesSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var classDeclarationsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax,
                static (context, ct) =>
                {
                    if (context.Node is ClassDeclarationSyntax classDeclaration &&
                        context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
                    {
                        return new Models.ClassDeclaration
                        {
                            Name = symbol.Name,
                            TopLevelNamespace = symbol.ContainingType is not null
                                ? null
                                : symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
                        };
                    }
                    else
                    {
                        return default;
                    }
                }
            )
            .Collect();

        var classNamesSourceProvider = classDeclarationsProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new ClassNamesProducer(declarations: p.Item1.NotNull(), rootNamespace: p.Item2.RootNamespace!));

        var classNameListSourceProvider = classDeclarationsProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new ClassNameListProducer(declarations: p.Item1.NotNull(), rootNamespace: p.Item2.RootNamespace!));

        context.ProduceCode(classNamesSourceProvider, classNameListSourceProvider);
    }
}
