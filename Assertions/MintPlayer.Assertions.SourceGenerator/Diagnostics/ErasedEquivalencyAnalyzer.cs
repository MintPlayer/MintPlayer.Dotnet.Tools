using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Threading;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// MPA0004: an object-graph equivalency call whose subject <em>and</em> expectation are both
/// deliberately cast to <see cref="object"/> — <c>((object)actual).Should().BeEquivalentTo((object)expected)</c>.
/// </summary>
/// <remarks>
/// The comparison still produces the right answer: the engine recovers the runtime type. What is
/// lost is silent. <c>EquivalencyScanner</c> excludes <see cref="object"/>, so no generated member
/// accessors are registered for such a call site and the comparison walks the reflection fallback —
/// the cost, and the trim/AOT safety, that the generated accessors exist to avoid. And with
/// <c>TExpectation</c> bound to <see cref="object"/> the options lambda is unusable: there is no
/// member to name in <c>Excluding(x => x.Prop)</c>.
///
/// Only <em>deliberate</em> erasure is reported, and only when both sides are erased. An expectation
/// that merely happens to be typed <see cref="object"/> (a generic helper, an extension author
/// forwarding a subject, a variable declared as <see cref="object"/>) is legitimate and stays quiet,
/// as does the <c>Should((object)actual).BeEquivalentTo(expected)</c> form the repo's own tests and
/// benchmarks use — a typed expectation still registers accessors.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ErasedEquivalencyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DiagnosticRules.ErasedEquivalencyRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntactic pre-filter before touching the semantic model: the call has to be
        // written as `<receiver>.BeEquivalentTo(...)`, and the receiver is where the erased
        // subject would live.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.ValueText is not ("BeEquivalentTo" or "NotBeEquivalentTo"))
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // StringAssertions.BeEquivalentTo is a different method entirely (case-insensitive string
        // compare, non-generic) and must never be flagged — hence the containing-type check
        // rather than a name check alone.
        if (method.ContainingType?.Name != "EquivalencyAssertionExtensions"
            || !SymbolHelpers.IsInAssertionsNamespace(method.ContainingType))
            return;

        var expectationIndex = IndexOfExpectationTypeParameter(method);
        if (expectationIndex < 0 || method.TypeArguments.Length <= expectationIndex)
            return;

        if (method.TypeArguments[expectationIndex].SpecialType != SpecialType.System_Object)
            return;

        if (!IsErasedToObject(ExpectationArgument(method, invocation), context.SemanticModel, context.CancellationToken))
            return;

        if (!SubjectIsErasedToObject(memberAccess.Expression, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.ErasedEquivalencyRule, invocation.GetLocation()));
    }

    /// <summary>
    /// Position of the <c>TExpectation</c> type parameter. Located by name rather than assumed to
    /// be first, because the collection overload is <c>BeEquivalentTo&lt;T, TExpectation&gt;</c>.
    /// </summary>
    private static int IndexOfExpectationTypeParameter(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        for (var i = 0; i < definition.TypeParameters.Length; i++)
        {
            if (definition.TypeParameters[i].Name == "TExpectation")
                return i;
        }

        return -1;
    }

    /// <summary>The syntax passed for the <c>expectation</c> parameter, named or positional.</summary>
    private static ExpressionSyntax? ExpectationArgument(IMethodSymbol method, InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;

        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == "expectation")
                return argument.Expression;
        }

        // Positional: the reduced extension method drops the subject parameter, a static call does not.
        var index = method.MethodKind == MethodKind.ReducedExtension ? 0 : 1;
        return arguments.Count > index && arguments[index].NameColon is null ? arguments[index].Expression : null;
    }

    /// <summary>
    /// True when the subject handed to <c>Should()</c> was deliberately cast to <see cref="object"/>.
    /// </summary>
    private static bool SubjectIsErasedToObject(ExpressionSyntax receiver, SemanticModel model, CancellationToken cancellationToken)
    {
        if (Unwrap(receiver) is not InvocationExpressionSyntax shouldInvocation)
            return false;

        var name = shouldInvocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
            _ => null,
        };
        if (name != "Should")
            return false;

        if (model.GetSymbolInfo(shouldInvocation, cancellationToken).Symbol is not IMethodSymbol should)
            return false;

        // `((object)x).Should()` — the subject is the member-access receiver.
        if (should.MethodKind == MethodKind.ReducedExtension)
        {
            return shouldInvocation.Expression is MemberAccessExpressionSyntax memberAccess
                && IsErasedToObject(memberAccess.Expression, model, cancellationToken);
        }

        // `Should((object)x)` — the subject is the first argument.
        var arguments = shouldInvocation.ArgumentList.Arguments;
        return arguments.Count > 0
            && arguments[0].NameColon is null
            && IsErasedToObject(arguments[0].Expression, model, cancellationToken);
    }

    /// <summary>True for an explicit <c>(object)</c> cast, parentheses ignored.</summary>
    private static bool IsErasedToObject(ExpressionSyntax? expression, SemanticModel model, CancellationToken cancellationToken)
        => Unwrap(expression) is CastExpressionSyntax cast
            && model.GetTypeInfo(cast.Type, cancellationToken).Type?.SpecialType == SpecialType.System_Object;

    private static ExpressionSyntax? Unwrap(ExpressionSyntax? expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }
}
