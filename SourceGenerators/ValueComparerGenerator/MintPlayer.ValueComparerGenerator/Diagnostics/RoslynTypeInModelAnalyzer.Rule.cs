using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.ValueComparerGenerator.Diagnostics;

public sealed partial class RoslynTypeInModelAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MINT001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "[AutoValueComparer] model holds a Roslyn type",
        messageFormat: "Property '{1}' of [AutoValueComparer] type '{0}' holds the Roslyn type '{2}'. Project it to a Roslyn-agnostic value (a string, a LocationKey, a PathSpec) instead.",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: """
            Incremental-pipeline models should be CLR-agnostic.
            If a property of an [AutoValueComparer] type (or of a type it reaches through collections, tuples or nested classes) holds a Microsoft.CodeAnalysis type
            (ISymbol, ITypeSymbol, SyntaxNode, Location, SemanticModel, Compilation, etc.), the model keeps a whole compilation alive between runs
            and its equality flaps across compilations, causing unstable incremental behavior.
            """
    );
}
