using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Tools;
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

        var usings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && u.Alias == null)
            .ToList();

        // Get all referenced symbols in the file
        var referencedNamespaces = root
            .DescendantNodes()
            .Select(n => semanticModel.GetSymbolInfo(n, context.CancellationToken).Symbol?.ContainingNamespace)
            .Where(ns => ns != null)
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();

        foreach (var usingDirective in usings.NotNull())
        {
            if (semanticModel.GetSymbolInfo(usingDirective.Name, context.CancellationToken).Symbol is not INamespaceSymbol usingSymbol)
                continue;

            var isUsed = referencedNamespaces.Any(ns => ns != null && (ns.Equals(usingSymbol) || IsChildNamespace(ns, usingSymbol)));

            if (!isUsed)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.UnusedUsingsRule,
                    usingDirective.GetLocation(),
                    usingDirective.Name.ToString()));
            }
        }
    }

    private static bool IsChildNamespace(INamespaceSymbol child, INamespaceSymbol parent)
    {
        while (child != null && !child.IsGlobalNamespace)
        {
            if (SymbolEqualityComparer.Default.Equals(child, parent))
                return true;

            child = child.ContainingNamespace;
        }

        return false;
    }
}
