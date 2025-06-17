using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Data;

namespace MintPlayer.SourceGenerators.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedUsingsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(DiagnosticRules.UnusedUsingsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            compilationContext.RegisterSyntaxTreeAction(syntaxTreeContext =>
            {
                var semanticModel = compilationContext.Compilation.GetSemanticModel(syntaxTreeContext.Tree);
                var diagnostics = semanticModel.GetDiagnostics();

                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Id == "CS8019")
                    {
                        var root = syntaxTreeContext.Tree.GetRoot(syntaxTreeContext.CancellationToken);
                        var node = root.FindNode(diagnostic.Location.SourceSpan);
                        syntaxTreeContext.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
                    }
                }
            });
        });
    }
}
