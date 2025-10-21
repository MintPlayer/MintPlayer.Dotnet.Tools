using Microsoft.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

// Per-Compilation hub that every generator in this DLL can use.
public static class ComparerCacheHub
{
    private static readonly ConditionalWeakTable<Compilation, PerCompilationCache> _cwt = new();

    public static PerCompilationCache Get(Compilation compilation) =>
        _cwt.GetValue(compilation, static _ => new PerCompilationCache());
}