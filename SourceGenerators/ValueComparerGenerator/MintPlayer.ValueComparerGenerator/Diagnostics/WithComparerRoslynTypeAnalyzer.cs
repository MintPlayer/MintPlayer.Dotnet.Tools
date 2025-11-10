using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using MintPlayer.SourceGenerators.Tools;
using System.Collections.Immutable;

namespace MintPlayer.ValueComparerGenerator.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class WithComparerRoslynTypeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // We only care about methods named WithComparer
        if (!string.Equals(method.Name, "WithComparer", StringComparison.Ordinal) && !string.Equals(method.Name, "WithNullableComparer", StringComparison.Ordinal))
            return;

        // 1) Prefer the invocation's constructed return type
        if (!TryGetProviderElementType(invocation.Type as INamedTypeSymbol, out var elementType) &&
            !TryGetProviderElementType(method.ReturnType as INamedTypeSymbol, out elementType))
            return;


        //// 2) (Optional) Fallback: find the receiver type (reduced/non-reduced/syntax)
        //var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        //if (TryGetReceiverType(invocation, context, out var recvType) &&
        //    TryGetProviderElementType(recvType, out elementType))
        //{
        //    ReportIfRoslyn(elementType!, invocation, context);
        //}
        ReportIfRoslyn(elementType!, invocation, context);
    }

    private static void ReportIfRoslyn(ITypeSymbol elementType, IInvocationOperation invocation, OperationAnalysisContext context)
    {
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (TryFindRoslynProperty(elementType, out var offendingProp, out var offendingType, visited))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                elementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                offendingProp?.Name ?? "<type>",
                offendingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
    }

    private static bool TryGetProviderElementType(INamedTypeSymbol? provider, out ITypeSymbol? elementType)
    {
        elementType = null;
        if (provider is null || provider.Arity != 1) return false;

        var name = provider.OriginalDefinition.Name;
        var ns = provider.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if ((name == "IncrementalValueProvider" || name == "IncrementalValuesProvider") &&
            ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
        {
            elementType = provider.TypeArguments[0];
            return true;
        }
        return false;
    }

    // walk properties + generics recursively
    private static bool TryFindRoslynProperty(
        ITypeSymbol type,
        out IPropertySymbol? offendingProperty,
        out ITypeSymbol offendingType,
        HashSet<ITypeSymbol> visited)
    {
        offendingProperty = null;
        offendingType = type;

        if (!visited.Add(type))
            return false;

        if (IsRoslynType(type)) { offendingType = type; return true; }

        switch (type)
        {
            case IArrayTypeSymbol arr:
                return TryFindRoslynProperty(arr.ElementType, out offendingProperty, out offendingType, visited);

            case IPointerTypeSymbol ptr:
                return TryFindRoslynProperty(ptr.PointedAtType, out offendingProperty, out offendingType, visited);

            case INamedTypeSymbol named:
                // Nullable<T>
                if (named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
                    named.TypeArguments.Length == 1 &&
                    TryFindRoslynProperty(named.TypeArguments[0], out offendingProperty, out offendingType, visited))
                    return true;

                // Tuples
                if (named.IsTupleType)
                {
                    foreach (var el in named.TupleElements)
                        if (TryFindRoslynProperty(el.Type, out offendingProperty, out offendingType, visited))
                            return true;
                }

                // Generic args
                foreach (var ta in named.TypeArguments)
                    if (TryFindRoslynProperty(ta, out offendingProperty, out offendingType, visited))
                        return true;

                // Instance properties
                foreach (var prop in named.GetAllProperties())
                {
                    if (prop.IsStatic) continue;
                    if (TryFindRoslynProperty(prop.Type, out _, out offendingType, visited))
                    {
                        offendingProperty = prop;
                        return true;
                    }
                }

                // (Optional) instance fields too
                // foreach (var f in named.GetMembers().OfType<IFieldSymbol>())
                // {
                //     if (f.IsStatic) continue;
                //     if (TryFindRoslynProperty(f.Type, out _, out offendingType, visited))
                //     {
                //         offendingProperty = null;
                //         return true;
                //     }
                // }

                return false;

            default:
                return false;
        }
    }

    //private static bool IsOrContainsRoslynType(ITypeSymbol t, HashSet<ITypeSymbol> visited)
    //{
    //    if (!visited.Add(t))
    //        return false;

    //    // Direct Roslyn types
    //    if (IsRoslynNamespace(t))
    //        return true;

    //    switch (t)
    //    {
    //        case IArrayTypeSymbol arr:
    //            return IsOrContainsRoslynType(arr.ElementType, visited);

    //        case IPointerTypeSymbol ptr:
    //            return IsOrContainsRoslynType(ptr.PointedAtType, visited);

    //        case INamedTypeSymbol named:
    //            {
    //                // Nullable<T>
    //                if (named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
    //                    named.TypeArguments.Length == 1)
    //                {
    //                    return IsOrContainsRoslynType(named.TypeArguments[0], visited);
    //                }

    //                // Tuples
    //                if (named.IsTupleType)
    //                {
    //                    foreach (var elem in named.TupleElements)
    //                        if (IsOrContainsRoslynType(elem.Type, visited))
    //                            return true;

    //                    return false;
    //                }

    //                // Generic type args
    //                foreach (var ta in named.TypeArguments)
    //                    if (IsOrContainsRoslynType(ta, visited))
    //                        return true;

    //                var props = named.GetAllProperties();
    //                foreach (var prop in props)
    //                    if (!prop.IsStatic && IsOrContainsRoslynType(prop.Type, visited))
    //                        return true;

    //                return false;
    //            }

    //        default:
    //            return false;
    //    }
    //}

    private static ITypeSymbol? FindFirstRoslynLeaf(ITypeSymbol t, HashSet<ITypeSymbol> visited)
    {
        // Best-effort: find a representative Roslyn type within t for the message
        if (IsRoslynNamespace(t))
            return t;

        switch (t)
        {
            case IArrayTypeSymbol arr:
                return FindFirstRoslynLeaf(arr.ElementType, visited);

            case IPointerTypeSymbol ptr:
                return FindFirstRoslynLeaf(ptr.PointedAtType, visited);

            case INamedTypeSymbol named:
                {
                    if (named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
                        named.TypeArguments.Length == 1)
                    {
                        return FindFirstRoslynLeaf(named.TypeArguments[0], visited);
                    }

                    if (named.IsTupleType)
                    {
                        foreach (var elem in named.TupleElements)
                        {
                            var leaf = FindFirstRoslynLeaf(elem.Type, visited);
                            if (leaf is not null) return leaf;
                        }
                        return null;
                    }

                    foreach (var ta in named.TypeArguments)
                    {
                        var leaf = FindFirstRoslynLeaf(ta, visited);
                        if (leaf is not null) return leaf;
                    }

                    return null;
                }

            default:
                return null;
        }
    }

    private static bool IsRoslynNamespace(ITypeSymbol t)
    {
        // Unwrap type parameters softly: treat unknown T as not-Roslyn
        if (t is ITypeParameterSymbol) return false;

        var ns = t.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal);
    }

    private static bool IsRoslynType(ITypeSymbol t)
    {
        if (t is ITypeParameterSymbol) return false;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal);
    }

    private static bool TryGetReceiverType(IInvocationOperation invocation, OperationAnalysisContext context, out INamedTypeSymbol? receiverType)
    {
        receiverType = null;
        var method = invocation.TargetMethod;

        // 1) Reduced extension: provider.WithXxx(...)
        if (invocation.Instance?.Type is INamedTypeSymbol instType)
        {
            receiverType = instType;
            return true;
        }

        // 2) Sometimes Roslyn gives a good receiver type for reduced methods
        if (method.ReceiverType is INamedTypeSymbol recvFromMethod &&
            !SymbolEqualityComparer.Default.Equals(recvFromMethod, method.ContainingType)) // filter out static class
        {
            receiverType = recvFromMethod;
            return true;
        }

        // 3) Non-reduced static form: Extensions.WithXxx(provider, ...)
        if (method.IsExtensionMethod &&
            method.Parameters.Length > 0 &&
            method.Parameters[0].Type is INamedTypeSymbol thisParamType)
        {
            receiverType = thisParamType;
            return true;
        }

        // 4) Syntax fallback (works for reduced calls where 1–3 fail)
        //    Get the type of the expression on the left of the dot.
        var syntax = invocation.Syntax; // InvocationExpressionSyntax
        var model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
        if (syntax is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax ies &&
            ies.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax maes)
        {
            var t = model.GetTypeInfo(maes.Expression, context.CancellationToken).Type as INamedTypeSymbol;
            if (t is not null)
            {
                receiverType = t;
                return true;
            }
        }

        // 5) Final attempt: argument 0 type info (rare; sometimes the op tree hides it)
        if (invocation.Arguments.Length > 0 && invocation.Arguments[0].Value is IOperation arg0 &&
            arg0.Type is INamedTypeSymbol argType)
        {
            receiverType = argType;
            return true;
        }

        return false;
    }
}