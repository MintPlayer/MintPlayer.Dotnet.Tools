using System.Collections;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public interface ICompilationCache
{
    // Symbol-keyed (uses SymbolEqualityComparer.Default internally)
    T GetOrCreate<T>(Microsoft.CodeAnalysis.ITypeSymbol symbol, Func<Microsoft.CodeAnalysis.ITypeSymbol, T> factory)
        where T : class, IEqualityComparer;

    // Pair-of-symbols cache (e.g., source→destination mapping plans)
    T GetOrCreate<T>(SymbolPair pair, Func<SymbolPair, T> factory)
        where T : class, IEqualityComparer;

    // String-keyed general bucket
    T GetOrCreate<T>(string key, Func<T> factory)
        where T : class, IEqualityComparer;

    // (optional) Kind+Key bucket
    T GetOrCreate<T>(string kind, string key, Func<T> factory)
        where T : class, IEqualityComparer;
}