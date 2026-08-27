using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0003: a <c>new AssertionScope(...)</c> whose result is never disposed. An undisposed scope
/// swallows every failure collected inside it, so the test silently passes.
/// Detects the two simple, high-precision shapes:
/// <list type="bullet">
/// <item><c>new AssertionScope();</c> as a bare expression statement</item>
/// <item><c>var scope = new AssertionScope();</c> (no <c>using</c>) where the local is never referenced again</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AssertionScopeNotDisposedAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.AssertionScopeNotDisposedRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var scopeType = compilationContext.Compilation.GetTypeByMetadataName("MintPlayer.Assertions.AssertionScope");
            if (scopeType is null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                c => AnalyzeObjectCreation(c, scopeType),
                SyntaxKind.ObjectCreationExpression,
                SyntaxKind.ImplicitObjectCreationExpression);
        });
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, INamedTypeSymbol scopeType)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;

        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(type, scopeType))
            return;

        switch (creation.Parent)
        {
            // new AssertionScope(); — nothing ever disposes it
            case ExpressionStatementSyntax:
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.AssertionScopeNotDisposedRule, creation.GetLocation()));
                break;

            // var scope = new AssertionScope(); — a plain local declaration without `using`
            case EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Parent: LocalDeclarationStatementSyntax localDeclaration,
                    },
                } declarator,
            } when localDeclaration.UsingKeyword.IsKind(SyntaxKind.None):
                // High precision: if the local is referenced again later (scope.Dispose(), passed
                // along, wrapped in a using statement, ...) assume it gets disposed and stay quiet.
                var scopeName = declarator.Identifier.ValueText;
                var container = (SyntaxNode?)localDeclaration.FirstAncestorOrSelf<MemberDeclarationSyntax>()
                    ?? localDeclaration.SyntaxTree.GetRoot(context.CancellationToken);
                var referencedLater = container.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Any(id => id.Identifier.ValueText == scopeName && id.SpanStart > declarator.Span.End);
                if (!referencedLater)
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.AssertionScopeNotDisposedRule, localDeclaration.GetLocation()));
                break;
        }
    }
}
