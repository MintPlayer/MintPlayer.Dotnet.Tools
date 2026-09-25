using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Generators;
using System.Collections.Immutable;

namespace MintPlayer.ValueComparerGenerator.Diagnostics;

/// <summary>
/// MINT001: a <c>[GenerateEquality]</c> model (or a type deriving from one) with a property that holds a Roslyn
/// type, directly or through an array, a nullable, a tuple item, a generic argument or a nested plain class.
/// </summary>
/// <remarks>
/// A nested type that is itself a model is not walked: it gets its own diagnostic on its own property, so one
/// offending property is reported once. The generator's MINT005 (reference equality only) leaves Roslyn types to
/// this rule for the same reason.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RoslynTypeInModelAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!Discovery.IsModel(type)) return;

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic) continue;

            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };
            if (!TryFindRoslynType(property.Type, visited, out var offendingType)) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                property.Locations.FirstOrDefault(),
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                property.Name,
                offendingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
    }

    /// <summary>Walks arrays, pointers, nullables, tuples, generic arguments and the properties of plain classes.</summary>
    private static bool TryFindRoslynType(ITypeSymbol type, HashSet<ITypeSymbol> visited, out ITypeSymbol offendingType)
    {
        offendingType = type;

        if (!visited.Add(type))
            return false;

        if (IsRoslynType(type)) return true;

        switch (type)
        {
            case IArrayTypeSymbol array:
                return TryFindRoslynType(array.ElementType, visited, out offendingType);

            case IPointerTypeSymbol pointer:
                return TryFindRoslynType(pointer.PointedAtType, visited, out offendingType);

            case INamedTypeSymbol named:
                if (named.IsTupleType)
                {
                    foreach (var element in named.TupleElements)
                        if (TryFindRoslynType(element.Type, visited, out offendingType))
                            return true;
                }

                // Also covers Nullable<T>.
                foreach (var argument in named.TypeArguments)
                    if (TryFindRoslynType(argument, visited, out offendingType))
                        return true;

                // A model declared in this compilation is analyzed on its own; walking it here would report the
                // same property twice.
                if (Discovery.IsModel(named) && named.Locations.Any(l => l.IsInSource))
                    return false;

                foreach (var property in named.GetAllProperties())
                {
                    if (property.IsStatic) continue;
                    if (TryFindRoslynType(property.Type, visited, out offendingType))
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Any type in a <c>Microsoft.CodeAnalysis</c> namespace. An unconstrained type parameter is not one.</summary>
    internal static bool IsRoslynType(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol) return false;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns == "Microsoft.CodeAnalysis" || ns.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal);
    }
}
