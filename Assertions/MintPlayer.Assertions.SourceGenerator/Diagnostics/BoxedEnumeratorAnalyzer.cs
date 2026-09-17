using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0005: a <c>foreach</c> whose collection expression has an <b>interface</b> static type, which
/// boxes the underlying struct enumerator — one heap allocation per loop.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to MintPlayer.Assertions' own assembly. This is a rule about a performance-critical
/// library's internals, not advice for consumers: boxing one enumerator in a test is irrelevant,
/// boxing one per member per node in the equivalency walker is 27% of the allocation budget.
/// </para>
/// <para>
/// Only <b>indexable</b> interfaces are reported — <c>IReadOnlyList&lt;T&gt;</c>,
/// <c>IList&lt;T&gt;</c> and friends — because those have a realistic fix. A bare
/// <c>IEnumerable&lt;T&gt;</c> has nowhere better to go: the boxed enumerator is the only way to
/// walk it, so flagging it would be noise the author cannot act on.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BoxedEnumeratorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.BoxedEnumeratorRule];

    /// <summary>
    /// The interfaces worth reporting: each promises indexed access, so the author can reach a span
    /// or change the declared type. Deliberately excludes IEnumerable&lt;T&gt; and IQueryable.
    /// </summary>
    private static readonly HashSet<string> ActionableInterfaces =
    [
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.ICollection<T>",
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // Only this library polices itself. A consumer's foreach is their business.
            if (start.Compilation.AssemblyName != "MintPlayer.Assertions")
                return;

            start.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
        });
    }

    private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        var statement = (ForEachStatementSyntax)context.Node;

        var collectionType = context.SemanticModel
            .GetTypeInfo(statement.Expression, context.CancellationToken).Type;

        // Only an INTERFACE static type boxes. An array, a List<T>, a span — all fine, and all
        // indistinguishable from this one in the source, which is exactly why a human misses it.
        if (collectionType is not INamedTypeSymbol { TypeKind: TypeKind.Interface } named)
            return;

        var definition = named.OriginalDefinition.ToDisplayString();
        if (!ActionableInterfaces.Contains(definition))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticRules.BoxedEnumeratorRule,
            statement.Expression.GetLocation(),
            statement.Expression.ToString(),
            named.ToDisplayString()));
    }
}
