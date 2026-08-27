using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0002: a call to <c>Should()</c> (declared in the MintPlayer.Assertions namespace) whose
/// result is discarded — the Should() invocation itself is the whole expression statement, so no
/// assertion method is ever called on it and nothing is verified.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class VacuousShouldAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.VacuousShouldRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;

        // Only the shape `<expr>.Should(...);` — the Should() call is the whole statement.
        if (statement.Expression is not InvocationExpressionSyntax invocation)
            return;

        // Cheap syntactic pre-filter before touching the semantic model.
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
            _ => null,
        };
        if (name != "Should")
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "Should" || !SymbolHelpers.IsInAssertionsNamespace(method.ContainingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.VacuousShouldRule, invocation.GetLocation()));
    }
}
