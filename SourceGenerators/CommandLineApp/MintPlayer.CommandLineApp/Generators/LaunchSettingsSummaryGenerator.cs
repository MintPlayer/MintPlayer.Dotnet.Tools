using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.CommandLineApp.Generators
{
    [Generator(LanguageNames.CSharp)]
    public sealed class LaunchSettingsSummaryGenerator : IncrementalGenerator
    {
        const string consoleAppAttribute = "MintPlayer.CommandLineApp.Attributes.ConsoleAppAttribute";

        public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
            var alreadyHasTopLevelStatements = context.CompilationProvider
                .Select((compilation, ct) => compilation.SyntaxTrees.Any(st => st.GetRoot(ct).DescendantNodesAndSelf().OfType<GlobalStatementSyntax>().Any()))
                .WithDefaultComparer();

            var consoleAppsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                consoleAppAttribute,
                (node, ct) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                (context, ct) =>
                {
                    if (context.TargetNode is ClassDeclarationSyntax classDeclaration &&
                        context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol classSymbol)
                    {
                        return new Models.ConsoleApp
                        {
                            ClassName = classSymbol.Name,
                            Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                        };
                    }

                    return default;
                })
                .WithNullableComparer()
                .Collect();

            var consoleAppsSourceProvider = consoleAppsProvider
                .Join(alreadyHasTopLevelStatements)
                .Join(settingsProvider)
                .Select(Producer (prov, ct) => new LaunchSettingsSummaryProducer(prov.Item1, prov.Item2, prov.Item3.RootNamespace!));

            context.ProduceCode(consoleAppsSourceProvider);
        }

        public override void RegisterComparers()
        {
        }
    }
}
