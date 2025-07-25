using Microsoft.CodeAnalysis;
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
        // We register an action that will be executed for each semantic model.
        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        // The compiler itself already calculates which usings are unnecessary.
        // This is exposed as diagnostic CS8019.
        // We can leverage the compiler's work instead of re-implementing the logic.
        // This is the most reliable way to handle all edge cases correctly.
        var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);

        // Find all diagnostics from the compiler that report an unused using directive.
        var unusedUsingDiagnostics = diagnostics.Where(d => d.Id == "CS8019");

        foreach (var unusedUsingDiagnostic in unusedUsingDiagnostics)
        {
            // We need to get the syntax root to find the node associated with the diagnostic.
            if (semanticModel.SyntaxTree.GetRoot(cancellationToken) is not SyntaxNode root)
            {
                continue;
            }

            var usingDirectiveNode = root.FindNode(unusedUsingDiagnostic.Location.SourceSpan);

            // Ensure the node is actually a UsingDirectiveSyntax.
            if (usingDirectiveNode is UsingDirectiveSyntax usingDirective)
            {
                // Report our own diagnostic (MP001) at the same location.
                // This allows our custom code fix to be triggered by the compiler's findings.
                var customDiagnostic = Diagnostic.Create(
                    DiagnosticRules.UnusedUsingsRule,
                    usingDirective.GetLocation(),
                    usingDirective.Name.ToString());

                context.ReportDiagnostic(customDiagnostic);
            }
        }
    }
}
