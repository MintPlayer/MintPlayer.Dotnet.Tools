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
    /// True when <paramref name="type"/> is something the caller was meant to await:
    /// <see cref="System.Threading.Tasks.Task"/> or a type deriving from it (covering
    /// <c>Task&lt;T&gt;</c>), or any other awaitable the library returns.
    /// </summary>
    /// <remarks>
    /// The second case matters: <c>ThrowAsync</c> returns <c>ThrownExceptionTask&lt;T&gt;</c>
    /// rather than a Task, so that chaining does not force callers to restate an inferable type
    /// argument. Recognising awaitables structurally — by the presence of a <c>GetAwaiter</c>
    /// method — keeps MPA0001 working for it, and for any future custom awaitable, instead of
    /// silently going quiet.
    /// </remarks>
    public static bool IsTaskLike(ITypeSymbol? type, INamedTypeSymbol taskType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, taskType))
                return true;
        }

        return type is not null
            && IsInAssertionsNamespace(type)
            && type.GetMembers("GetAwaiter").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0);
    }
}
