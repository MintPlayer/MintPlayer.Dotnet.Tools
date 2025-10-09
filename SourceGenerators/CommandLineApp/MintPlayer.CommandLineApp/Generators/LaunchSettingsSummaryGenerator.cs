using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.CommandLineApp.Generators
{
    [Generator(LanguageNames.CSharp)]
    public sealed class LaunchSettingsSummaryGenerator : IncrementalGenerator
    {
        const string consoleAppAttribute = "MintPlayer.CommandLineApp.Attributes.ConsoleAppAttribute";

        public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
            var filesWithTopLevelStatements = context.CompilationProvider
                .SelectMany(static (compilation, ct) => compilation.SyntaxTrees
                    .Where(st => st.GetRoot(ct).DescendantNodesAndSelf().OfType<GlobalStatementSyntax>().Any())
                    .Select(f => f.GetLocation(TextSpan.FromBounds(0, f.Length - 1))))
                .WithComparer(ValueComparer<Location>.Instance)
                .Collect();

            var consoleAppsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                consoleAppAttribute,
                static (node, ct) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, ct) =>
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
                .Join(filesWithTopLevelStatements)
                .Join(settingsProvider)
                .Select(static Producer (prov, ct) => new LaunchSettingsSummaryProducer(prov.Item1, prov.Item2, prov.Item3.RootNamespace!));

            var consoleAppsDiagnosticProvider = consoleAppsProvider
                .Join(filesWithTopLevelStatements)
                .Join(settingsProvider)
                .Select(static IDiagnosticReporter (prov, ct) => new LaunchSettingsSummaryProducer(prov.Item1, prov.Item2, prov.Item3.RootNamespace!));

            context.ProduceCode(consoleAppsSourceProvider);
            context.ReportDiagnostics(consoleAppsDiagnosticProvider);
        }

        public override void RegisterComparers()
        {
        }
    }
}
