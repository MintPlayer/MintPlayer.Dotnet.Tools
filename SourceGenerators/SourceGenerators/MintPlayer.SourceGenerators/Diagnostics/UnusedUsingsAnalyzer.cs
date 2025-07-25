using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedUsingsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticRules.UnusedUsingsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        var semanticModel = context.SemanticModel;
        var root = semanticModel.SyntaxTree.GetRoot(context.CancellationToken);

        // Collect all identifiers in the tree (could be used types)
        var allIdentifiers = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .ToList();

        var usings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && u.Alias == null)
            .ToList();

        foreach (var usingDirective in usings)
        {
            var usingSymbol = semanticModel.GetSymbolInfo(usingDirective.Name, context.CancellationToken).Symbol;

            if (usingSymbol == null)
                continue;

            // Check if any identifier resolves to a symbol from the using namespace
            var isUsed = allIdentifiers
                .Select(id => semanticModel.GetSymbolInfo(id, context.CancellationToken).Symbol)
                .Any(sym => sym != null &&
                            sym.ContainingNamespace != null &&
                            SymbolEqualityComparer.Default.Equals(sym.ContainingNamespace, usingSymbol));

            if (!isUsed)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.UnusedUsingsRule,
                    usingDirective.GetLocation(),
                    usingDirective.Name.ToString()));
            }
        }
    }
}
