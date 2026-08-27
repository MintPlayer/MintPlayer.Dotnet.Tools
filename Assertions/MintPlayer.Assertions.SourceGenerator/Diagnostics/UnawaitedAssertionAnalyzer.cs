using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0001: an assertion method declared in the MintPlayer.Assertions namespace that returns a
/// <see cref="Task"/> (e.g. ThrowAsync, NotThrowAsync, CompleteWithinAsync) is used as a bare
/// expression statement, so its result is discarded. A discarded assertion Task means the test
/// passes even when the assertion fails.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnawaitedAssertionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.UnawaitedAssertionRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var taskType = compilationContext.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            if (taskType is null)
                return;

            compilationContext.RegisterSyntaxNodeAction(c => AnalyzeExpressionStatement(c, taskType), SyntaxKind.ExpressionStatement);
        });
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context, INamedTypeSymbol taskType)
    {
        var statement = (ExpressionStatementSyntax)context.Node;

        // An expression statement is by definition not awaited, returned or assigned.
        if (statement.Expression is not InvocationExpressionSyntax invocation)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (!SymbolHelpers.IsInAssertionsNamespace(method.ContainingType))
            return;

        if (!SymbolHelpers.IsTaskLike(method.ReturnType, taskType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.UnawaitedAssertionRule, invocation.GetLocation()));
    }
}
