using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

/// <summary>
/// Shared symbol checks for the MintPlayer.Assertions analyzers.
/// </summary>
internal static class SymbolHelpers
{
    /// <summary>
    /// True when the symbol's containing namespace is <c>MintPlayer.Assertions</c> or a sub-namespace of it.
    /// </summary>
    public static bool IsInAssertionsNamespace(ISymbol? symbol)
    {
        var ns = symbol?.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace) return false;

        var display = ns.ToDisplayString();
        return display == "MintPlayer.Assertions"
            || display.StartsWith("MintPlayer.Assertions.", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="type"/> is <see cref="System.Threading.Tasks.Task"/> or derives from it
    /// (which covers <c>Task&lt;T&gt;</c>).
    /// </summary>
    public static bool IsTaskLike(ITypeSymbol? type, INamedTypeSymbol taskType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, taskType))
                return true;
        }
        return false;
    }
}
