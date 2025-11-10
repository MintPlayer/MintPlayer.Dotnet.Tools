using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public static class SourceProductionContextExtensions
{
    public static void ReportDiagnostic(this SourceProductionContext context, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic);
    }
}
