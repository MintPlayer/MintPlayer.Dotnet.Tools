using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.ValueComparerGenerator.Diagnostics;

public sealed partial class WithComparerRoslynTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MINT001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "WithComparer used with a type containing Roslyn symbols",
        messageFormat: "Type '{0}' used with WithComparer contains Roslyn types (e.g., property '{1}' of type '{2}'). Use a Roslyn-agnostic projection before calling WithComparer.",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Incremental .WithComparer(...) should be applied to CLR-agnostic models. " +
            "If the element type contains Microsoft.CodeAnalysis symbols (ISymbol, ITypeSymbol, SyntaxNode, Location, SemanticModel, Compilation, etc.), " +
            "equality may flap across compilations causing unstable incremental behavior."
    );
}
