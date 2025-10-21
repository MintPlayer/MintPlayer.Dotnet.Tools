using Microsoft.CodeAnalysis;
using System.Collections;
using System.Collections.Concurrent;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public sealed class PerCompilationCache
{
    internal PerCompilationCache() { }

    // Common: cache comparers keyed by ITypeSymbol
    private readonly ConcurrentDictionary<ITypeSymbol, Lazy<IEqualityComparer>> _byType = new(SymbolEqualityComparer.Default);

    // Optional: a general-purpose bucket if you also need other keys
    private readonly ConcurrentDictionary<(string Kind, string Key), Lazy<object>> _misc = new();

    // Misc bucket for CLR keys (rarely needed)
    private readonly ConcurrentDictionary<string, Lazy<object>> _byKey = new(StringComparer.Ordinal);

    // By pair
    private readonly ConcurrentDictionary<SymbolPair, Lazy<object>> _byPair = new();

    public IEqualityComparer GetOrCreate(ITypeSymbol type, Func<ITypeSymbol, IEqualityComparer> factory)
        => _byType.GetOrAdd(type, t => new Lazy<IEqualityComparer>(
                () => factory(t),
                LazyThreadSafetyMode.ExecutionAndPublication))
             .Value;

    public T GetOrCreate<T>(string kind, string key, Func<T> factory) where T : class
        => (T)_misc.GetOrAdd((kind, key), _ => new Lazy<object>(
                () => (IEqualityComparer)factory(),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    public T GetOrCreate<T>(string key, Func<T> factory) where T : class
        => (T)_byKey.GetOrAdd(key, _ => new Lazy<object>(
                () => (IEqualityComparer)factory(),
                LazyThreadSafetyMode.ExecutionAndPublication))
           .Value;

    public T GetOrCreate<T>(SymbolPair pair, Func<SymbolPair, T> factory) where T : class
        => (T)_byPair.GetOrAdd(pair, p => new Lazy<object>(
                () => factory(p), LazyThreadSafetyMode.ExecutionAndPublication))
           .Value;
}