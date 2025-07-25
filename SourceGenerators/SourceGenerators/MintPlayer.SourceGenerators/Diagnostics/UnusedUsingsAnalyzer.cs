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
        var cancellationToken = context.CancellationToken;

        // Get all required namespaces that are used in the file.
        var usedUsings = semanticModel.SyntaxTree
            .GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol?.ContainingNamespace?.ToDisplayString())
            .Where(ns => ns != null)
            .ToImmutableHashSet();

        if (semanticModel.SyntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax root)
            return;

        // Iterate through all using directives in the file.
        foreach (var usingDirective in root.Usings)
        {
            // We only want to check simple namespace usings (e.g., `using System;`).
            // We'll ignore static usings (`using static System.Console;`) and aliased usings (`using S = System;`).
            if (usingDirective.StaticKeyword.IsKind(SyntaxKind.GlobalKeyword) || usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) || usingDirective.Alias != null)
                continue;

            if (semanticModel.GetDeclaredSymbol(usingDirective, cancellationToken) is not INamespaceSymbol usingSymbol)
                continue;

            var isUsed = usedUsings.Contains(usingSymbol.Name);

            // If a using directive is not in the set of used usings provided by the semantic model,
            // then it is considered unused.
            if (!isUsed)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticRules.UnusedUsingsRule,
                    usingDirective.GetLocation(),
                    usingSymbol.Name));
            }
        }
    }
}
