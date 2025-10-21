using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using MintPlayer.SourceGenerators.Tools.Extensions;
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

        // We look at the receiver (reduced extension) to ensure it's an Incremental*Provider<T>
        var receiverType = invocation.Instance?.Type as INamedTypeSymbol;
        if (receiverType is null)
            return;

        if (!IsIncrementalProvider(receiverType, out var elementType))
            return;

        // elementType is the T in Incremental(Value|Values)Provider<T>
        if (elementType is null)
            return;

        // Inspect T's properties for Roslyn types
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (TryFindRoslynProperty(elementType, out var offendingProp, out var offendingType, visited))
        {
            // Place the diagnostic on the invocation (or on the WithComparer identifier, if you want to be fancy)
            var diag = Rule.Create(invocation.Syntax.GetLocation(), [
                elementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                offendingProp?.Name ?? "<type>",
                offendingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ]);
            context.ReportDiagnostic(diag);
        }
    }

    private static bool IsIncrementalProvider(INamedTypeSymbol receiver, out ITypeSymbol? elementType)
    {
        elementType = null;

        if (receiver.Arity != 1)
            return false;

        var name = receiver.ConstructedFrom?.Name ?? receiver.Name;
        if (name is not ("IncrementalValueProvider" or "IncrementalValuesProvider"))
            return false;

        // Be strict on namespace: Microsoft.CodeAnalysis
        var ns = receiver.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!ns.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            return false;

        elementType = receiver.TypeArguments[0];
        return elementType is not null;
    }

    private static bool TryFindRoslynProperty(
        ITypeSymbol type,
        out IPropertySymbol? offendingProperty,
        out ITypeSymbol offendingType,
        HashSet<ITypeSymbol> visited)
    {
        offendingProperty = null;
        offendingType = type;

        // If T itself is a Roslyn type, we can flag immediately
        if (IsOrContainsRoslynType(type, visited))
        {
            offendingProperty = null;
            offendingType = type;
            return true;
        }

        // Walk instance properties (public & non-public; you can narrow if desired)
        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (prop.IsStatic) continue;

            var pType = prop.Type;
            if (IsOrContainsRoslynType(pType, visited))
            {
                offendingProperty = prop;
                offendingType = FindFirstRoslynLeaf(pType, visited) ?? pType;
                return true;
            }
        }

        return false;
    }

    private static bool IsOrContainsRoslynType(ITypeSymbol t, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(t))
            return false;

        // Direct Roslyn types
        if (IsRoslynNamespace(t))
            return true;

        switch (t)
        {
            case IArrayTypeSymbol arr:
                return IsOrContainsRoslynType(arr.ElementType, visited);

            case IPointerTypeSymbol ptr:
                return IsOrContainsRoslynType(ptr.PointedAtType, visited);

            case INamedTypeSymbol named:
                {
                    // Nullable<T>
                    if (named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
                        named.TypeArguments.Length == 1)
                    {
                        return IsOrContainsRoslynType(named.TypeArguments[0], visited);
                    }

                    // Tuples
                    if (named.IsTupleType)
                    {
                        foreach (var elem in named.TupleElements)
                            if (IsOrContainsRoslynType(elem.Type, visited))
                                return true;

                        return false;
                    }

                    // Generic type args
                    foreach (var ta in named.TypeArguments)
                        if (IsOrContainsRoslynType(ta, visited))
                            return true;

                    return false;
                }

            default:
                return false;
        }
    }

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
}