using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0005: <c>foreach</c> over an expression whose static type is <c>IReadOnlyList&lt;T&gt;</c> or
/// <c>IList&lt;T&gt;</c>. Iterating through the interface boxes the underlying struct enumerator —
/// one heap allocation per call, on the passing path.
/// </summary>
/// <remarks>
/// <para>
/// This rule exists because the cost is <b>invisible in the source</b>. These two loops are
/// character-for-character identical:
/// </para>
/// <code>
/// List&lt;int&gt; a = ...;            foreach (var x in a) { }   // struct enumerator, no allocation
/// IReadOnlyList&lt;int&gt; b = ...;    foreach (var x in b) { }   // boxed enumerator, one allocation
/// </code>
/// <para>
/// Only the static type decides, and nothing at the call site says so. Neither review nor reading
/// the code catches it; only measuring does, and measuring only covers what someone remembered to
/// measure. A real instance of this survived in the collection assertions until an allocation test
/// happened to be extended to cover them.
/// </para>
/// <para>
/// Deliberately narrow. It fires only for <b>indexable</b> interfaces, where an indexed loop is a
/// free and obvious fix. Iterating an <c>IEnumerable&lt;T&gt;</c> parameter also boxes, but there is
/// often nothing the author can do about it, and a rule that cannot be acted on is noise that gets
/// suppressed wholesale — taking the actionable cases with it.
/// </para>
/// <para>
/// It also fires only inside <c>MintPlayer.Assertions</c> itself. Consumers iterate interfaces all
/// day and are right to; the boundary this protects ("a passing assertion allocates nothing") is
/// this library's promise, not theirs.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BoxedEnumeratorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.BoxedEnumeratorRule];

    /// <summary>The indexable collection interfaces. A struct enumerator behind either of these is boxed by foreach.</summary>
    private static readonly string[] IndexableInterfaces =
    [
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IList<T>",
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
    }

    private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        var statement = (ForEachStatementSyntax)context.Node;

        // Only guard this library's own code. Everyone else may iterate interfaces freely.
        if (!SymbolHelpers.IsInAssertionsNamespace(context.ContainingSymbol))
            return;

        var collectionType = context.SemanticModel.GetTypeInfo(statement.Expression, context.CancellationToken).Type;
        if (collectionType is not INamedTypeSymbol { TypeKind: TypeKind.Interface } named)
            return;

        if (!IsIndexable(named))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticRules.BoxedEnumeratorRule,
            statement.Expression.GetLocation(),
            statement.Expression.ToString(),
            named.ToDisplayString()));
    }

    /// <summary>
    /// True when the interface is, or inherits, an indexable collection interface — so the fix is a
    /// <c>for</c> loop over <c>Count</c> rather than a restructure.
    /// </summary>
    private static bool IsIndexable(INamedTypeSymbol type)
    {
        if (Matches(type)) return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (Matches(implemented)) return true;
        }

        return false;

        static bool Matches(INamedTypeSymbol candidate)
        {
            var definition = candidate.OriginalDefinition.ToDisplayString();
            foreach (var indexable in IndexableInterfaces)
            {
                if (definition == indexable) return true;
            }

            return false;
        }
    }
}
